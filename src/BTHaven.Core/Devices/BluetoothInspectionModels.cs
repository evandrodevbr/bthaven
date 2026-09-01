using BTHaven.Core.Battery;
using BTHaven.Core.Audio;

namespace BTHaven.Core.Devices;

public sealed record BluetoothDeviceInspectionSnapshot
{
    public required string DeviceId { get; init; }
    public required string Name { get; init; }
    public string? ContainerId { get; init; }
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? Address { get; init; }
    public string? ClassOfDevice { get; init; }
    public string? ConnectionStatus { get; init; }
    public bool? IsPaired { get; init; }
    public bool? IsConnected { get; init; }
    public bool? IsPresent { get; init; }
    public int? Rssi { get; init; }
    public string? HostName { get; init; }
    public bool? WasSecureConnectionUsedForPairing { get; init; }
    public BluetoothTransport Transport { get; init; }
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<BluetoothObservedProperty> DeviceProperties { get; init; } = [];
    public IReadOnlyList<BluetoothEndpointInspection> Endpoints { get; init; } = [];
    public IReadOnlyList<BluetoothBatteryObservation> BatteryObservations { get; init; } = [];
    public IReadOnlyList<BluetoothProfileObservation> ProfileObservations { get; init; } = [];
    public RemoteVolumeStatus? RemoteVolume { get; init; }
    public IReadOnlyList<BluetoothGattServiceInspection> GattServices { get; init; } = [];
    public IReadOnlyList<BluetoothRfcommServiceInspection> RfcommServices { get; init; } = [];
    public IReadOnlyList<BluetoothInspectionDiagnostic> Diagnostics { get; init; } = [];
}

public sealed record BluetoothObservedProperty
{
    public required string Key { get; init; }
    public required string Type { get; init; }
    public string? Value { get; init; }
    public string Source { get; init; } = "DeviceInformation";
    public string Status { get; init; } = "Observed";
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? HResult { get; init; }
    public string? Message { get; init; }
}

public sealed record BluetoothBatteryObservation
{
    public required string Source { get; init; }
    public required string Status { get; init; }
    public int? Percentage { get; init; }
    public bool? IsCharging { get; init; }
    public BatteryConfidence Confidence { get; init; }
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? HResult { get; init; }
    public string? Message { get; init; }
}

public sealed record BluetoothProfileObservation
{
    public required string Profile { get; init; }
    public required string Source { get; init; }
    public required string Status { get; init; }
    public string? DeviceId { get; init; }
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? HResult { get; init; }
    public string? Message { get; init; }
}

public sealed record BluetoothEndpointInspection
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public string? Name { get; init; }
    public BluetoothTransport Transport { get; init; }
    public string? Source { get; init; }
    public string? ContainerId { get; init; }
    public string? Address { get; init; }
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public int? Rssi { get; init; }
    public bool? IsPaired { get; init; }
    public bool? IsConnected { get; init; }
    public bool? IsPresent { get; init; }
    public string? ProtocolId { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<BluetoothObservedProperty> Properties { get; init; } = [];
    public string? HResult { get; init; }
    public string? Message { get; init; }
}

public sealed record BluetoothGattServiceInspection
{
    public required string Uuid { get; init; }
    public ushort AttributeHandle { get; init; }
    public required string Status { get; init; }
    public string Source { get; init; } = "BluetoothLE.GATT";
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? HResult { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<BluetoothGattCharacteristicInspection> Characteristics { get; init; } = [];
}

public sealed record BluetoothGattCharacteristicInspection
{
    public required string Uuid { get; init; }
    public ushort AttributeHandle { get; init; }
    public string? Properties { get; init; }
    public string? UserDescription { get; init; }
    public required string Status { get; init; }
    public string? DescriptorStatus { get; init; }
    public string Source { get; init; } = "BluetoothLE.GATT";
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? HResult { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<BluetoothGattDescriptorInspection> Descriptors { get; init; } = [];
}

public sealed record BluetoothGattDescriptorInspection
{
    public required string Uuid { get; init; }
    public ushort AttributeHandle { get; init; }
    public string Source { get; init; } = "BluetoothLE.GATT";
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? HResult { get; init; }
    public string? Message { get; init; }
}

public sealed record BluetoothRfcommServiceInspection
{
    public required string ServiceId { get; init; }
    public string? KnownName { get; init; }
    public string? DeviceId { get; init; }
    public required string Status { get; init; }
    public string Source { get; init; } = "BluetoothClassic.RFCOMM";
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? HResult { get; init; }
    public string? Message { get; init; }
}

public sealed record BluetoothInspectionDiagnostic
{
    public required string Operation { get; init; }
    public string? Source { get; init; }
    public string? Status { get; init; }
    public string? HResult { get; init; }
    public string? ExceptionType { get; init; }
    public string? Message { get; init; }
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
}
