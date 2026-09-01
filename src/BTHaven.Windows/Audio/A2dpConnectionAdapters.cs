using Windows.Media.Audio;

namespace BTHaven.Windows.Audio;

internal interface IA2dpConnection : IDisposable
{
    string DeviceId { get; }
    AudioPlaybackConnectionState State { get; }
    event Action<IA2dpConnection>? StateChanged;
    Task StartAsync();
    Task<AudioPlaybackConnectionOpenResultStatus> OpenAsync();
}

internal static class A2dpConnectionAdapters
{
    public static IA2dpConnection? TryCreate(string deviceId)
    {
        var connection = AudioPlaybackConnection.TryCreateFromId(deviceId);
        return connection is null ? null : new WinRtA2dpConnection(connection);
    }

    private sealed class WinRtA2dpConnection : IA2dpConnection
    {
        private readonly AudioPlaybackConnection connection;

        public WinRtA2dpConnection(AudioPlaybackConnection connection)
        {
            this.connection = connection;
            this.connection.StateChanged += OnStateChanged;
        }

        public string DeviceId => connection.DeviceId;
        public AudioPlaybackConnectionState State => connection.State;
        public event Action<IA2dpConnection>? StateChanged;

        public Task StartAsync() => connection.StartAsync().AsTask();

        public async Task<AudioPlaybackConnectionOpenResultStatus> OpenAsync()
        {
            var result = await connection.OpenAsync();
            return result.Status;
        }

        public void Dispose()
        {
            connection.StateChanged -= OnStateChanged;
            connection.Dispose();
        }

        private void OnStateChanged(AudioPlaybackConnection sender, object args) => StateChanged?.Invoke(this);
    }
}
