using System.Collections.Concurrent;
using BTHaven.Core.Audio;
using BTHaven.Windows.Audio;
using BTHaven.Windows.Diagnostics;

namespace BTHaven.IntegrationTests;

public sealed class A2dpAutoReconnectServiceTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Target_unavailable_retries_without_connecting()
    {
        var sink = new FakeReconnectSink();
        var delay = new ControlledDelay(completedCalls: 0);
        await using var service = CreateService(sink, delay);

        await service.EnableAsync("target");
        await delay.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(0, sink.ConnectCalls);
        Assert.Equal([TimeSpan.FromSeconds(1)], delay.Durations.ToArray());
        await service.DisableAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Connect_false_schedules_the_next_retry()
    {
        var sink = new FakeReconnectSink
        {
            AvailableDevices = [CreateTarget("target")],
        };
        sink.ConnectionResults.Enqueue(false);
        var delay = new ControlledDelay(completedCalls: 0);
        await using var service = CreateService(sink, delay);

        await service.EnableAsync("target");
        await delay.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, sink.ConnectCalls);
        Assert.Equal([TimeSpan.FromSeconds(1)], delay.Durations.ToArray());
        await service.DisableAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Retry_schedule_is_one_two_five_ten_thirty_sixty_seconds()
    {
        var sink = new FakeReconnectSink
        {
            AvailableDevices = [CreateTarget("target")],
        };
        for (var index = 0; index < 7; index++)
        {
            sink.ConnectionResults.Enqueue(false);
        }

        var delay = new ControlledDelay(completedCalls: 6);
        await using var service = CreateService(sink, delay);

        await service.EnableAsync("target");
        await delay.CallCountReached.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(
            [1d, 2d, 5d, 10d, 30d, 60d],
            delay.Durations.Select(duration => duration.TotalSeconds).Take(6).ToArray());
        await service.DisableAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Successful_connection_resets_backoff_after_closed_event()
    {
        var sink = new FakeReconnectSink
        {
            AvailableDevices = [CreateTarget("target")],
        };
        sink.ConnectionResults.Enqueue(false);
        sink.ConnectionResults.Enqueue(true);
        sink.ConnectionResults.Enqueue(false);
        var delay = new ControlledDelay(completedCalls: 1, stateWaitTimeout: TimeSpan.FromSeconds(30));
        await using var service = CreateService(sink, delay, TimeSpan.FromSeconds(30));

        await service.EnableAsync("target");
        await delay.StateWaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await sink.StateChangedSubscriberAdded.Task.WaitAsync(TimeSpan.FromSeconds(1));
        sink.Close();
        await delay.SecondRetryStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal([1d, 1d], delay.Durations.Where(duration => duration == TimeSpan.FromSeconds(1)).Select(duration => duration.TotalSeconds).ToArray());
        Assert.True(delay.CanceledStateWaits > 0);
        await service.DisableAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Already_open_sink_waits_without_discovery_or_connect()
    {
        var sink = new FakeReconnectSink();
        sink.Open("target");
        var delay = new ControlledDelay(completedCalls: 0, stateWaitTimeout: TimeSpan.FromSeconds(30));
        await using var service = CreateService(sink, delay, TimeSpan.FromSeconds(30));

        await service.EnableAsync("target");
        await delay.StateWaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(0, sink.DiscoveryCalls);
        Assert.Equal(0, sink.ConnectCalls);
        await service.DisableAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Closed_event_causes_a_new_connection_attempt()
    {
        var sink = new FakeReconnectSink
        {
            AvailableDevices = [CreateTarget("target")],
        };
        sink.ConnectionResults.Enqueue(true);
        sink.ConnectionResults.Enqueue(true);
        var delay = new ControlledDelay(completedCalls: 0, stateWaitTimeout: TimeSpan.FromSeconds(30));
        await using var service = CreateService(sink, delay, TimeSpan.FromSeconds(30));

        await service.EnableAsync("target");
        await delay.StateWaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await sink.StateChangedSubscriberAdded.Task.WaitAsync(TimeSpan.FromSeconds(1));
        sink.Close();
        await sink.SecondConnectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(2, sink.ConnectCalls);
        await service.DisableAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Integration")]
    public async Task Disable_or_dispose_cancels_a_pending_retry_delay(bool dispose)
    {
        var sink = new FakeReconnectSink();
        var delay = new ControlledDelay(completedCalls: 0);
        await using var service = CreateService(sink, delay);

        await service.EnableAsync("target");
        await delay.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        if (dispose)
        {
            await service.DisposeAsync();
        }
        else
        {
            await service.DisableAsync();
        }

        Assert.True(delay.CanceledDelays > 0);
        Assert.False(service.IsEnabled);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Canceled_enable_does_not_start_the_reconnect_loop()
    {
        var sink = new FakeReconnectSink();
        var delay = new ControlledDelay(completedCalls: 0);
        await using var service = CreateService(sink, delay);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.EnableAsync("target", cancellation.Token));

        Assert.False(service.IsEnabled);
        Assert.Equal(0, sink.DiscoveryCalls);
    }

    private static A2dpAutoReconnectService CreateService(
        FakeReconnectSink sink,
        ControlledDelay delay,
        TimeSpan? stateWaitTimeout = null)
        => new(
            sink,
            NullDiagnosticLogger.Instance,
            delay.DelayAsync,
            stateWaitTimeout ?? TimeSpan.FromMilliseconds(30));

    private static RemoteAudioDeviceInfo CreateTarget(string id)
        => new(id, "Target", null, null);

    private sealed class ControlledDelay
    {

        private readonly int completedCalls;
        private readonly TimeSpan stateWaitTimeout;
        private readonly ConcurrentQueue<TimeSpan> durations = new();
        private int callCount;
        private int oneSecondCount;

        public ControlledDelay(int completedCalls, TimeSpan? stateWaitTimeout = null)
        {
            this.completedCalls = completedCalls;
            this.stateWaitTimeout = stateWaitTimeout ?? TimeSpan.FromMilliseconds(30);
        }

        public TaskCompletionSource<bool> FirstCallStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> CallCountReached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> StateWaitStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> SecondRetryStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IEnumerable<TimeSpan> Durations => durations;

        public int CanceledDelays { get; private set; }

        public int CanceledStateWaits { get; private set; }

        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            durations.Enqueue(duration);
            var call = Interlocked.Increment(ref callCount);
            FirstCallStarted.TrySetResult(true);
            if (duration == stateWaitTimeout)
            {
                StateWaitStarted.TrySetResult(true);
                return WaitForCancellationAsync(cancellationToken, isStateWait: true);
            }

            if (duration == TimeSpan.FromSeconds(1)
                && Interlocked.Increment(ref oneSecondCount) == 2)
            {
                SecondRetryStarted.TrySetResult(true);
            }

            if (call == 6)
            {
                CallCountReached.TrySetResult(true);
            }

            if (call <= completedCalls)
            {
                return Task.CompletedTask;
            }

            return WaitForCancellationAsync(cancellationToken, isStateWait: false);
        }

        private async Task WaitForCancellationAsync(CancellationToken cancellationToken, bool isStateWait)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (isStateWait)
                {
                    CanceledStateWaits++;
                }
                else
                {
                    CanceledDelays++;
                }
            }
        }
    }

    private sealed class FakeReconnectSink : IA2dpReconnectSink
    {
        private Action<MediaAudioSinkState>? stateChanged;

        public IReadOnlyList<RemoteAudioDeviceInfo> AvailableDevices { get; init; } = [];

        public Queue<bool> ConnectionResults { get; } = new();

        public TaskCompletionSource<bool> StateChangedSubscriberAdded { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> SecondConnectionStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsEnabled { get; private set; }

        public string? DeviceId { get; private set; }

        public MediaAudioSinkState State { get; private set; } = MediaAudioSinkState.Disabled;

        public int DiscoveryCalls { get; private set; }

        public int ConnectCalls { get; private set; }

        public event Action<MediaAudioSinkState>? StateChanged
        {
            add
            {
                stateChanged += value;
                StateChangedSubscriberAdded.TrySetResult(true);
            }
            remove => stateChanged -= value;
        }

        public Task<IReadOnlyList<RemoteAudioDeviceInfo>> GetAvailableDevicesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DiscoveryCalls++;
            return Task.FromResult(AvailableDevices);
        }

        public Task<bool> ConnectAsync(string requestedDeviceId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectCalls++;
            if (ConnectCalls == 2)
            {
                SecondConnectionStarted.TrySetResult(true);
            }

            var connected = ConnectionResults.Count == 0 || ConnectionResults.Dequeue();
            if (connected)
            {
                Open(requestedDeviceId);
            }
            else
            {
                State = MediaAudioSinkState.Failed;
                IsEnabled = false;
                DeviceId = null;
            }

            return Task.FromResult(connected);
        }

        public void Open(string requestedDeviceId)
        {
            IsEnabled = true;
            DeviceId = requestedDeviceId;
            State = MediaAudioSinkState.Opened;
        }

        public void Close()
        {
            IsEnabled = false;
            DeviceId = null;
            State = MediaAudioSinkState.Disabled;
            stateChanged?.Invoke(MediaAudioSinkState.Disabled);
        }
    }
}
