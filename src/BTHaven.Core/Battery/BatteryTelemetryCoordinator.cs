using BTHaven.Core.Contracts;
using BTHaven.Core.Devices;

namespace BTHaven.Core.Battery;

public enum BatteryTelemetryStatus
{
    NotRequested,
    Loading,
    Available,
    Unavailable,
    Failed,
}

public sealed record BatteryTelemetryEntry
{
    public required string DeviceId { get; init; }
    public BatteryTelemetryStatus Status { get; init; }
    public BatteryState? Current { get; init; }
    public BatteryState? LastAvailable { get; init; }
    public DateTimeOffset AttemptedAt { get; init; }
}

public sealed class BatteryTelemetryChangedEventArgs : EventArgs
{
    public required BatteryTelemetryEntry Entry { get; init; }
}

public sealed class BatteryTelemetryCoordinator
{
    private readonly object gate = new();
    private readonly IBatteryService service;
    private readonly int maxConcurrency;
    private readonly SemaphoreSlim concurrency;
    private readonly Dictionary<string, BatteryTelemetryEntry> entries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> generations =
        new(StringComparer.OrdinalIgnoreCase);

    public BatteryTelemetryCoordinator(IBatteryService service, int maxConcurrency = 2)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);
        this.service = service;
        this.maxConcurrency = maxConcurrency;
        concurrency = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    public event EventHandler<BatteryTelemetryChangedEventArgs>? Changed;

    public bool TryGet(string deviceId, out BatteryTelemetryEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        lock (gate)
        {
            if (entries.TryGetValue(deviceId, out var found))
            {
                entry = found;
                return true;
            }
        }

        entry = null!;
        return false;
    }

    public async Task RefreshAsync(
        IReadOnlyList<BluetoothDeviceModel> devices,
        string? priorityDeviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);
        cancellationToken.ThrowIfCancellationRequested();

        var orderedDevices = devices
            .Where(device => device.IsPresent)
            .OrderByDescending(device => SameId(device.Id, priorityDeviceId))
            .ThenByDescending(device => device.IsConnected)
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (orderedDevices.Length == 0)
        {
            return;
        }

        var work = Prepare(orderedDevices);
        foreach (var item in work)
        {
            RaiseLoadingIfCurrent(item);
        }

        var nextIndex = -1;
        async Task RunWorkerAsync()
        {
            while (true)
            {
                var index = Interlocked.Increment(ref nextIndex);
                if (index >= work.Length)
                {
                    return;
                }

                await QueryAsync(work[index], cancellationToken).ConfigureAwait(false);
            }
        }

        var workers = new Task[Math.Min(maxConcurrency, work.Length)];
        for (var index = 0; index < workers.Length; index++)
        {
            workers[index] = RunWorkerAsync();
        }

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            foreach (var item in work)
            {
                RollBackIfCurrent(item);
            }

            throw;
        }
    }

    public void Prune(IReadOnlySet<string> validDeviceIds)
    {
        ArgumentNullException.ThrowIfNull(validDeviceIds);
        var validIds = new HashSet<string>(validDeviceIds, StringComparer.OrdinalIgnoreCase);

        lock (gate)
        {
            foreach (var deviceId in entries.Keys.Where(id => !validIds.Contains(id)).ToArray())
            {
                entries.Remove(deviceId);
                // Keep the generation tombstone so a re-added ID cannot accept pre-prune work.
            }
        }
    }

    private RefreshWork[] Prepare(IReadOnlyList<BluetoothDeviceModel> devices)
    {
        var attemptedAt = DateTimeOffset.UtcNow;
        var work = new RefreshWork[devices.Count];

        lock (gate)
        {
            for (var index = 0; index < devices.Count; index++)
            {
                var device = devices[index];
                entries.TryGetValue(device.Id, out var previous);
                var lastAvailable = previous?.LastAvailable
                    ?? (previous?.Status == BatteryTelemetryStatus.Available ? previous.Current : null);
                var generation = generations.GetValueOrDefault(device.Id) + 1;
                generations[device.Id] = generation;

                var loading = new BatteryTelemetryEntry
                {
                    DeviceId = device.Id,
                    Status = BatteryTelemetryStatus.Loading,
                    Current = null,
                    LastAvailable = lastAvailable,
                    AttemptedAt = attemptedAt,
                };
                var rollback = previous is null or { Status: BatteryTelemetryStatus.Loading }
                    ? new BatteryTelemetryEntry
                    {
                        DeviceId = device.Id,
                        Status = BatteryTelemetryStatus.NotRequested,
                        Current = null,
                        LastAvailable = lastAvailable,
                    }
                    : previous;

                entries[device.Id] = loading;
                work[index] = new RefreshWork(device, generation, loading, rollback);
            }
        }

        return work;
    }

    private async Task QueryAsync(RefreshWork work, CancellationToken cancellationToken)
    {
        var acquired = false;
        try
        {
            await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;

            BatteryState state;
            try
            {
                state = await service.GetBatteryAsync(work.Device, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                Publish(work, BatteryTelemetryStatus.Failed, current: null);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (state.Percentage is not null || state.IsCharging is not null)
            {
                Publish(work, BatteryTelemetryStatus.Available, state);
            }
            else
            {
                Publish(work, BatteryTelemetryStatus.Unavailable, current: null);
            }
        }
        catch (OperationCanceledException)
        {
            RollBackIfCurrent(work);
            throw;
        }
        finally
        {
            if (acquired)
            {
                concurrency.Release();
            }
        }
    }

    private void Publish(
        RefreshWork work,
        BatteryTelemetryStatus status,
        BatteryState? current)
    {
        BatteryTelemetryEntry? entry = null;
        lock (gate)
        {
            if (!IsCurrentGeneration(work))
            {
                return;
            }

            var lastAvailable = current
                ?? entries[work.Device.Id].LastAvailable;
            entry = new BatteryTelemetryEntry
            {
                DeviceId = work.Device.Id,
                Status = status,
                Current = current,
                LastAvailable = lastAvailable,
                AttemptedAt = work.Loading.AttemptedAt,
            };
            entries[work.Device.Id] = entry;
        }

        OnChanged(entry);
    }

    private void RaiseLoadingIfCurrent(RefreshWork work)
    {
        lock (gate)
        {
            if (!IsCurrentGeneration(work)
                || !ReferenceEquals(entries[work.Device.Id], work.Loading))
            {
                return;
            }
        }

        OnChanged(work.Loading);
    }

    private void RollBackIfCurrent(RefreshWork work)
    {
        BatteryTelemetryEntry? entry = null;
        lock (gate)
        {
            if (!IsCurrentGeneration(work)
                || entries[work.Device.Id].Status != BatteryTelemetryStatus.Loading)
            {
                return;
            }

            entries[work.Device.Id] = work.Rollback;
            entry = work.Rollback;
        }

        OnChanged(entry);
    }

    private bool IsCurrentGeneration(RefreshWork work) =>
        generations.TryGetValue(work.Device.Id, out var generation)
        && generation == work.Generation
        && entries.ContainsKey(work.Device.Id);

    private void OnChanged(BatteryTelemetryEntry entry) =>
        Changed?.Invoke(this, new BatteryTelemetryChangedEventArgs { Entry = entry });

    private static bool SameId(string left, string? right) =>
        right is not null && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private sealed record RefreshWork(
        BluetoothDeviceModel Device,
        long Generation,
        BatteryTelemetryEntry Loading,
        BatteryTelemetryEntry Rollback);
}
