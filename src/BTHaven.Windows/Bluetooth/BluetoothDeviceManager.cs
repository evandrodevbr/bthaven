using System.Runtime.CompilerServices;
using System.Threading.Channels;
using BTHaven.Core.Contracts;
using BTHaven.Core.Devices;
using BTHaven.Windows.Diagnostics;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace BTHaven.Windows.Bluetooth;

public sealed class BluetoothDeviceManager : IBluetoothDeviceService, IAsyncDisposable
{
    private static readonly IReadOnlyList<string> RequestedProperties =
    [
        WindowsDevicePropertyNames.IsConnected,
        WindowsDevicePropertyNames.IsPaired,
        WindowsDevicePropertyNames.IsPresent,
        WindowsDevicePropertyNames.DeviceAddress,
        WindowsDevicePropertyNames.Manufacturer,
        WindowsDevicePropertyNames.ModelName,
        WindowsDevicePropertyNames.SignalStrength,
        WindowsDevicePropertyNames.ContainerId,
        WindowsDevicePropertyNames.Category,
        WindowsDevicePropertyNames.BatteryLife,
        WindowsDevicePropertyNames.BatteryPlusCharging,
        WindowsDevicePropertyNames.ChargingState,
    ];

    private readonly object sync = new();
    private readonly Dictionary<string, BluetoothDeviceObservation> endpointObservations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Channel<BluetoothDeviceChange> changes = Channel.CreateUnbounded<BluetoothDeviceChange>(new UnboundedChannelOptions
    {
        SingleReader = false,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });
    private readonly IWindowsDiagnosticLogger logger;
    private Task? startTask;
    private DeviceWatcher? classicWatcher;
    private DeviceWatcher? bleWatcher;
    private bool disposed;

    public BluetoothDeviceManager(IWindowsDiagnosticLogger? logger = null)
    {
        this.logger = logger ?? NullDiagnosticLogger.Instance;
    }

    public async Task<IReadOnlyList<BluetoothDeviceModel>> GetDevicesAsync(
        BluetoothDeviceFilter filter = BluetoothDeviceFilter.Connected,
        CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);

