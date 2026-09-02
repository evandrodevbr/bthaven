using BTHaven.Core.Battery;
using BTHaven.Core.Contracts;
using BTHaven.Core.Devices;

namespace BTHaven.Core.Tests;

public sealed class BatteryTelemetryCoordinatorTests
{
    [Fact]
    public async Task Refresh_starts_priority_device_first_and_limits_concurrency_to_two()
    {
        var service = new ControlledBatteryService();
        var coordinator = new BatteryTelemetryCoordinator(service, maxConcurrency: 2);

        var refresh = coordinator.RefreshAsync(
            [
                Device("omega"),
                Device("selected"),
                Device("beta", isConnected: true),
                Device("alpha", isConnected: true),
            ],
            priorityDeviceId: "selected");

        await service.WaitForStartsAsync(2);

        Assert.Equal(new[] { "selected", "alpha" }, service.Starts);
        Assert.Equal(2, service.PeakConcurrency);

        service.CompleteAll(Available(70));
        await refresh;

        Assert.Equal(new[] { "selected", "alpha", "beta", "omega" }, service.Starts);
        Assert.Equal(2, service.PeakConcurrency);
    }

    [Fact]
    public async Task Refresh_queries_only_present_devices()
    {
        var service = new ControlledBatteryService();
        var coordinator = new BatteryTelemetryCoordinator(service);

        var refresh = coordinator.RefreshAsync(
            [Device("present"), Device("absent", isPresent: false)],
            priorityDeviceId: "absent");

        await service.WaitForStartsAsync(1);
        service.CompleteAll(Available(55));
        await refresh;

        Assert.Equal(new[] { "present" }, service.Starts);
        Assert.True(coordinator.TryGet("present", out _));
        Assert.False(coordinator.TryGet("absent", out _));
    }

    [Fact]
    public async Task Older_generation_cannot_replace_newer_battery_result()
    {
        var service = new ControlledBatteryService();
        var coordinator = new BatteryTelemetryCoordinator(service);

        var olderRefresh = coordinator.RefreshAsync([Device("phone")], priorityDeviceId: "phone");
        await service.WaitForStartsAsync(1);

        var newerRefresh = coordinator.RefreshAsync([Device("phone")], priorityDeviceId: "phone");
        await service.WaitForStartsAsync(2);

        service.Complete("phone", occurrence: 1, Available(90));
        await newerRefresh;
        service.Complete("phone", occurrence: 0, Available(10));
        await olderRefresh;

        Assert.True(coordinator.TryGet("phone", out var entry));
        Assert.Equal(BatteryTelemetryStatus.Available, entry.Status);
        Assert.Equal(90, entry.Current?.Percentage);
        Assert.Equal(90, entry.LastAvailable?.Percentage);
    }

    [Fact]
    public async Task Unavailable_attempt_retains_last_available_reading()
    {
        var service = new ControlledBatteryService();
        var coordinator = new BatteryTelemetryCoordinator(service);

        var availableRefresh = coordinator.RefreshAsync([Device("phone")], priorityDeviceId: "phone");
        await service.WaitForStartsAsync(1);
        service.Complete("phone", occurrence: 0, Available(80));
        await availableRefresh;

        var unavailableRefresh = coordinator.RefreshAsync([Device("phone")], priorityDeviceId: "phone");
        await service.WaitForStartsAsync(2);

        Assert.True(coordinator.TryGet("phone", out var loading));
        Assert.Equal(BatteryTelemetryStatus.Loading, loading.Status);
        Assert.Null(loading.Current);
        Assert.Equal(80, loading.LastAvailable?.Percentage);

        service.Complete("phone", occurrence: 1, BatteryState.Unavailable("test"));
        await unavailableRefresh;

        Assert.True(coordinator.TryGet("phone", out var entry));
        Assert.Equal(BatteryTelemetryStatus.Unavailable, entry.Status);
        Assert.Null(entry.Current);
        Assert.Equal(80, entry.LastAvailable?.Percentage);
    }

