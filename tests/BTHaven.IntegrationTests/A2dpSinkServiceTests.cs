using BTHaven.Core.Audio;
using BTHaven.Windows.Audio;
using Windows.Media.Audio;

namespace BTHaven.IntegrationTests;

public sealed class A2dpSinkServiceTests
{
    [Fact]
    public async Task Concurrent_connects_dispose_the_previous_candidate_and_leave_only_the_winner_active()
    {
        var first = new FakeConnection("selector-a", waitForOpen: true);
        var second = new FakeConnection("selector-b");
        await using var service = CreateService(first, second);

        var firstTask = service.ConnectAsync(first.DeviceId);
        await first.OpenStarted.Task;
        var secondTask = service.ConnectAsync(second.DeviceId);
        first.AllowOpen();

        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.All(results, Assert.True);
        Assert.Equal(second.DeviceId, service.DeviceId);
        Assert.Equal(MediaAudioSinkState.Opened, service.State);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(0, second.DisposeCount);
    }

    [Fact]
    public async Task Closed_event_from_previous_sender_does_not_change_the_current_connection()
    {
        var first = new FakeConnection("selector-a");
        var second = new FakeConnection("selector-b");
        await using var service = CreateService(first, second);

        Assert.True(await service.ConnectAsync(first.DeviceId));
        Assert.True(await service.ConnectAsync(second.DeviceId));

        first.RaiseClosed();

        Assert.Equal(second.DeviceId, service.DeviceId);
        Assert.Equal(MediaAudioSinkState.Opened, service.State);
        Assert.True(service.IsEnabled);
    }

    [Fact]
    public async Task Open_failure_disposes_candidate_and_finishes_failed_without_active_device()
    {
        var failed = new FakeConnection(
            "selector-fail",
            openStatus: (AudioPlaybackConnectionOpenResultStatus)int.MaxValue);
        await using var service = CreateService(failed);

        var opened = await service.ConnectAsync(failed.DeviceId);

        Assert.False(opened);
        Assert.Equal(MediaAudioSinkState.Failed, service.State);
        Assert.Null(service.DeviceId);
        Assert.False(service.IsEnabled);
        Assert.Equal(1, failed.DisposeCount);
    }

    [Fact]
    public async Task Cancellation_during_open_disposes_candidate_and_finishes_failed()
    {
        var connection = new FakeConnection("selector-cancel", waitForOpen: true);
        await using var service = CreateService(connection);
        using var cancellation = new CancellationTokenSource();

        var connectTask = service.ConnectAsync(connection.DeviceId, cancellation.Token);
        await connection.OpenStarted.Task;
        cancellation.Cancel();
        connection.AllowOpen();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await connectTask);

        Assert.Equal(MediaAudioSinkState.Failed, service.State);
        Assert.Null(service.DeviceId);
        Assert.False(service.IsEnabled);
        Assert.Equal(1, connection.DisposeCount);
    }

    [Fact]
    public async Task Disconnect_finishes_disabled_and_disposes_the_current_connection()
    {
        var connection = new FakeConnection("selector-disconnect");
        await using var service = CreateService(connection);

        Assert.True(await service.ConnectAsync(connection.DeviceId));
        await service.DisconnectAsync();

        Assert.Equal(MediaAudioSinkState.Disabled, service.State);
        Assert.Null(service.DeviceId);
        Assert.False(service.IsEnabled);
        Assert.Equal(1, connection.DisposeCount);
    }

    private static A2dpSinkService CreateService(params FakeConnection[] connections)
    {
        var byId = connections.ToDictionary(connection => connection.DeviceId, StringComparer.Ordinal);
        return new A2dpSinkService(deviceId => byId[deviceId]);
    }

    private sealed class FakeConnection : IA2dpConnection
    {
        private readonly TaskCompletionSource openStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource allowOpen =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly bool waitForOpen;
        private readonly AudioPlaybackConnectionOpenResultStatus openStatus;
        private Action<IA2dpConnection>? stateChanged;
        private Action<IA2dpConnection>? historicalStateChanged;

        public FakeConnection(
            string deviceId,
            bool waitForOpen = false,
            AudioPlaybackConnectionOpenResultStatus openStatus = AudioPlaybackConnectionOpenResultStatus.Success)
        {
            DeviceId = deviceId;
            this.waitForOpen = waitForOpen;
            this.openStatus = openStatus;
        }

        public string DeviceId { get; }
        public AudioPlaybackConnectionState State { get; private set; } = AudioPlaybackConnectionState.Closed;
        public TaskCompletionSource OpenStarted => openStarted;
        public int DisposeCount { get; private set; }
        public event Action<IA2dpConnection>? StateChanged
        {
            add
            {
                stateChanged += value;
                historicalStateChanged = value;
            }
            remove => stateChanged -= value;
        }

        public Task StartAsync() => Task.CompletedTask;

        public async Task<AudioPlaybackConnectionOpenResultStatus> OpenAsync()
        {
            openStarted.TrySetResult();
            if (waitForOpen)
            {
                await allowOpen.Task;
            }

            if (openStatus == AudioPlaybackConnectionOpenResultStatus.Success)
            {
                State = AudioPlaybackConnectionState.Opened;
                stateChanged?.Invoke(this);
            }

            return openStatus;
        }

        public void AllowOpen() => allowOpen.TrySetResult();

        public void RaiseClosed()
        {
            State = AudioPlaybackConnectionState.Closed;
            (stateChanged ?? historicalStateChanged)?.Invoke(this);
        }

        public void Dispose() => DisposeCount++;
    }
}
