using BTHaven.Core.Battery;
using BTHaven.Core.Contracts;
using BTHaven.Core.Devices;
using BTHaven.Windows.Bluetooth;
using BTHaven.Windows.Diagnostics;
using Windows.Devices.Enumeration;

namespace BTHaven.Windows.Battery;

public sealed class WindowsDevicePropertiesBatteryProvider : IBatteryProvider
{
    private static readonly IReadOnlyList<string> RequestedProperties =
    [
        WindowsDevicePropertyNames.BatteryLife,
        WindowsDevicePropertyNames.BatteryPlusCharging,
        WindowsDevicePropertyNames.ChargingState,
    ];

    private readonly IWindowsDiagnosticLogger logger;

    public WindowsDevicePropertiesBatteryProvider(IWindowsDiagnosticLogger? logger = null)
    {
        this.logger = logger ?? NullDiagnosticLogger.Instance;
    }

    public string Name => "windows-device-properties";

    public async Task<BatteryState> GetBatteryAsync(
        BluetoothDeviceModel device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();

        var selector = $"System.Devices.Aep.AepId:=\"{EscapeAqs(device.Id)}\"";
        logger.Debug("Battery.WindowsProperties.QueryStarted", new Dictionary<string, object?>
        {
            ["deviceId"] = device.Id,
            ["name"] = device.Name,
            ["selector"] = selector,
            ["properties"] = RequestedProperties,
        });
        var matches = await DeviceInformation.FindAllAsync(selector, RequestedProperties);
        var info = matches.FirstOrDefault();
        if (info is null)
        {
            logger.Info("Battery.WindowsProperties.Unavailable", new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
                ["reason"] = "No matching DeviceInformation",
            });
            return BatteryState.Unavailable(Name);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var batteryLife = WindowsDevicePropertyReader.Int32(info.Properties, WindowsDevicePropertyNames.BatteryLife);
        var batteryPlusCharging = WindowsDevicePropertyReader.Int32(info.Properties, WindowsDevicePropertyNames.BatteryPlusCharging);
        var chargingRaw = WindowsDevicePropertyReader.String(info.Properties, WindowsDevicePropertyNames.ChargingState);
        var charging = ParseChargingState(chargingRaw);
        logger.Debug("Battery.WindowsProperties.Values", new Dictionary<string, object?>
        {
            ["deviceId"] = device.Id,
            ["batteryLife"] = batteryLife,
            ["batteryPlusCharging"] = batteryPlusCharging,
            ["chargingState"] = chargingRaw,
            ["parsedCharging"] = charging,
        });

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
            logger.Info("Battery.WindowsProperties.Unavailable", new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
                ["reason"] = "Properties exposed no usable percentage or charging state",
            });
            return BatteryState.Unavailable(Name);
        }

        var state = new BatteryState
        {
            Percentage = percentage,
            IsCharging = charging,
            Source = Name,
            LastUpdated = DateTimeOffset.UtcNow,
            Confidence = BatteryConfidence.High,
        };
        logger.Info("Battery.WindowsProperties.Report", new Dictionary<string, object?>
        {
            ["deviceId"] = device.Id,
            ["percentage"] = state.Percentage,
            ["isCharging"] = state.IsCharging,
        });
        return state;
    }

    private static bool? ParseChargingState(string? raw)
    {
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
