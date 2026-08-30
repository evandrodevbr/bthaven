using BTHaven.Core.Audio;
using BTHaven.Core.Calls;
using BTHaven.Core.Devices;

namespace BTHaven.Core.Contracts;

public interface IBluetoothDeviceService
{
    Task<IReadOnlyList<BluetoothDeviceModel>> GetDevicesAsync(
        BluetoothDeviceFilter filter = BluetoothDeviceFilter.Connected,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<BluetoothDeviceChange> WatchAsync(
        CancellationToken cancellationToken = default);
}

public interface IBatteryProvider
{
    string Name { get; }

    Task<Battery.BatteryState> GetBatteryAsync(
        BluetoothDeviceModel device,
        CancellationToken cancellationToken = default);
}

public interface IBatteryService
{
    Task<Battery.BatteryState> GetBatteryAsync(
        BluetoothDeviceModel device,
        CancellationToken cancellationToken = default);
}

public interface IMediaAudioSink
{
    bool IsEnabled { get; }
    string? DeviceId { get; }
    MediaAudioSinkState State { get; }
    Task<bool> ConnectAsync(string deviceId, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task EnableAsync(string deviceId, CancellationToken cancellationToken = default);
    Task DisableAsync(CancellationToken cancellationToken = default);
}

public interface IPhoneTransport
{
    CallState State { get; }
    Task<bool> ConnectAsync(string deviceId, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}

public interface ICallSession
{
    CallSessionSnapshot Snapshot { get; }
    event EventHandler<CallSessionSnapshot>? Changed;
    Task SetMuteAsync(bool muted, CancellationToken cancellationToken = default);
}

public interface IAudioEndpointService
{
    Task<IReadOnlyList<AudioEndpointModel>> GetEndpointsAsync(
        AudioEndpointDirection direction,
        CancellationToken cancellationToken = default);
}

public interface IAudioRouter
{
    Task RouteMediaAsync(string? endpointId, CancellationToken cancellationToken = default);
    Task RouteCallAsync(string? outputEndpointId, string? inputEndpointId, CancellationToken cancellationToken = default);
}

public interface IAudioProcessingPipeline
{
    bool IsEnabled { get; }
    ValueTask ProcessAsync(ReadOnlyMemory<float> input, Memory<float> output, CancellationToken cancellationToken = default);
}
