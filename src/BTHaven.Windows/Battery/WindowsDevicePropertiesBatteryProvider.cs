using BTHaven.Core.Battery;
using BTHaven.Core.Devices;
using BTHaven.Windows.Bluetooth;
using Windows.Devices.Enumeration;

namespace BTHaven.Windows.Battery;

public sealed class WindowsDevicePropertiesBatteryProvider : BTHaven.Core.Contracts.IBatteryProvider
{
    private static readonly IReadOnlyList<string> RequestedProperties =
    [
        WindowsDevicePropertyNames.BatteryLife,
        WindowsDevicePropertyNames.BatteryPlusCharging,
        WindowsDevicePropertyNames.ChargingState,
    ];

    public string Name => "windows-device-properties";

    public async Task<BatteryState> GetBatteryAsync(
        BluetoothDeviceModel device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();

        var selector = $"System.Devices.Aep.AepId:=\"{EscapeAqs(device.Id)}\"";
        var matches = await DeviceInformation.FindAllAsync(selector, RequestedProperties);
        var info = matches.FirstOrDefault();
        if (info is null)
        {
            return BatteryState.Unavailable(Name);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var batteryLife = WindowsDevicePropertyReader.Int32(info.Properties, WindowsDevicePropertyNames.BatteryLife);
        var batteryPlusCharging = WindowsDevicePropertyReader.Int32(info.Properties, WindowsDevicePropertyNames.BatteryPlusCharging);
        var charging = ParseChargingState(info.Properties);

        var percentage = batteryLife is >= 0 and <= 100
            ? batteryLife
            : batteryPlusCharging is >= 0 and <= 100
                ? batteryPlusCharging
                : null;

        if (charging is null && batteryPlusCharging is >= 101)
        {
            charging = true;
        }
        else if (charging is null && batteryPlusCharging is >= 0 and <= 100)
        {
            charging = false;
        }

        if (percentage is null && charging is null)
        {
            return BatteryState.Unavailable(Name);
        }

        return new BatteryState
        {
            Percentage = percentage,
            IsCharging = charging,
            Source = Name,
            LastUpdated = DateTimeOffset.UtcNow,
            Confidence = BatteryConfidence.High,
        };
    }

    private static bool? ParseChargingState(IReadOnlyDictionary<string, object> properties)
    {
        var raw = WindowsDevicePropertyReader.String(properties, WindowsDevicePropertyNames.ChargingState);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.ToLowerInvariant();
        if (value.Contains("notcharging") || value.Contains("discharg") || value.Contains("idle"))
        {
            return false;
        }
        if (value.Contains("charg"))
        {
            return true;
        }

        return null;
    }

    private static string EscapeAqs(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
