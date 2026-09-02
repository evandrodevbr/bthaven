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

        var endpoints = BluetoothEndpointSelection.SelectBatteryPropertyEndpoints(device);
        if (endpoints.Count == 0)
        {
            logger.Info("Battery.WindowsProperties.Unavailable", new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
                ["reason"] = "No endpoint reference",
            });
            return BatteryState.Unavailable(Name);
        }

        BatteryState? partialState = null;

        foreach (var endpoint in endpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selector = $"System.Devices.Aep.AepId:=\"{EscapeAqs(endpoint.Id)}\"";
            logger.Debug("Battery.WindowsProperties.QueryStarted", new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
                ["endpointId"] = endpoint.Id,
                ["name"] = device.Name,
                ["selector"] = selector,
                ["properties"] = RequestedProperties,
            });

            IReadOnlyList<DeviceInformation> matches;
            try
            {
                matches = await DeviceInformation.FindAllAsync(selector, RequestedProperties);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.Error("Battery.WindowsProperties.QueryFailed", exception, new Dictionary<string, object?>
                {
                    ["deviceId"] = device.Id,
                    ["endpointId"] = endpoint.Id,
                });
                continue;
            }

            var info = matches.FirstOrDefault();
            if (info is null)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var batteryLife = WindowsDevicePropertyReader.Byte(
                info.Properties,
                WindowsDevicePropertyNames.BatteryLife);
            var batteryPlusCharging = WindowsDevicePropertyReader.Byte(
                info.Properties,
                WindowsDevicePropertyNames.BatteryPlusCharging);
            var chargingState = WindowsDevicePropertyReader.Byte(
                info.Properties,
                WindowsDevicePropertyNames.ChargingState);
            var decoded = WindowsBatteryPropertyDecoder.Decode(
                batteryLife,
                batteryPlusCharging,
                chargingState);
            logger.Debug("Battery.WindowsProperties.Values", new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
                ["endpointId"] = endpoint.Id,
                ["batteryLife"] = batteryLife,
                ["batteryPlusCharging"] = batteryPlusCharging,
                ["chargingState"] = chargingState,
                ["parsedPercentage"] = decoded.Percentage,
                ["parsedCharging"] = decoded.IsCharging,
            });

            if (!decoded.HasData)
            {
                logger.Info("Battery.WindowsProperties.Unavailable", new Dictionary<string, object?>
                {
                    ["deviceId"] = device.Id,
                    ["endpointId"] = endpoint.Id,
                    ["reason"] = "Properties exposed no usable percentage or charging state",
                });
                continue;
            }

            var state = new BatteryState
            {
                Percentage = decoded.Percentage,
                IsCharging = decoded.IsCharging,
                Source = Name,
                LastUpdated = DateTimeOffset.UtcNow,
                Confidence = BatteryConfidence.High,
            };
            logger.Info("Battery.WindowsProperties.Report", new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
                ["endpointId"] = endpoint.Id,
                ["percentage"] = state.Percentage,
                ["isCharging"] = state.IsCharging,
            });
            if (state.Percentage is not null)
            {
                return state;
            }

            partialState ??= state;
        }

        if (partialState is not null)
        {
            return partialState;
        }

        logger.Info("Battery.WindowsProperties.Unavailable", new Dictionary<string, object?>
        {
            ["deviceId"] = device.Id,
            ["reason"] = "No endpoint exposed a usable percentage or charging state",
        });
        return BatteryState.Unavailable(Name);
    }

    private static string EscapeAqs(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