        lock (sync)
        {
            return BuildModelsLocked()
                .Values
                .Where(device => BluetoothDeviceFilterMatcher.Matches(device, filter))
                .OrderByDescending(device => device.IsConnected)
                .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public async IAsyncEnumerable<BluetoothDeviceChange> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var change in changes.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return change;
        }
    }

    public ValueTask DisposeAsync()
    {
        DeviceWatcher?[] watchers;
        lock (sync)
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }

            disposed = true;
            watchers = [classicWatcher, bleWatcher];
            classicWatcher = null;
            bleWatcher = null;
        }

        foreach (var watcher in watchers)
        {
            if (watcher is null)
            {
                continue;
            }

            try
            {
                watcher.Stop();
            }
            catch (Exception exception)
            {
                logger.Error("Bluetooth.DeviceWatcher.StopFailed", exception, new Dictionary<string, object?>
                {
                    ["status"] = watcher.Status.ToString(),
                });
            }
        }

        changes.Writer.TryComplete();
        logger.Info("Bluetooth.DeviceManager.Disposed", new Dictionary<string, object?>());
        return ValueTask.CompletedTask;
    }

    private Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        Task task;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            startTask ??= StartCoreAsync();
            task = startTask;
        }

        return task.WaitAsync(cancellationToken);
    }

    private async Task StartCoreAsync()
    {
        var classicEnumeration = NewCompletionSource();
        var bleEnumeration = NewCompletionSource();
        DeviceWatcher classic;
        DeviceWatcher ble;

        try
        {
            classic = DeviceInformation.CreateWatcher(
                BluetoothDevice.GetDeviceSelectorFromPairingState(true),
                RequestedProperties);
            ble = DeviceInformation.CreateWatcher(
                BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
                RequestedProperties);

            classic.Added += (_, device) => HandleAdded(device, BluetoothTransport.Classic);
            classic.Updated += (_, update) => HandleUpdated(update, BluetoothTransport.Classic);
            classic.Removed += (_, update) => HandleRemoved(update.Id, BluetoothTransport.Classic);
            classic.EnumerationCompleted += (_, _) => HandleEnumerationCompleted("classic", classic, classicEnumeration);
            classic.Stopped += (_, _) => HandleStopped("classic", classic);

            ble.Added += (_, device) => HandleAdded(device, BluetoothTransport.LowEnergy);
            ble.Updated += (_, update) => HandleUpdated(update, BluetoothTransport.LowEnergy);
            ble.Removed += (_, update) => HandleRemoved(update.Id, BluetoothTransport.LowEnergy);
            ble.EnumerationCompleted += (_, _) => HandleEnumerationCompleted("ble", ble, bleEnumeration);
            ble.Stopped += (_, _) => HandleStopped("ble", ble);

            lock (sync)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                classicWatcher = classic;
                bleWatcher = ble;
            }

            logger.Info("Bluetooth.DeviceWatchers.Starting", new Dictionary<string, object?>
            {
                ["classicSelector"] = BluetoothDevice.GetDeviceSelectorFromPairingState(true),
                ["bleSelector"] = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
            });
            classic.Start();
            ble.Start();
            await Task.WhenAll(classicEnumeration.Task, bleEnumeration.Task).ConfigureAwait(false);
            logger.Info("Bluetooth.DeviceWatchers.Ready", new Dictionary<string, object?>
            {
                ["classicStatus"] = classic.Status.ToString(),
                ["bleStatus"] = ble.Status.ToString(),
            });
        }
        catch (Exception exception)
        {
            logger.Error("Bluetooth.DeviceWatchers.StartFailed", exception);
            throw;
        }
    }

    private void HandleAdded(DeviceInformation device, BluetoothTransport transport)
    {
        try
        {
            var observation = WindowsBluetoothDeviceObservationFactory.FromDeviceInformation(device, transport);
            ApplyObservation(observation);
        }
        catch (Exception exception)
        {
            logger.Error("Bluetooth.Device.AddFailed", exception, new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
                ["name"] = device.Name,
                ["transport"] = transport.ToString(),
            });
        }
    }

    private void HandleUpdated(DeviceInformationUpdate update, BluetoothTransport transport)
    {
        try
        {
            BluetoothDeviceObservation? previous;
            lock (sync)
            {
                endpointObservations.TryGetValue(update.Id, out previous);
            }

            if (previous is null)
            {
                logger.Info("Bluetooth.Device.UpdateIgnored", new Dictionary<string, object?>
                {
                    ["deviceId"] = update.Id,
                    ["transport"] = transport.ToString(),
                    ["reason"] = "Update arrived before Added or after Removed",
                });
                return;
            }

            ApplyObservation(WindowsBluetoothDeviceObservationFactory.FromUpdate(previous, update));
        }
        catch (Exception exception)
        {
            logger.Error("Bluetooth.Device.UpdateFailed", exception, new Dictionary<string, object?>
            {
                ["deviceId"] = update.Id,
                ["transport"] = transport.ToString(),
            });
        }
    }

    private void HandleRemoved(string endpointId, BluetoothTransport transport)
    {
        BluetoothDeviceModel? before;
        BluetoothDeviceModel? after;
        lock (sync)
        {
            if (!endpointObservations.TryGetValue(endpointId, out var previous))
            {
                return;
            }

            var logicalKey = LogicalKey(previous);
            before = BuildModelLocked(logicalKey);
            endpointObservations.Remove(endpointId);
            after = BuildModelLocked(logicalKey);
        }

        PublishChange(before, after);
        logger.Info("Bluetooth.Device.Removed", new Dictionary<string, object?>
        {
            ["deviceId"] = endpointId,
            ["transport"] = transport.ToString(),
            ["logicalKey"] = before?.ContainerId ?? endpointId,
        });
    }

    private void ApplyObservation(BluetoothDeviceObservation observation)
    {
        BluetoothDeviceModel? before;
        BluetoothDeviceModel? after;
        BluetoothDeviceModel? oldGroupAfter = null;
        string? oldLogicalKey;
        var newLogicalKey = LogicalKey(observation);

        lock (sync)
        {
            endpointObservations.TryGetValue(observation.Id, out var previous);
            oldLogicalKey = previous is null ? null : LogicalKey(previous);
            before = oldLogicalKey is null ? null : BuildModelLocked(oldLogicalKey);

            if (previous is not null && !string.Equals(oldLogicalKey, newLogicalKey, StringComparison.OrdinalIgnoreCase))
            {
                endpointObservations.Remove(observation.Id);
            }

            endpointObservations[observation.Id] = observation;
            after = BuildModelLocked(newLogicalKey);
            if (oldLogicalKey is not null && !string.Equals(oldLogicalKey, newLogicalKey, StringComparison.OrdinalIgnoreCase))
            {
                oldGroupAfter = BuildModelLocked(oldLogicalKey);
            }
        }

        var logicalKeyChanged = oldLogicalKey is not null
            && !string.Equals(oldLogicalKey, newLogicalKey, StringComparison.OrdinalIgnoreCase);
        if (logicalKeyChanged)
        {
            PublishChange(before, oldGroupAfter);
            PublishChange(null, after);
        }
        else
        {
            PublishChange(before, after);
        }

        logger.Info(before is null ? "Bluetooth.Device.Added" : "Bluetooth.Device.Updated", new Dictionary<string, object?>
        {
            ["deviceId"] = observation.Id,
            ["name"] = observation.Name,
            ["transport"] = observation.Transport.ToString(),
            ["isPaired"] = observation.IsPaired,
            ["isConnected"] = observation.IsConnected,
            ["isPresent"] = observation.IsPresent,
            ["logicalKey"] = newLogicalKey,
        });
    }

    private void PublishChange(BluetoothDeviceModel? before, BluetoothDeviceModel? after)
    {
        BluetoothDeviceChange? change = before is null && after is not null
            ? new BluetoothDeviceChange { Kind = BluetoothDeviceChangeKind.Added, DeviceId = after.Id, Device = after }
            : before is not null && after is null
                ? new BluetoothDeviceChange { Kind = BluetoothDeviceChangeKind.Removed, DeviceId = before.Id }
                : before is not null && after is not null
                    ? new BluetoothDeviceChange { Kind = BluetoothDeviceChangeKind.Updated, DeviceId = after.Id, Device = after }
                    : null;

        if (change is not null)
        {
            changes.Writer.TryWrite(change);
        }
    }

    private Dictionary<string, BluetoothDeviceModel> BuildModelsLocked()
    {
        return endpointObservations.Values
            .GroupBy(LogicalKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, Merge, StringComparer.OrdinalIgnoreCase);
    }

    private BluetoothDeviceModel? BuildModelLocked(string logicalKey)
    {
        var group = endpointObservations.Values
            .Where(observation => string.Equals(LogicalKey(observation), logicalKey, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return group.Length == 0 ? null : Merge(group);
    }

    private static BluetoothDeviceModel Merge(IEnumerable<BluetoothDeviceObservation> observations)
    {
        var items = observations.ToArray();
        var first = items[0];
        var hasClassic = items.Any(item => item.Transport is BluetoothTransport.Classic or BluetoothTransport.DualMode);
        var hasBle = items.Any(item => item.Transport is BluetoothTransport.LowEnergy or BluetoothTransport.DualMode);
        var transport = hasClassic && hasBle
            ? BluetoothTransport.DualMode
            : hasClassic
                ? BluetoothTransport.Classic
                : hasBle
                    ? BluetoothTransport.LowEnergy
                    : BluetoothTransport.Unknown;
        var capabilities = items.Aggregate(BluetoothCapabilities.None, (current, item) => current | item.Capabilities);
        if (hasClassic)
        {
            capabilities |= BluetoothCapabilities.Classic;
        }
        if (hasBle)
        {
            capabilities |= BluetoothCapabilities.Ble;
        }

        return new BluetoothDeviceModel
        {
            Id = first.Id,
            ContainerId = items.Select(item => item.ContainerId).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            Name = items.Select(item => item.Name).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "Bluetooth device",
            Manufacturer = items.Select(item => item.Manufacturer).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            Model = items.Select(item => item.Model).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            Address = items.Select(item => item.Address).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            Transport = transport,
            Category = items.Select(item => item.Category).FirstOrDefault(value => value != BluetoothDeviceCategory.Unknown),
            IsPaired = items.Any(item => item.IsPaired == true),
            IsConnected = items.Any(item => item.IsConnected == true),
            IsPresent = items.Any(item => item.IsPresent == true),
            Rssi = items.Select(item => item.Rssi).FirstOrDefault(value => value.HasValue),
            Capabilities = capabilities,
            Services = items.SelectMany(item => item.Services).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToArray(),
            Profiles = items.SelectMany(item => item.Profiles).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToArray(),
            LastUpdated = items.Max(item => item.ObservedAt),
        };
    }

    private static string LogicalKey(BluetoothDeviceObservation observation)
    {
        return string.IsNullOrWhiteSpace(observation.ContainerId) ? observation.Id : observation.ContainerId;
    }

    private static TaskCompletionSource<bool> NewCompletionSource()
    {
        return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private void HandleEnumerationCompleted(string transport, DeviceWatcher watcher, TaskCompletionSource<bool> completion)
    {
        logger.Info("Bluetooth.DeviceWatcher.EnumerationCompleted", new Dictionary<string, object?>
        {
            ["transport"] = transport,
            ["status"] = watcher.Status.ToString(),
        });
        completion.TrySetResult(true);
    }

    private void HandleStopped(string transport, DeviceWatcher watcher)
    {
        logger.Info("Bluetooth.DeviceWatcher.Stopped", new Dictionary<string, object?>
        {
            ["transport"] = transport,
            ["status"] = watcher.Status.ToString(),
        });
    }
}