    [Fact]
    public async Task Failed_attempt_retains_last_available_reading()
    {
        var service = new ControlledBatteryService();
        var coordinator = new BatteryTelemetryCoordinator(service);

        var availableRefresh = coordinator.RefreshAsync([Device("phone")], priorityDeviceId: "phone");
        await service.WaitForStartsAsync(1);
        service.Complete("phone", occurrence: 0, Available(80));
        await availableRefresh;

        var failedRefresh = coordinator.RefreshAsync([Device("phone")], priorityDeviceId: "phone");
        await service.WaitForStartsAsync(2);
        service.Fail("phone", occurrence: 1, new InvalidOperationException("probe failed"));
        await failedRefresh;

        Assert.True(coordinator.TryGet("phone", out var entry));
        Assert.Equal(BatteryTelemetryStatus.Failed, entry.Status);
        Assert.Null(entry.Current);
        Assert.Equal(80, entry.LastAvailable?.Percentage);
    }

    [Fact]
    public async Task Prune_keeps_filtered_but_known_ids_and_removes_unknown_ids()
    {
        var service = new ControlledBatteryService();
        var coordinator = new BatteryTelemetryCoordinator(service);
        var refresh = coordinator.RefreshAsync(
            [Device("visible"), Device("filtered"), Device("removed")],
            priorityDeviceId: "visible");
        await service.WaitForStartsAsync(2);
        service.CompleteAll(Available(65));
        await refresh;

        coordinator.Prune(new HashSet<string>(["VISIBLE", "filtered"], StringComparer.Ordinal));

        Assert.True(coordinator.TryGet("visible", out _));
        Assert.True(coordinator.TryGet("filtered", out _));
        Assert.False(coordinator.TryGet("removed", out _));
    }

    [Fact]
    public async Task Pruned_inflight_result_cannot_recreate_entry()
    {
        var service = new ControlledBatteryService();
        var coordinator = new BatteryTelemetryCoordinator(service);
        var refresh = coordinator.RefreshAsync([Device("removed")], priorityDeviceId: "removed");
        await service.WaitForStartsAsync(1);

        coordinator.Prune(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        service.Complete("removed", occurrence: 0, Available(40));
        await refresh;

        Assert.False(coordinator.TryGet("removed", out _));
    }

    [Fact]
    public async Task Pruned_id_readded_before_old_result_cannot_accept_old_generation()
    {
        var service = new ControlledBatteryService();
        var coordinator = new BatteryTelemetryCoordinator(service);
        var olderRefresh = coordinator.RefreshAsync([Device("phone")], priorityDeviceId: "phone");
        await service.WaitForStartsAsync(1);

        coordinator.Prune(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var newerRefresh = coordinator.RefreshAsync([Device("phone")], priorityDeviceId: "phone");
        await service.WaitForStartsAsync(2);

        service.Complete("phone", occurrence: 1, Available(90));
        await newerRefresh;
        service.Complete("phone", occurrence: 0, Available(10));
        await olderRefresh;

        Assert.True(coordinator.TryGet("phone", out var entry));
        Assert.Equal(90, entry.Current?.Percentage);
    }

    [Fact]
    public async Task Invalidate_retains_last_available_and_rejects_inflight_result()
    {
        var service = new ControlledBatteryService();
        var coordinator = new BatteryTelemetryCoordinator(service);
        var availableRefresh = coordinator.RefreshAsync([Device("phone")], priorityDeviceId: "phone");
        await service.WaitForStartsAsync(1);
        service.Complete("phone", occurrence: 0, Available(80));
        await availableRefresh;

        var staleRefresh = coordinator.RefreshAsync([Device("phone")], priorityDeviceId: "phone");
        await service.WaitForStartsAsync(2);
        coordinator.Invalidate("phone");
        service.Complete("phone", occurrence: 1, Available(10));
        await staleRefresh;

        Assert.True(coordinator.TryGet("phone", out var entry));
        Assert.Equal(BatteryTelemetryStatus.Unavailable, entry.Status);
        Assert.Null(entry.Current);
        Assert.Equal(80, entry.LastAvailable?.Percentage);
    }

    [Fact]
    public async Task Cancellation_restores_previous_available_entry()
    {
        var service = new ControlledBatteryService();
        var coordinator = new BatteryTelemetryCoordinator(service);

        var initialRefresh = coordinator.RefreshAsync([Device("phone")], priorityDeviceId: "phone");
        await service.WaitForStartsAsync(1);
        service.Complete("phone", occurrence: 0, Available(75));
        await initialRefresh;

        using var cancellation = new CancellationTokenSource();
        var cancelledRefresh = coordinator.RefreshAsync(
            [Device("phone")],
            priorityDeviceId: "phone",
            cancellation.Token);
        await service.WaitForStartsAsync(2);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledRefresh);

        Assert.True(coordinator.TryGet("phone", out var entry));
        Assert.Equal(BatteryTelemetryStatus.Available, entry.Status);
        Assert.Equal(75, entry.Current?.Percentage);
        Assert.Equal(75, entry.LastAvailable?.Percentage);
    }

    [Fact]
    public async Task Cancellation_does_not_start_queued_calls_or_leave_loading_entries()
    {
        var service = new ControlledBatteryService();
        var coordinator = new BatteryTelemetryCoordinator(service, maxConcurrency: 2);
        using var cancellation = new CancellationTokenSource();

        var refresh = coordinator.RefreshAsync(
            [Device("alpha"), Device("beta"), Device("gamma")],
            priorityDeviceId: "alpha",
            cancellation.Token);
        await service.WaitForStartsAsync(2);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);

        Assert.Equal(2, service.Starts.Count);
        foreach (var deviceId in new[] { "alpha", "beta", "gamma" })
        {
            Assert.True(coordinator.TryGet(deviceId, out var entry));
            Assert.Equal(BatteryTelemetryStatus.NotRequested, entry.Status);
            Assert.Null(entry.Current);
            Assert.Null(entry.LastAvailable);
        }
    }

