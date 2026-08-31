using BTHaven.Core.Audio;
using BTHaven.Core.Bluetooth;
using BTHaven.Windows.Diagnostics;

namespace BTHaven.Windows.Audio;

public sealed class A2dpAutoReconnectService : IAsyncDisposable
{
    private readonly object sync = new();
    private readonly A2dpSinkService sink;
    private readonly IWindowsDiagnosticLogger logger;
    private CancellationTokenSource? cancellation;
    private Task? loop;
    private string? targetId;

    public A2dpAutoReconnectService(
        A2dpSinkService sink,
        IWindowsDiagnosticLogger? logger = null)
    {
        this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        this.logger = logger ?? NullDiagnosticLogger.Instance;
    }

    public bool IsEnabled
    {
        get
        {
            lock (sync)
            {
                return cancellation is not null;
            }
        }
    }

    public async Task EnableAsync(string requestedDeviceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedDeviceId);
        cancellationToken.ThrowIfCancellationRequested();
        await DisableAsync().ConfigureAwait(false);

        var source = new CancellationTokenSource();
        lock (sync)
        {
            cancellation = source;
            targetId = requestedDeviceId;
            loop = RunAsync(requestedDeviceId, source.Token);
        }
        logger.Info("A2DP.AutoReconnect.Enabled", new Dictionary<string, object?>
        {
            ["deviceId"] = requestedDeviceId,
            ["schedule"] = "1s,2s,5s,10s,30s,60s",
        });
    }

    public async Task DisableAsync()
    {
        CancellationTokenSource? source;
        Task? running;
        lock (sync)
        {
            source = cancellation;
            running = loop;
            cancellation = null;
            loop = null;
            targetId = null;
        }

        if (source is null)
        {
            return;
        }

        source.Cancel();
        if (running is not null)
        {
            try
            {
                await running.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (source.IsCancellationRequested)
            {
            }
        }
        source.Dispose();
        logger.Info("A2DP.AutoReconnect.Disabled");
    }

    public ValueTask DisposeAsync() => new(DisableAsync());

    private async Task RunAsync(string requestedDeviceId, CancellationToken cancellationToken)
    {
        var backoff = new ReconnectBackoff();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sink.IsEnabled && string.Equals(sink.DeviceId, requestedDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                logger.Debug("A2DP.AutoReconnect.Waiting", new Dictionary<string, object?>
                {
                    ["deviceId"] = requestedDeviceId,
                    ["state"] = sink.State.ToString(),
                });
                await WaitForStateChangeOrTimeoutAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            IReadOnlyList<RemoteAudioDeviceInfo> targets;
            try
            {
                targets = await sink.GetAvailableDevicesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.Error("A2DP.AutoReconnect.DiscoveryFailed", exception, new Dictionary<string, object?>
                {
                    ["deviceId"] = requestedDeviceId,
                });
                await DelayForRetryAsync(backoff, requestedDeviceId, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!targets.Any(target => string.Equals(target.Id, requestedDeviceId, StringComparison.OrdinalIgnoreCase)))
            {
                logger.Info("A2DP.AutoReconnect.TargetUnavailable", new Dictionary<string, object?>
                {
                    ["deviceId"] = requestedDeviceId,
                    ["availableCount"] = targets.Count,
                });
                await DelayForRetryAsync(backoff, requestedDeviceId, cancellationToken).ConfigureAwait(false);
                continue;
            }

            logger.Info("A2DP.AutoReconnect.Attempt", new Dictionary<string, object?>
            {
                ["deviceId"] = requestedDeviceId,
                ["attempt"] = "next",
            });
            if (await sink.ConnectAsync(requestedDeviceId, cancellationToken).ConfigureAwait(false))
            {
                backoff.Reset();
                logger.Info("A2DP.AutoReconnect.Succeeded", new Dictionary<string, object?>
                {
                    ["deviceId"] = requestedDeviceId,
                });
                await WaitForStateChangeOrTimeoutAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            await DelayForRetryAsync(backoff, requestedDeviceId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DelayForRetryAsync(
        ReconnectBackoff backoff,
        string requestedDeviceId,
        CancellationToken cancellationToken)
    {
        var delay = backoff.NextDelay();
        logger.Info("A2DP.AutoReconnect.RetryScheduled", new Dictionary<string, object?>
        {
            ["deviceId"] = requestedDeviceId,
            ["delaySeconds"] = delay.TotalSeconds,
        });
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    private async Task WaitForStateChangeOrTimeoutAsync(CancellationToken cancellationToken)
    {
        var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStateChanged(MediaAudioSinkState _) => signal.TrySetResult(true);
        sink.StateChanged += OnStateChanged;
        try
        {
            await Task.WhenAny(signal.Task, Task.Delay(TimeSpan.FromSeconds(30), cancellationToken)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            sink.StateChanged -= OnStateChanged;
        }
    }
}
