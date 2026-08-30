using BTHaven.Core.Devices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;


namespace BTHaven.Windows.Bluetooth;

internal static class WindowsBluetoothDeviceObservationFactory
{
    public static BluetoothDeviceObservation FromDeviceInformation(DeviceInformation device, BluetoothTransport transport)
    {
        ArgumentNullException.ThrowIfNull(device);
        return FromProperties(
            device.Id,
            device.Name,
            device.Properties,
            transport,
            device.Pairing?.IsPaired);
    }

    public static BluetoothDeviceObservation FromUpdate(
        BluetoothDeviceObservation previous,
        DeviceInformationUpdate update)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(update);

        var properties = update.Properties;
        var categories = WindowsDevicePropertyReader.Strings(properties, WindowsDevicePropertyNames.Category)
            .Select(ParseCategory)
            .Where(category => category != BluetoothDeviceCategory.Unknown)
            .Distinct()
            .ToArray();

        var category = categories.FirstOrDefault();
        return previous with
        {
            ContainerId = WindowsDevicePropertyReader.String(properties, WindowsDevicePropertyNames.ContainerId) ?? previous.ContainerId,
            Manufacturer = WindowsDevicePropertyReader.String(properties, WindowsDevicePropertyNames.Manufacturer) ?? previous.Manufacturer,
            Model = WindowsDevicePropertyReader.String(properties, WindowsDevicePropertyNames.ModelName) ?? previous.Model,
            Address = WindowsDevicePropertyReader.String(properties, WindowsDevicePropertyNames.DeviceAddress) ?? previous.Address,
            IsPaired = WindowsDevicePropertyReader.Bool(properties, WindowsDevicePropertyNames.IsPaired) ?? previous.IsPaired,
            IsConnected = WindowsDevicePropertyReader.Bool(properties, WindowsDevicePropertyNames.IsConnected) ?? previous.IsConnected,
            IsPresent = WindowsDevicePropertyReader.Bool(properties, WindowsDevicePropertyNames.IsPresent) ?? previous.IsPresent,
            Rssi = WindowsDevicePropertyReader.Int32(properties, WindowsDevicePropertyNames.SignalStrength) ?? previous.Rssi,
            Category = category == BluetoothDeviceCategory.Unknown ? previous.Category : category,
            Categories = categories.Length == 0 ? previous.Categories : categories,
            Capabilities = AddBatteryCapability(previous.Capabilities, properties),
            ObservedAt = DateTimeOffset.UtcNow,
        };
    }

    private static BluetoothDeviceObservation FromProperties(
        string id,
        string name,
        IReadOnlyDictionary<string, object> properties,
        BluetoothTransport transport,
        bool? pairingState)
    {
        var categories = WindowsDevicePropertyReader.Strings(properties, WindowsDevicePropertyNames.Category)
            .Select(ParseCategory)
            .Where(category => category != BluetoothDeviceCategory.Unknown)
            .Distinct()
            .ToArray();
        var category = categories.FirstOrDefault();
        if (category == BluetoothDeviceCategory.Unknown)
        {
            category = ParseCategory(name);
        }

        var capabilities = transport switch
        {
            BluetoothTransport.Classic => BluetoothCapabilities.Classic,
            BluetoothTransport.LowEnergy => BluetoothCapabilities.Ble,
            BluetoothTransport.DualMode => BluetoothCapabilities.Classic | BluetoothCapabilities.Ble,
            _ => BluetoothCapabilities.None,
        };
        capabilities = AddBatteryCapability(capabilities, properties);

        return new BluetoothDeviceObservation
        {
            Id = id,
            ContainerId = WindowsDevicePropertyReader.String(properties, WindowsDevicePropertyNames.ContainerId),
            Name = string.IsNullOrWhiteSpace(name) ? "Bluetooth device" : name,
            Manufacturer = WindowsDevicePropertyReader.String(properties, WindowsDevicePropertyNames.Manufacturer),
            Model = WindowsDevicePropertyReader.String(properties, WindowsDevicePropertyNames.ModelName),
            Address = WindowsDevicePropertyReader.String(properties, WindowsDevicePropertyNames.DeviceAddress),
            Transport = transport,
            Category = category,
            Categories = categories,
            IsPaired = WindowsDevicePropertyReader.Bool(properties, WindowsDevicePropertyNames.IsPaired) ?? pairingState,
            IsConnected = WindowsDevicePropertyReader.Bool(properties, WindowsDevicePropertyNames.IsConnected),
            IsPresent = WindowsDevicePropertyReader.Bool(properties, WindowsDevicePropertyNames.IsPresent),
            Rssi = WindowsDevicePropertyReader.Int32(properties, WindowsDevicePropertyNames.SignalStrength),
            Capabilities = capabilities,
            ObservedAt = DateTimeOffset.UtcNow,
        };
    }

    private static BluetoothCapabilities AddBatteryCapability(
        BluetoothCapabilities capabilities,
        IReadOnlyDictionary<string, object> properties)
    {
        return WindowsDevicePropertyReader.Contains(properties, WindowsDevicePropertyNames.BatteryLife)
            || WindowsDevicePropertyReader.Contains(properties, WindowsDevicePropertyNames.BatteryPlusCharging)
            ? capabilities | BluetoothCapabilities.Battery
            : capabilities;
    }

    private static BluetoothDeviceCategory ParseCategory(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return BluetoothDeviceCategory.Unknown;
        }

        var value = text.ToLowerInvariant();
        if (value.Contains("headphone") || value.Contains("headset") || value.Contains("earbud"))
        {
            return BluetoothDeviceCategory.Headphones;
        }
        if (value.Contains("speaker") || value.Contains("audio"))
        {
            return BluetoothDeviceCategory.Speaker;
        }
        if (value.Contains("smartphone") || value.Contains("phone") || value.Contains("mobile"))
        {
            return BluetoothDeviceCategory.Smartphone;
        }
        if (value.Contains("mouse"))
        {
            return BluetoothDeviceCategory.Mouse;
        }
        if (value.Contains("keyboard") || value.Contains("keypad"))
        {
            return BluetoothDeviceCategory.Keyboard;
        }
        if (value.Contains("controller") || value.Contains("gamepad"))
        {
            return BluetoothDeviceCategory.Controller;
        }
        if (value.Contains("peripheral"))
        {
            return BluetoothDeviceCategory.Peripheral;
        }

        return BluetoothDeviceCategory.Unknown;
    }
}