    private static BluetoothDeviceModel Device(
        string id,
        bool isPresent = true,
        bool isConnected = false) => new()
    {
        Id = id,
        Name = id,
        IsPresent = isPresent,
        IsConnected = isConnected,
    };

    private static BatteryState Available(int percentage) => new()
    {
        Percentage = percentage,
        IsCharging = false,
        Source = "test",
        LastUpdated = DateTimeOffset.UtcNow,
        Confidence = BatteryConfidence.High,
    };

    private sealed class ControlledBatteryService : IBatteryService
    {
        private readonly object gate = new();
        private readonly List<string> starts = [];
        private readonly List<PendingCall> calls = [];
        private TaskCompletionSource startsChanged = NewSignal();
        private BatteryState? completionForFutureCalls;
        private int activeCalls;
        private int peakConcurrency;

        public IReadOnlyList<string> Starts
        {
            get
            {
                lock (gate)
                {
                    return starts.ToArray();
                }
            }
        }

        public int PeakConcurrency
        {
            get
            {
                lock (gate)
                {
                    return peakConcurrency;
                }
            }
        }

        public async Task<BatteryState> GetBatteryAsync(
            BluetoothDeviceModel device,
            CancellationToken cancellationToken = default)
        {
            PendingCall call;
            TaskCompletionSource signal;
            BatteryState? immediateResult;
            lock (gate)
            {
                call = new PendingCall(device.Id);
                calls.Add(call);
                starts.Add(device.Id);
                activeCalls++;
                peakConcurrency = Math.Max(peakConcurrency, activeCalls);
                immediateResult = completionForFutureCalls;
                signal = startsChanged;
                startsChanged = NewSignal();
            }

            signal.TrySetResult();
            if (immediateResult is not null)
            {
                call.Completion.TrySetResult(immediateResult);
            }

            try
            {
                return await call.Completion.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                lock (gate)
                {
                    activeCalls--;
                }
            }
        }

        public async Task WaitForStartsAsync(int count)
        {
            while (true)
            {
                Task signal;
                lock (gate)
                {
                    if (starts.Count >= count)
                    {
                        return;
                    }

                    signal = startsChanged.Task;
                }

                await signal.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        public void Complete(string deviceId, int occurrence, BatteryState result) =>
            GetCall(deviceId, occurrence).Completion.TrySetResult(result);

        public void Fail(string deviceId, int occurrence, Exception exception) =>
            GetCall(deviceId, occurrence).Completion.TrySetException(exception);

        public void CompleteAll(BatteryState result)
        {
            PendingCall[] pending;
            lock (gate)
            {
                completionForFutureCalls = result;
                pending = calls.Where(call => !call.Completion.Task.IsCompleted).ToArray();
            }

            foreach (var call in pending)
            {
                call.Completion.TrySetResult(result);
            }
        }

        private PendingCall GetCall(string deviceId, int occurrence)
        {
            lock (gate)
            {
                return calls
                    .Where(call => string.Equals(call.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                    .ElementAt(occurrence);
            }
        }

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed record PendingCall(string DeviceId)
        {
            public TaskCompletionSource<BatteryState> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
