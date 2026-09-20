using BTHaven.Core.Audio;
using BTHaven.Core.Devices;
using BTHaven.Windows.Audio;
using BTHaven_App;
using Windows.Media.Audio;

namespace BTHaven.IntegrationTests;

public sealed class MediaConnectionCoordinatorTests
{
    [Fact]
    public async Task Queued_connection_rechecks_selection_before_replacing_active_media()
    {
        var connection = new ControlledConnection();
        await using var sink = new A2dpSinkService(id => id == "target" ? connection : throw new InvalidOperationException("Stale connection"));
        await using var reconnect = new A2dpAutoReconnectService(sink);
        using var media = new MediaConnectionCoordinator(sink, reconnect);
        var first = media.ConnectAsync("phone", "target", false, CancellationToken.None);
        await connection.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var current = true;
        var stale = media.ConnectAsync("other", "other-target", false, CancellationToken.None, () => current);
        Assert.False(stale.IsCompleted);
        current = false;
        connection.Release.SetResult();

        Assert.True(await first);
        Assert.False(await stale);
        Assert.Equal("phone", media.ActiveDeviceId);
        Assert.Equal("target", sink.DeviceId);
        await media.DisconnectAsync("other", CancellationToken.None);
        Assert.Equal("target", sink.DeviceId);
        await media.DisconnectAsync("phone", CancellationToken.None);
        Assert.Null(media.ActiveDeviceId);
        Assert.False(sink.IsEnabled);
    }

    [Fact]
    public async Task Removed_device_cannot_be_reactivated_by_a_delayed_open_notification()
    {
        var connection = new ControlledConnection();
        connection.Release.SetResult();
        await using var sink = new A2dpSinkService(_ => connection);
        await using var reconnect = new A2dpAutoReconnectService(sink);
        using var media = new MediaConnectionCoordinator(sink, reconnect);
        Assert.True(await media.ConnectAsync("phone", "target", false, CancellationToken.None));
        media.RemoveDevice("phone");
        var devices = new Dictionary<string, BluetoothDeviceModel>
        {
            ["phone"] = new()
            {
                Id = "phone",
                Name = "Phone",
                Endpoints = [new() { Id = "target", Transport = BluetoothTransport.Classic }],
            },
        };

        Assert.False(media.ApplyState(MediaAudioSinkState.Opened, "target", devices));
        Assert.Null(media.ActiveDeviceId);
        Assert.True(await media.ConnectAsync("phone", "target", false, CancellationToken.None));
        Assert.True(media.ApplyState(MediaAudioSinkState.Opened, "target", devices));
        Assert.Equal("phone", media.ActiveDeviceId);
    }

    private sealed class ControlledConnection : IA2dpConnection
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string DeviceId => "target";
        public AudioPlaybackConnectionState State { get; private set; } = AudioPlaybackConnectionState.Closed;
        public event Action<IA2dpConnection>? StateChanged;
        public Task StartAsync() => Task.CompletedTask;
        public async Task<AudioPlaybackConnectionOpenResultStatus> OpenAsync()
        {
            Started.TrySetResult();
            await Release.Task;
            State = AudioPlaybackConnectionState.Opened;
            StateChanged?.Invoke(this);
            return AudioPlaybackConnectionOpenResultStatus.Success;
        }
        public void Dispose() { }
    }
}
