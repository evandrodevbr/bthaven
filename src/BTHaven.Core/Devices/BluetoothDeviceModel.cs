namespace BTHaven.Core.Devices;

[Flags]
public enum BluetoothCapabilities
{
    None = 0,
    MediaAudio = 1 << 0,
    PhoneCalls = 1 << 1,
    Ble = 1 << 2,
    Classic = 1 << 3,
    Battery = 1 << 4,
    Gatt = 1 << 5,
}

public enum BluetoothTransport
{
    Unknown,
    Classic,
    LowEnergy,
    DualMode,
}

public sealed record BluetoothDeviceModel
{
    public required string Id { get; init; }
    public string? ContainerId { get; init; }
    public required string Name { get; init; }
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? Address { get; init; }
    public BluetoothTransport Transport { get; init; }
    public bool IsPaired { get; init; }
    public bool IsConnected { get; init; }
    public bool IsPresent { get; init; }
    public int? Rssi { get; init; }
    public Battery.BatteryState? Battery { get; init; }
    public BluetoothCapabilities Capabilities { get; init; }
    public DateTimeOffset LastUpdated { get; init; } = DateTimeOffset.UtcNow;
}

public enum BluetoothDeviceFilter
{
    All,
    Connected,
    Paired,
    Ble,
    Classic,
    Audio,
    Smartphones,
    Peripherals,
}

public enum BluetoothDeviceChangeKind
{
    Added,
    Updated,
    Removed,
}

public sealed record BluetoothDeviceChange
{
    public required BluetoothDeviceChangeKind Kind { get; init; }
    public required string DeviceId { get; init; }
    public BluetoothDeviceModel? Device { get; init; }
}
