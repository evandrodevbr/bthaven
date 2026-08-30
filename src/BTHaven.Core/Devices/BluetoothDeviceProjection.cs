namespace BTHaven.Core.Devices;

public sealed record BluetoothDeviceObservation
{
    public required string Id { get; init; }
    public string? ContainerId { get; init; }
    public required string Name { get; init; }
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? Address { get; init; }
    public BluetoothTransport Transport { get; init; }
    public BluetoothDeviceCategory Category { get; init; }
    public IReadOnlyList<BluetoothDeviceCategory> Categories { get; init; } = [];
    public bool? IsPaired { get; init; }
    public bool? IsConnected { get; init; }
    public bool? IsPresent { get; init; }
    public int? Rssi { get; init; }
    public BluetoothCapabilities Capabilities { get; init; }
    public IReadOnlyList<string> Services { get; init; } = [];
    public IReadOnlyList<string> Profiles { get; init; } = [];
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
}

public static class BluetoothDeviceProjection
{
    public static BluetoothDeviceModel ToModel(BluetoothDeviceObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        return new BluetoothDeviceModel
        {
            Id = observation.Id,
            ContainerId = observation.ContainerId,
            Name = observation.Name,
            Manufacturer = observation.Manufacturer,
            Model = observation.Model,
            Address = observation.Address,
            Transport = observation.Transport,
            Category = observation.Category != BluetoothDeviceCategory.Unknown
                ? observation.Category
                : observation.Categories.FirstOrDefault(),
            IsPaired = observation.IsPaired ?? false,
            IsConnected = observation.IsConnected ?? false,
            IsPresent = observation.IsPresent ?? false,
            Rssi = observation.Rssi,
            Capabilities = observation.Capabilities,
            Services = observation.Services,
            Profiles = observation.Profiles,
            LastUpdated = observation.ObservedAt,
        };
    }
}

public static class BluetoothDeviceFilterMatcher
{
    public static bool Matches(BluetoothDeviceModel device, BluetoothDeviceFilter filter)
    {
        ArgumentNullException.ThrowIfNull(device);

        return filter switch
        {
            BluetoothDeviceFilter.All => true,
            BluetoothDeviceFilter.Connected => device.IsConnected,
            BluetoothDeviceFilter.Paired => device.IsPaired,
            BluetoothDeviceFilter.Ble => device.Transport is BluetoothTransport.LowEnergy or BluetoothTransport.DualMode,
            BluetoothDeviceFilter.Classic => device.Transport is BluetoothTransport.Classic or BluetoothTransport.DualMode,
            BluetoothDeviceFilter.Audio => (device.Capabilities & (BluetoothCapabilities.MediaAudio | BluetoothCapabilities.PhoneCalls)) != 0,
            BluetoothDeviceFilter.Smartphones => device.Category == BluetoothDeviceCategory.Smartphone,
            BluetoothDeviceFilter.Peripherals => device.Category is BluetoothDeviceCategory.Mouse
                or BluetoothDeviceCategory.Keyboard
                or BluetoothDeviceCategory.Controller
                or BluetoothDeviceCategory.Peripheral,
            _ => false,
        };
    }
}