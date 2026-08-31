using BTHaven.Core.Audio;
using BTHaven.Core.Contracts;
using BTHaven.Windows.Diagnostics;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace BTHaven.Windows.Audio;

public sealed record RemoteAudioDeviceInfo(
    string Id,
    string Name,
    string? ContainerId,
    string? Address);

public sealed class A2dpSinkService : IMediaAudioSink, IAsyncDisposable
{
    private static readonly IReadOnlyList<string> RequestedProperties =
    [
        "System.Devices.Aep.ContainerId",
        "System.Devices.Aep.DeviceAddress",
    ];

    private readonly object sync = new();
    private readonly IWindowsDiagnosticLogger logger;
    private AudioPlaybackConnection? connection;
    private string? deviceId;
    private MediaAudioSinkState state = MediaAudioSinkState.Disabled;

    public event Action<MediaAudioSinkState>? StateChanged;

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
        logger.Info("A2DP.Discovery.Started", new Dictionary<string, object?>
        {
            ["selector"] = selector,
            ["properties"] = RequestedProperties,
        });

        try
        {
            var devices = await DeviceInformation.FindAllAsync(selector, RequestedProperties);
            cancellationToken.ThrowIfCancellationRequested();
            var result = devices.Select(device => new RemoteAudioDeviceInfo(
                device.Id,
                device.Name,
                GetProperty(device, "System.Devices.Aep.ContainerId"),
                GetProperty(device, "System.Devices.Aep.DeviceAddress"))).ToArray();

            logger.Info("A2DP.Discovery.Completed", new Dictionary<string, object?>
            {
                ["count"] = result.Length,
                ["selector"] = selector,
            });
            foreach (var target in result)
            {
                logger.Debug("A2DP.Target.Observed", new Dictionary<string, object?>
                {
                    ["deviceId"] = target.Id,
                    ["name"] = target.Name,
                    ["containerId"] = target.ContainerId,
                    ["address"] = target.Address,
                });
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.Info("A2DP.Discovery.Cancelled");
            throw;
        }
        catch (Exception exception)
        {
            logger.Error("A2DP.Discovery.Failed", exception, new Dictionary<string, object?>
            {
                ["selector"] = selector,
            });
            throw;
        }
    }

    public async Task<bool> ConnectAsync(
        string requestedDeviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedDeviceId);
        cancellationToken.ThrowIfCancellationRequested();
        logger.Info("A2DP.Connection.Requested", new Dictionary<string, object?>
        {
            ["deviceId"] = requestedDeviceId,
        });
        await DisconnectAsync(cancellationToken).ConfigureAwait(false);
        SetState(MediaAudioSinkState.Starting, requestedDeviceId);

        AudioPlaybackConnection? newConnection = null;
        try
        {
            newConnection = AudioPlaybackConnection.TryCreateFromId(requestedDeviceId);
            if (newConnection is null)
            {
                SetState(MediaAudioSinkState.Failed, requestedDeviceId);
                logger.Warning("A2DP.Connection.Unavailable", new Dictionary<string, object?>
                {
                    ["deviceId"] = requestedDeviceId,
                    ["reason"] = "TryCreateFromId returned null",
                });
                return false;
            }

            newConnection.StateChanged += OnStateChanged;
            logger.Debug("A2DP.Connection.Starting", new Dictionary<string, object?>
            {
                ["deviceId"] = requestedDeviceId,
                ["state"] = newConnection.State.ToString(),
            });
            await newConnection.StartAsync();
            SetState(MediaAudioSinkState.Started, requestedDeviceId);
            SetState(MediaAudioSinkState.Opening, requestedDeviceId);
            var openResult = await newConnection.OpenAsync();
            logger.Info("A2DP.Connection.OpenResult", new Dictionary<string, object?>
            {
                ["deviceId"] = requestedDeviceId,
                ["status"] = openResult.Status.ToString(),
                ["state"] = newConnection.State.ToString(),
            });
            cancellationToken.ThrowIfCancellationRequested();
            if (openResult.Status != AudioPlaybackConnectionOpenResultStatus.Success)
            {
                SetState(MediaAudioSinkState.Failed, requestedDeviceId);
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
                ["audioPath"] = "Windows system playback endpoint",
            });
            newConnection = null;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetState(MediaAudioSinkState.Failed, requestedDeviceId);
            logger.Info("A2DP.Connection.Cancelled", new Dictionary<string, object?>
            {
                ["deviceId"] = requestedDeviceId,
            });
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
        string? oldDeviceId;
        AudioPlaybackConnection? oldConnection;
        lock (sync)
        {
            oldConnection = connection;
            oldDeviceId = deviceId;
            connection = null;
            deviceId = null;
            state = MediaAudioSinkState.Disabled;
        }

        if (oldConnection is null)
        {
            logger.Debug("A2DP.Connection.NoActiveConnection");
            return;
        }

        oldConnection.Dispose();
        logger.Info("A2DP.Connection.Closed", new Dictionary<string, object?>
        {
            ["deviceId"] = oldDeviceId,
        });
        await Task.CompletedTask;
    }

    public async Task EnableAsync(string requestedDeviceId, CancellationToken cancellationToken = default)
    {
        logger.Info("A2DP.Enable.Requested", new Dictionary<string, object?>
        {
            ["deviceId"] = requestedDeviceId,
        });
        if (!await ConnectAsync(requestedDeviceId, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Unable to open the A2DP connection for '{requestedDeviceId}'.");
        }
    }

    public Task DisableAsync(CancellationToken cancellationToken = default)
    {
        logger.Info("A2DP.Disable.Requested");
        return DisconnectAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        logger.Info("A2DP.Service.Disposed");
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
        StateChanged?.Invoke(nextState);
    }

    private void OnStateChanged(AudioPlaybackConnection sender, object args)
    {
        var nextState = sender.State switch
        {
            AudioPlaybackConnectionState.Opened => MediaAudioSinkState.Opened,
            AudioPlaybackConnectionState.Closed => MediaAudioSinkState.Disabled,
            _ => State,
        };
        lock (sync)
        {
            state = nextState;
        }
        logger.Info("A2DP.Connection.StateChanged", new Dictionary<string, object?>
        {
            ["deviceId"] = sender.DeviceId,
            ["state"] = sender.State.ToString(),
            ["mappedState"] = nextState.ToString(),
        });
        StateChanged?.Invoke(nextState);
    }

    private static string? GetProperty(DeviceInformation device, string key)
    {
        return device.Properties.TryGetValue(key, out var value) ? value?.ToString() : null;
    }
}
