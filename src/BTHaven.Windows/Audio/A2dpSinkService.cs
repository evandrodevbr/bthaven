using BTHaven.Core.Audio;
using BTHaven.Core.Contracts;
using BTHaven.Windows.Diagnostics;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace BTHaven.Windows.Audio;

public sealed record RemoteAudioDeviceInfo(string Id, string Name);

public sealed class A2dpSinkService : IMediaAudioSink, IAsyncDisposable
{
    private readonly object sync = new();
    private readonly IWindowsDiagnosticLogger logger;
    private AudioPlaybackConnection? connection;
    private string? deviceId;
    private MediaAudioSinkState state = MediaAudioSinkState.Disabled;

    public A2dpSinkService(IWindowsDiagnosticLogger? logger = null)
    {
        this.logger = logger ?? NullDiagnosticLogger.Instance;
    }

    public bool IsEnabled
    {
        get
        {
            lock (sync)
            {
                return state == MediaAudioSinkState.Opened;
            }
        }
    }

    public string? DeviceId
    {
        get
        {
            lock (sync)
            {
                return deviceId;
            }
        }
    }

    public MediaAudioSinkState State
    {
        get
        {
            lock (sync)
            {
                return state;
            }
        }
    }

    public MediaAudioSinkState GetConnectionState() => State;

    public async Task<IReadOnlyList<RemoteAudioDeviceInfo>> GetAvailableDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selector = AudioPlaybackConnection.GetDeviceSelector();
        var devices = await DeviceInformation.FindAllAsync(selector);
        cancellationToken.ThrowIfCancellationRequested();
        return devices.Select(device => new RemoteAudioDeviceInfo(device.Id, device.Name)).ToArray();
    }

    public async Task<bool> ConnectAsync(
        string requestedDeviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedDeviceId);
        cancellationToken.ThrowIfCancellationRequested();
        await DisconnectAsync(cancellationToken).ConfigureAwait(false);
        SetState(MediaAudioSinkState.Starting, requestedDeviceId);

        AudioPlaybackConnection? newConnection = null;
        try
        {
            newConnection = AudioPlaybackConnection.TryCreateFromId(requestedDeviceId);
            if (newConnection is null)
            {
                SetState(MediaAudioSinkState.Failed, requestedDeviceId);
                logger.Info("A2DP.Connection.Unavailable", new Dictionary<string, object?>
                {
                    ["deviceId"] = requestedDeviceId,
                    ["reason"] = "TryCreateFromId returned null",
                });
                return false;
            }

            newConnection.StateChanged += OnStateChanged;
            await newConnection.StartAsync();
            SetState(MediaAudioSinkState.Started, requestedDeviceId);
            cancellationToken.ThrowIfCancellationRequested();
            SetState(MediaAudioSinkState.Opening, requestedDeviceId);
            var openResult = await newConnection.OpenAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (openResult.Status != AudioPlaybackConnectionOpenResultStatus.Success)
            {
                SetState(MediaAudioSinkState.Failed, requestedDeviceId);
                logger.Info("A2DP.Connection.OpenFailed", new Dictionary<string, object?>
                {
                    ["deviceId"] = requestedDeviceId,
                    ["status"] = openResult.Status.ToString(),
                });
                newConnection.Dispose();
                return false;
            }

            lock (sync)
            {
                connection = newConnection;
                deviceId = requestedDeviceId;
                state = MediaAudioSinkState.Opened;
            }
            logger.Info("A2DP.Connection.Opened", new Dictionary<string, object?>
            {
                ["deviceId"] = requestedDeviceId,
                ["state"] = newConnection.State.ToString(),
            });
            newConnection = null;
            return true;
        }
        catch (OperationCanceledException)
        {
            SetState(MediaAudioSinkState.Failed, requestedDeviceId);
            throw;
        }
        catch (Exception exception)
        {
            SetState(MediaAudioSinkState.Failed, requestedDeviceId);
            logger.Error("A2DP.Connection.Failed", exception, new Dictionary<string, object?>
            {
                ["deviceId"] = requestedDeviceId,
            });
            return false;
        }
        finally
        {
            newConnection?.Dispose();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AudioPlaybackConnection? oldConnection;
        string? oldDeviceId;
        lock (sync)
        {
            oldConnection = connection;
            oldDeviceId = deviceId;
            connection = null;
            deviceId = null;
            state = MediaAudioSinkState.Disabled;
        }

        if (oldConnection is not null)
        {
            oldConnection.Dispose();
            logger.Info("A2DP.Connection.Closed", new Dictionary<string, object?>
            {
                ["deviceId"] = oldDeviceId,
            });
        }
        await Task.CompletedTask;
    }

    public async Task EnableAsync(string requestedDeviceId, CancellationToken cancellationToken = default)
    {
        if (!await ConnectAsync(requestedDeviceId, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Unable to open the A2DP connection for '{requestedDeviceId}'.");
        }
    }

    public Task DisableAsync(CancellationToken cancellationToken = default)
    {
        return DisconnectAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return new ValueTask(DisconnectAsync());
    }

    private void SetState(MediaAudioSinkState nextState, string requestedDeviceId)
    {
        lock (sync)
        {
            state = nextState;
            deviceId = requestedDeviceId;
        }
        logger.Info("A2DP.Connection.State", new Dictionary<string, object?>
        {
            ["deviceId"] = requestedDeviceId,
            ["state"] = nextState.ToString(),
        });
    }

    private void OnStateChanged(AudioPlaybackConnection sender, object args)
    {
        logger.Info("A2DP.Connection.StateChanged", new Dictionary<string, object?>
        {
            ["deviceId"] = sender.DeviceId,
            ["state"] = sender.State.ToString(),
        });
    }
}
