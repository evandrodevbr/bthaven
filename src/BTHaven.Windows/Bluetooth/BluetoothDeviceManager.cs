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
    private readonly Dictionary<string, string> endpointLogicalKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> reliableIdentityLogicalKeys = new(StringComparer.OrdinalIgnoreCase);
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
        logger.Info("Bluetooth.Snapshot.Requested", new Dictionary<string, object?>
        {
            ["filter"] = filter.ToString(),
        });
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<BluetoothDeviceModel> result;
        lock (sync)
        {
            result = OrderDevices(
                BuildModelsLocked().Values
                    .Where(device => BluetoothDeviceFilterMatcher.Matches(device, filter)));
        }
        logger.Info("Bluetooth.Snapshot.Completed", new Dictionary<string, object?>
        {
            ["filter"] = filter.ToString(),
            ["count"] = result.Count,
            ["connected"] = result.Count(device => device.IsConnected),
            ["paired"] = result.Count(device => device.IsPaired),
        });
        return result;
    }

    public async IAsyncEnumerable<BluetoothDeviceChange> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        logger.Info("Bluetooth.Watch.Subscribed");
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var change in changes.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return change;
        }
        logger.Info("Bluetooth.Watch.Completed");
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

    internal void ApplyObservationForTesting(BluetoothDeviceObservation observation) =>
        ApplyObservation(observation);

    internal void RemoveObservationForTesting(string endpointId, BluetoothTransport transport) =>
        HandleRemoved(endpointId, transport);

    internal IReadOnlyList<BluetoothDeviceModel> GetModelsForTesting()
    {
        lock (sync)
        {
            return OrderDevices(BuildModelsLocked().Values);
        }
    }

    private static BluetoothDeviceModel[] OrderDevices(IEnumerable<BluetoothDeviceModel> devices) =>
        devices
            .OrderByDescending(device => device.IsConnected)
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal bool TryReadChangeForTesting(out BluetoothDeviceChange? change) =>
        changes.Reader.TryRead(out change);

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
                var endpointKey = EndpointKey(update.Id, transport);
                if (!endpointObservations.TryGetValue(endpointKey, out previous))
                {
                    logger.Info("Bluetooth.Device.UpdateIgnored", new Dictionary<string, object?>
                    {
                        ["deviceId"] = update.Id,
                        ["transport"] = transport.ToString(),
                        ["reason"] = "Update arrived before Added or after Removed",
                    });
                    return;
                }
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
            var endpointKey = EndpointKey(endpointId, transport);
            if (!endpointObservations.TryGetValue(endpointKey, out var previous))
            {
                return;
            }

            var logicalKey = LogicalKey(previous);
            before = BuildModelLocked(logicalKey);
            endpointObservations.Remove(endpointKey);
            endpointLogicalKeys.Remove(endpointKey);
            after = BuildModelLocked(logicalKey);
            RemoveReliableBindingsIfUnused(logicalKey);
        }

        PublishChange(before, after, endpointId);
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
        BluetoothDeviceObservation effectiveObservation;
        string? oldLogicalKey;
        string newLogicalKey;

        lock (sync)
        {
            var endpointKey = EndpointKey(observation.Id, observation.Transport);
            var hadEndpointBinding = endpointLogicalKeys.ContainsKey(endpointKey);
            endpointObservations.TryGetValue(endpointKey, out var previous);
            effectiveObservation = previous is null
                ? observation
                : RetainPreviousIdentityIfIncomplete(previous, observation);
            oldLogicalKey = previous is null ? null : LogicalKey(previous);
            var identityConflict = previous is not null
                && HasReliableIdentity(previous)
                && HasReliableIdentity(observation)
                && !ReliableIdentityMatches(previous, observation);

            if (identityConflict)
            {
                before = BuildModelLocked(oldLogicalKey!);
                endpointObservations.Remove(endpointKey);
                endpointLogicalKeys.Remove(endpointKey);
                RemoveReliableBindingsIfUnused(oldLogicalKey!);
                newLogicalKey = ResolveLogicalKey(effectiveObservation);
            }
            else
            {
                newLogicalKey = oldLogicalKey ?? ResolveLogicalKey(effectiveObservation);
                before = BuildModelLocked(newLogicalKey);
            }

            endpointObservations[endpointKey] = effectiveObservation;
            if (hadEndpointBinding || (previous is null && !HasReliableIdentity(effectiveObservation)))
            {
                endpointLogicalKeys[endpointKey] = newLogicalKey;
            }

            BindReliableIdentities(
                effectiveObservation,
                newLogicalKey,
                previous is not null && !HasReliableIdentity(previous) && HasReliableIdentity(effectiveObservation));
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
            ["deviceId"] = effectiveObservation.Id,
            ["name"] = effectiveObservation.Name,
            ["transport"] = effectiveObservation.Transport.ToString(),
            ["isPaired"] = effectiveObservation.IsPaired,
            ["isConnected"] = effectiveObservation.IsConnected,
            ["isPresent"] = effectiveObservation.IsPresent,
            ["address"] = effectiveObservation.Address,
            ["containerId"] = effectiveObservation.ContainerId,
            ["manufacturer"] = effectiveObservation.Manufacturer,
            ["model"] = effectiveObservation.Model,
            ["rssi"] = effectiveObservation.Rssi,
            ["category"] = effectiveObservation.Category.ToString(),
            ["categories"] = effectiveObservation.Categories,
            ["capabilities"] = effectiveObservation.Capabilities.ToString(),
            ["logicalKey"] = newLogicalKey,
        });
    }

    private static BluetoothDeviceObservation RetainPreviousIdentityIfIncomplete(
        BluetoothDeviceObservation previous,
        BluetoothDeviceObservation current)
    {
        if (!string.IsNullOrWhiteSpace(current.ContainerId))
        {
            return current;
        }

        if (!string.IsNullOrWhiteSpace(previous.ContainerId))
        {
            var currentAddress = BluetoothDeviceIdentity.NormalizeAddress(current.Address);
            var previousAddress = BluetoothDeviceIdentity.NormalizeAddress(previous.Address);
            if (currentAddress.Length > 0
                && previousAddress.Length > 0
                && !string.Equals(currentAddress, previousAddress, StringComparison.Ordinal))
            {
                return current;
            }

            return current with
            {
                ContainerId = previous.ContainerId,
                Address = string.IsNullOrWhiteSpace(current.Address) ? previous.Address : current.Address,
            };
        }

        return string.IsNullOrWhiteSpace(current.Address) && !string.IsNullOrWhiteSpace(previous.Address)
            ? current with { Address = previous.Address }
            : current;
    }

    private string ResolveLogicalKey(BluetoothDeviceObservation observation)
    {
        if (TryGetReliableLogicalKey(observation, out var logicalKey))
        {
            return logicalKey;
        }

        return BluetoothDeviceIdentity.GetLogicalId(observation);
    }

    private string LogicalKey(BluetoothDeviceObservation observation)
    {
        var endpointKey = EndpointKey(observation.Id, observation.Transport);
        return endpointLogicalKeys.TryGetValue(endpointKey, out var logicalKey)
            ? logicalKey
            : ResolveLogicalKey(observation);
    }

    private bool TryGetReliableLogicalKey(BluetoothDeviceObservation observation, out string logicalKey)
    {
        var containerId = NormalizeContainerId(observation.ContainerId);
        if (containerId.Length > 0)
        {
            if (reliableIdentityLogicalKeys.TryGetValue($"container:{containerId}", out logicalKey!))
            {
                return true;
            }

            logicalKey = string.Empty;
            return false;
        }

        var address = BluetoothDeviceIdentity.NormalizeAddress(observation.Address);
        if (address.Length > 0
            && reliableIdentityLogicalKeys.TryGetValue($"address:{address}", out logicalKey!))
        {
            return true;
        }

        logicalKey = string.Empty;
        return false;
    }

    private void BindReliableIdentities(
        BluetoothDeviceObservation observation,
        string logicalKey,
        bool overwrite)
    {
        var containerId = NormalizeContainerId(observation.ContainerId);
        if (containerId.Length > 0)
        {
            BindReliableIdentity($"container:{containerId}", logicalKey, overwrite);
        }

        var address = BluetoothDeviceIdentity.NormalizeAddress(observation.Address);
        if (address.Length > 0)
        {
            BindReliableIdentity($"address:{address}", logicalKey, overwrite);
        }
    }

    private void BindReliableIdentity(string identityKey, string logicalKey, bool overwrite)
    {
        if (overwrite || !reliableIdentityLogicalKeys.ContainsKey(identityKey))
        {
            reliableIdentityLogicalKeys[identityKey] = logicalKey;
        }
    }

    private void RemoveReliableBindingsIfUnused(string logicalKey)
    {
        if (endpointObservations.Values.Any(observation =>
                string.Equals(LogicalKey(observation), logicalKey, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        foreach (var identityKey in reliableIdentityLogicalKeys
                     .Where(pair => string.Equals(pair.Value, logicalKey, StringComparison.OrdinalIgnoreCase))
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            reliableIdentityLogicalKeys.Remove(identityKey);
        }
    }

    private static bool HasReliableIdentity(BluetoothDeviceObservation observation) =>
        !string.IsNullOrWhiteSpace(observation.ContainerId)
        || !string.IsNullOrWhiteSpace(observation.Address);

    private static bool ReliableIdentityMatches(
        BluetoothDeviceObservation previous,
        BluetoothDeviceObservation current)
    {
        var previousContainerId = NormalizeContainerId(previous.ContainerId);
        var currentContainerId = NormalizeContainerId(current.ContainerId);
        if (previousContainerId.Length > 0 && currentContainerId.Length > 0)
        {
            return string.Equals(previousContainerId, currentContainerId, StringComparison.Ordinal);
        }

        var previousAddress = BluetoothDeviceIdentity.NormalizeAddress(previous.Address);
        var currentAddress = BluetoothDeviceIdentity.NormalizeAddress(current.Address);
        return previousAddress.Length > 0
            && currentAddress.Length > 0
            && string.Equals(previousAddress, currentAddress, StringComparison.Ordinal);
    }

    private static string NormalizeContainerId(string? containerId) =>
        containerId?.Trim().ToUpperInvariant() ?? string.Empty;

    private static string EndpointKey(string endpointId, BluetoothTransport transport) =>
        $"{transport}:{endpointId}";

    private void PublishChange(
        BluetoothDeviceModel? before,
        BluetoothDeviceModel? after,
        string? endpointId = null)
    {
        BluetoothDeviceChange? change = before is null && after is not null
            ? new BluetoothDeviceChange
            {
                Kind = BluetoothDeviceChangeKind.Added,
                DeviceId = after.Id,
                EndpointId = endpointId,
                Device = after,
            }
            : before is not null && after is null
                ? new BluetoothDeviceChange
                {
                    Kind = BluetoothDeviceChangeKind.Removed,
                    DeviceId = before.Id,
                    EndpointId = endpointId,
                }
                : before is not null && after is not null
                    ? new BluetoothDeviceChange
                    {
                        Kind = BluetoothDeviceChangeKind.Updated,
                        DeviceId = after.Id,
                        EndpointId = endpointId,
                        Device = after,
                    }
                    : null;

        if (change is not null)
        {
            changes.Writer.TryWrite(change);
        }
    }

    private Dictionary<string, BluetoothDeviceModel> BuildModelsLocked()
    {
        return endpointObservations.Values
            .GroupBy(observation => LogicalKey(observation), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => Merge(group.Key, group), StringComparer.OrdinalIgnoreCase);
    }

    private BluetoothDeviceModel? BuildModelLocked(string logicalKey)
    {
        var group = endpointObservations.Values
            .Where(observation => string.Equals(LogicalKey(observation), logicalKey, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return group.Length == 0 ? null : Merge(logicalKey, group);
    }

    private static BluetoothDeviceModel Merge(
        string logicalKey,
        IEnumerable<BluetoothDeviceObservation> observations)
    {
        var items = observations
            .OrderBy(item => item.Transport)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var preferredItems = items
            .OrderByDescending(item => item.IsConnected == true)
            .ThenByDescending(item => item.IsPresent == true)
            .ThenByDescending(item => item.ObservedAt)
            .ThenBy(item => item.Transport)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
            Id = logicalKey,
            ContainerId = preferredItems.Select(item => item.ContainerId).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            Name = preferredItems.Select(item => item.Name).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "Bluetooth device",
            Manufacturer = preferredItems.Select(item => item.Manufacturer).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            Model = preferredItems.Select(item => item.Model).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            Address = preferredItems.Select(item => item.Address).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            Transport = transport,
            Category = preferredItems.Select(item => item.Category).FirstOrDefault(value => value != BluetoothDeviceCategory.Unknown),
            IsPaired = items.Any(item => item.IsPaired == true),
            IsConnected = items.Any(item => item.IsConnected == true),
            IsPresent = items.Any(item => item.IsPresent == true),
            Rssi = preferredItems.Select(item => item.Rssi).FirstOrDefault(value => value.HasValue),
            Capabilities = capabilities,
            Endpoints = items
                .Select(item => new BluetoothEndpointReference
                {
                    Id = item.Id,
                    Transport = item.Transport,
                    ContainerId = item.ContainerId,
                    Address = item.Address,
                    IsConnected = item.IsConnected,
                    IsPresent = item.IsPresent,
                    ObservedAt = item.ObservedAt,
                })
                .ToArray(),
            Services = items.SelectMany(item => item.Services).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToArray(),
            Profiles = items.SelectMany(item => item.Profiles).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToArray(),
            LastUpdated = items.Max(item => item.ObservedAt),
        };
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
