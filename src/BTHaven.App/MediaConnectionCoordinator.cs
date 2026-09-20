using BTHaven.Core.Audio;
using BTHaven.Core.Devices;
using BTHaven.Windows.Audio;

namespace BTHaven_App;

// UI-free owner of target identity and the serialized connect/reconnect policy.
// The page invokes this on its dispatcher; service notifications are marshalled there first.
internal sealed class MediaConnectionCoordinator(A2dpSinkService sink, A2dpAutoReconnectService reconnect) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, string> bindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> invalidated = new(StringComparer.OrdinalIgnoreCase);

    public string? ActiveDeviceId { get; private set; }

    public void Dispose() => gate.Dispose();

    public bool MapsTo(string targetId, string deviceId) =>
        bindings.TryGetValue(targetId, out var mapped) && SameId(mapped, deviceId);

    public bool MapsToDifferentDevice(string targetId, string deviceId) =>
        bindings.TryGetValue(targetId, out var mapped) && !SameId(mapped, deviceId);

    public void ObserveTarget(string targetId, string deviceId)
    {
        if (!invalidated.Contains(targetId)) bindings[targetId] = deviceId;
    }

    private void Bind(string targetId, string deviceId)
    {
        bindings[targetId] = deviceId;
        invalidated.Remove(targetId);
    }

    public void RemoveDevice(string deviceId)
    {
        foreach (var target in bindings.Where(pair => SameId(pair.Value, deviceId)).Select(pair => pair.Key).ToArray())
        {
            bindings.Remove(target);
            invalidated.Add(target);
        }
        if (SameId(ActiveDeviceId, deviceId)) ActiveDeviceId = null;
    }

    public void Prune(IReadOnlyDictionary<string, BluetoothDeviceModel> devices)
    {
        foreach (var deviceId in bindings.Values.Where(id => !devices.ContainsKey(id)).Distinct().ToArray())
            RemoveDevice(deviceId);
        if (ActiveDeviceId is not null && !devices.ContainsKey(ActiveDeviceId)) ActiveDeviceId = null;
    }

    public bool ApplyState(MediaAudioSinkState state, string? targetId, IReadOnlyDictionary<string, BluetoothDeviceModel> devices)
    {
        if (sink.State != state || !SameId(sink.DeviceId, targetId)) return false;
        if (state == MediaAudioSinkState.Opened)
        {
            if (targetId is null || invalidated.Contains(targetId)) return false;
            var logicalId = bindings.GetValueOrDefault(targetId);
            if (logicalId is null || !devices.ContainsKey(logicalId))
            {
                var matches = devices.Values.Where(device => device.Endpoints.Any(endpoint => SameId(endpoint.Id, targetId))).ToArray();
                if (matches.Length != 1) return false;
                logicalId = matches[0].Id;
            }
            ActiveDeviceId = logicalId;
        }
        else if (state is MediaAudioSinkState.Disabled or MediaAudioSinkState.Failed)
        {
            if (!reconnect.IsEnabled) ActiveDeviceId = null;
        }
        return true;
    }

    public async Task<bool> ConnectAsync(
        string deviceId, string targetId, bool autoReconnect, CancellationToken cancellationToken,
        Func<bool>? isCurrent = null)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await reconnect.DisableAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (isCurrent?.Invoke() == false) return false;
            Bind(targetId, deviceId);
            var connected = await sink.ConnectAsync(targetId, cancellationToken);
            if (connected && !invalidated.Contains(targetId) && MapsTo(targetId, deviceId))
            {
                ActiveDeviceId = deviceId;
                if (autoReconnect) await reconnect.EnableAsync(targetId, cancellationToken);
            }
            return connected;
        }
        finally { gate.Release(); }
    }

    public async Task DisconnectAsync(string deviceId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!SameId(ActiveDeviceId, deviceId)) return;
            await reconnect.DisableAsync();
            RemoveDevice(deviceId);
            if (sink.DeviceId is { } targetId) invalidated.Add(targetId);
            await sink.DisconnectAsync(cancellationToken);
            ActiveDeviceId = null;
        }
        finally { gate.Release(); }
    }

    public async Task SetReconnectAsync(bool enabled, string? deviceId, string? targetId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (enabled && deviceId is not null && targetId is not null)
            {
                Bind(targetId, deviceId);
                await reconnect.EnableAsync(targetId, cancellationToken);
            }
            else if (!enabled)
            {
                foreach (var target in bindings.Keys) invalidated.Add(target);
                bindings.Clear();
                if (sink.DeviceId is { } current) invalidated.Add(current);
                await reconnect.DisableAsync();
            }
        }
        finally { gate.Release(); }
    }

    private static bool SameId(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
