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

public sealed class A2dpSinkService : IMediaAudioSink, IA2dpReconnectSink, IAsyncDisposable
{
    private static readonly IReadOnlyList<string> RequestedProperties =
    [
        "System.Devices.Aep.ContainerId",
        "System.Devices.Aep.DeviceAddress",
    ];

    private readonly object sync = new();
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly IWindowsDiagnosticLogger logger;
    private readonly Func<string, IA2dpConnection?> connectionFactory;
    private IA2dpConnection? connection;
    private Action<IA2dpConnection>? connectionStateChanged;
    private string? deviceId;
    private long generation;
    private MediaAudioSinkState state = MediaAudioSinkState.Disabled;

    public event Action<MediaAudioSinkState>? StateChanged;

    public A2dpSinkService(IWindowsDiagnosticLogger? logger = null)
        : this(A2dpConnectionAdapters.TryCreate, logger)
    {
    }

    internal A2dpSinkService(
        Func<string, IA2dpConnection?> connectionFactory,
        IWindowsDiagnosticLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        this.connectionFactory = connectionFactory;
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

        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        IA2dpConnection? candidate = null;
        Action<IA2dpConnection>? candidateStateChanged = null;
        var candidateGeneration = 0L;
        try
        {
            DisconnectCurrent();
            lock (sync)
            {
                candidateGeneration = ++generation;
            }
            candidate = connectionFactory(requestedDeviceId);
            if (candidate is null)
            {
                SetState(MediaAudioSinkState.Failed, null);
                logger.Warning("A2DP.Connection.Unavailable", new Dictionary<string, object?>
                {
                    ["deviceId"] = requestedDeviceId,
                    ["reason"] = "TryCreateFromId returned null",
                });
                return false;
            }

            candidateStateChanged = sender => OnStateChanged(sender, candidateGeneration);
            lock (sync)
            {
                connection = candidate;
                connectionStateChanged = candidateStateChanged;
            }
            candidate.StateChanged += candidateStateChanged;
            SetState(MediaAudioSinkState.Starting, requestedDeviceId);
            logger.Debug("A2DP.Connection.Starting", new Dictionary<string, object?>
            {
                ["deviceId"] = requestedDeviceId,
                ["state"] = candidate.State.ToString(),
            });
            await candidate.StartAsync().ConfigureAwait(false);
            SetState(MediaAudioSinkState.Started, requestedDeviceId);
            SetState(MediaAudioSinkState.Opening, requestedDeviceId);
            var openStatus = await candidate.OpenAsync().ConfigureAwait(false);
            logger.Info("A2DP.Connection.OpenResult", new Dictionary<string, object?>
            {
                ["deviceId"] = requestedDeviceId,
                ["status"] = openStatus.ToString(),
                ["state"] = candidate.State.ToString(),
            });
            cancellationToken.ThrowIfCancellationRequested();
            if (openStatus != AudioPlaybackConnectionOpenResultStatus.Success)
            {
                SetState(MediaAudioSinkState.Failed, null);
                return false;
            }

            if (candidate.State == AudioPlaybackConnectionState.Closed)
            {
                SetState(MediaAudioSinkState.Disabled, null);
                return false;
            }

            lock (sync)
            {
                if (!ReferenceEquals(connection, candidate) || generation != candidateGeneration)
                {
                    return false;
                }

                state = MediaAudioSinkState.Opened;
                deviceId = requestedDeviceId;
            }
            logger.Info("A2DP.Connection.Opened", new Dictionary<string, object?>
            {
                ["deviceId"] = requestedDeviceId,
                ["state"] = candidate.State.ToString(),
                ["audioPath"] = "Windows system playback endpoint",
            });
            StateChanged?.Invoke(MediaAudioSinkState.Opened);
            candidate = null;
            candidateStateChanged = null;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (candidate is null || IsCurrent(candidate, candidateGeneration))
            {
                SetState(MediaAudioSinkState.Failed, null);
            }
            logger.Info("A2DP.Connection.Cancelled", new Dictionary<string, object?>
            {
                ["deviceId"] = requestedDeviceId,
            });
            throw;
        }
        catch (Exception exception)
        {
            if (candidate is null || IsCurrent(candidate, candidateGeneration))
            {
                SetState(MediaAudioSinkState.Failed, null);
            }
            logger.Error("A2DP.Connection.Failed", exception, new Dictionary<string, object?>
            {
                ["deviceId"] = requestedDeviceId,
            });
            return false;
        }
        finally
        {
            if (candidate is not null)
            {
                if (candidateStateChanged is not null)
                {
                    candidate.StateChanged -= candidateStateChanged;
                }

                lock (sync)
                {
                    if (ReferenceEquals(connection, candidate)
                        && generation == candidateGeneration)
                    {
                        connection = null;
                        connectionStateChanged = null;
                        deviceId = null;
                    }
                }
                candidate.Dispose();
            }

            lifecycleGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DisconnectCurrent();
        }
        finally
        {
            lifecycleGate.Release();
        }
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

    private void SetState(MediaAudioSinkState nextState, string? requestedDeviceId)
    {
        lock (sync)
        {
            state = nextState;
            deviceId = nextState is MediaAudioSinkState.Disabled or MediaAudioSinkState.Failed
                ? null
                : requestedDeviceId;
        }
        logger.Info("A2DP.Connection.State", new Dictionary<string, object?>
        {
            ["deviceId"] = requestedDeviceId,
            ["state"] = nextState.ToString(),
        });
        StateChanged?.Invoke(nextState);
    }

    private void DisconnectCurrent()
    {
        string? oldDeviceId;
        IA2dpConnection? oldConnection;
        Action<IA2dpConnection>? oldStateChanged;
        lock (sync)
        {
            oldConnection = connection;
            oldDeviceId = deviceId;
            oldStateChanged = connectionStateChanged;
            connection = null;
            connectionStateChanged = null;
            deviceId = null;
            state = MediaAudioSinkState.Disabled;
            generation++;
        }

        if (oldConnection is null)
        {
            logger.Debug("A2DP.Connection.NoActiveConnection");
            return;
        }

        if (oldStateChanged is not null)
        {
            oldConnection.StateChanged -= oldStateChanged;
        }
        oldConnection.Dispose();
        logger.Info("A2DP.Connection.Closed", new Dictionary<string, object?>
        {
            ["deviceId"] = oldDeviceId,
        });
        StateChanged?.Invoke(MediaAudioSinkState.Disabled);
    }

    private bool IsCurrent(IA2dpConnection candidate, long candidateGeneration)
    {
        lock (sync)
        {
            return ReferenceEquals(connection, candidate) && generation == candidateGeneration;
        }
    }

    private void OnStateChanged(IA2dpConnection sender, long senderGeneration)
    {
        MediaAudioSinkState nextState;
        lock (sync)
        {
            if (!ReferenceEquals(connection, sender) || generation != senderGeneration)
            {
                return;
            }

            nextState = sender.State switch
            {
                AudioPlaybackConnectionState.Opened => MediaAudioSinkState.Opened,
                AudioPlaybackConnectionState.Closed => MediaAudioSinkState.Disabled,
                _ => state,
            };
            state = nextState;
            deviceId = nextState == MediaAudioSinkState.Opened ? sender.DeviceId : null;
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
