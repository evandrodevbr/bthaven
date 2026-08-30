using BTHaven.Probes.Common;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Power;
using Windows.Storage.Streams;

const string BatteryLife = "System.Devices.BatteryLife";
const string BatteryPlusCharging = "System.Devices.BatteryPlusCharging";
const string ChargingState = "System.Devices.ChargingState";
const string IsConnected = "System.Devices.Aep.IsConnected";
const string IsPaired = "System.Devices.Aep.IsPaired";
const string IsPresent = "System.Devices.Aep.IsPresent";

ProbeLog.Header("02-battery");
var requestedProperties = new[] { BatteryLife, BatteryPlusCharging, ChargingState, IsConnected, IsPaired, IsPresent };
await ProbeWindowsBatteryReportsAsync();
await ProbeBluetoothAssociationPropertiesAsync(requestedProperties);
await ProbeGattBatteryAsync();
ProbeLog.Event("Probe.Completed");

static async Task ProbeWindowsBatteryReportsAsync()
{
    try
    {
        var batteryDevices = await DeviceInformation.FindAllAsync(Battery.GetDeviceSelector());
        ProbeLog.Event("Battery.Windows.ControllersFound", new { count = batteryDevices.Count });
        if (batteryDevices.Count == 0)
        {
            ProbeLog.Event("Battery.Windows.Unavailable", new { reason = "No battery controller was returned by Battery.GetDeviceSelector()" });
            return;
        }

        foreach (var deviceInfo in batteryDevices)
        {
            try
            {
                var battery = await Battery.FromIdAsync(deviceInfo.Id);
                var report = battery?.GetReport();
                var percentage = report?.FullChargeCapacityInMilliwattHours is > 0 && report.RemainingCapacityInMilliwattHours is >= 0
                    ? Math.Clamp((int)Math.Round(report.RemainingCapacityInMilliwattHours.Value * 100d / report.FullChargeCapacityInMilliwattHours.Value), 0, 100)
                    : (int?)null;
                ProbeLog.Event("Battery.Windows.Report", new
                {
                    deviceInfo.Id,
                    deviceInfo.Name,
                    status = report?.Status.ToString(),
                    percentage,
                    chargeRateMilliwatts = report?.ChargeRateInMilliwatts,
                    designCapacityMilliwattHours = report?.DesignCapacityInMilliwattHours,
                    fullChargeCapacityMilliwattHours = report?.FullChargeCapacityInMilliwattHours,
                    remainingCapacityMilliwattHours = report?.RemainingCapacityInMilliwattHours,
                    battery = percentage is null ? "unavailable" : "reported",
                });
            }
            catch (Exception exception)
            {
                ProbeLog.Error("Battery.Windows.FromIdOrGetReport", exception, new { deviceInfo.Id, deviceInfo.Name });
            }
        }
    }
    catch (Exception exception)
    {
        ProbeLog.Error("Battery.Windows.Enumerate", exception);
    }
}

static async Task ProbeBluetoothAssociationPropertiesAsync(IReadOnlyList<string> requestedProperties)
{
    try
    {
        var devices = await DeviceInformation.FindAllAsync(
            BluetoothDevice.GetDeviceSelectorFromPairingState(true),
            requestedProperties);
        ProbeLog.Event("Battery.AssociationProperties.DevicesFound", new { count = devices.Count });
        foreach (var device in devices)
        {
            var properties = DevicePropertySnapshot.Read(device);
            var hasBatteryProperty = properties.ContainsKey(BatteryLife) || properties.ContainsKey(BatteryPlusCharging) || properties.ContainsKey(ChargingState);
            ProbeLog.Event(hasBatteryProperty ? "Battery.AssociationProperties.Observed" : "Battery.AssociationProperties.Unavailable", new
            {
                device.Id,
                device.Name,
                isConnected = DevicePropertySnapshot.GetBool(properties, IsConnected),
                isPaired = DevicePropertySnapshot.GetBool(properties, IsPaired),
                isPresent = DevicePropertySnapshot.GetBool(properties, IsPresent),
                batteryLife = DevicePropertySnapshot.Get(properties, BatteryLife),
                batteryPlusCharging = DevicePropertySnapshot.Get(properties, BatteryPlusCharging),
                chargingState = DevicePropertySnapshot.Get(properties, ChargingState),
                rawProperties = properties,
            });
        }
    }
    catch (Exception exception)
    {
        ProbeLog.Error("Battery.AssociationProperties.Enumerate", exception);
    }
}

static async Task ProbeGattBatteryAsync()
{
    try
    {
        var devices = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true));
        ProbeLog.Event("Battery.Gatt.DevicesFound", new { count = devices.Count, serviceUuid = GattServiceUuids.Battery, characteristicUuid = GattCharacteristicUuids.BatteryLevel });
        foreach (var info in devices)
        {
            BluetoothLEDevice? device = null;
            try
            {
                device = await BluetoothLEDevice.FromIdAsync(info.Id);
                if (device is null)
                {
                    ProbeLog.Event("Battery.Gatt.Unavailable", new { info.Id, info.Name, reason = "BluetoothLEDevice.FromIdAsync returned null" });
                    continue;
                }

                var servicesResult = await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached);
                ProbeLog.Event("Battery.Gatt.ServiceQuery", new { info.Id, info.Name, status = servicesResult.Status.ToString(), serviceCount = servicesResult.Services.Count });
                if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
                {
                    ProbeLog.Event("Battery.Gatt.Unavailable", new { info.Id, info.Name, reason = "Battery Service was not returned" });
                    continue;
                }

                foreach (var service in servicesResult.Services)
                {
                    var characteristicsResult = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached);
                    ProbeLog.Event("Battery.Gatt.CharacteristicQuery", new { info.Id, info.Name, service = service.Uuid, status = characteristicsResult.Status.ToString(), characteristicCount = characteristicsResult.Characteristics.Count });
                    foreach (var characteristic in characteristicsResult.Characteristics)
                    {
                        var readResult = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
                        if (readResult.Status == GattCommunicationStatus.Success && readResult.Value is not null)
                        {
                            var reader = DataReader.FromBuffer(readResult.Value);
                            var level = reader.UnconsumedBufferLength > 0 ? reader.ReadByte() : (byte?)null;
                            ProbeLog.Event("Battery.Gatt.Report", new { info.Id, info.Name, status = readResult.Status.ToString(), percentage = level, source = "gatt-0x180f-0x2a19" });
                        }
                        else
                        {
                            ProbeLog.Event("Battery.Gatt.ReadUnavailable", new { info.Id, info.Name, status = readResult.Status.ToString() });
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                ProbeLog.Error("Battery.Gatt.Device", exception, new { info.Id, info.Name });
            }
            finally
            {
                device?.Dispose();
            }
        }
    }
    catch (Exception exception)
    {
        ProbeLog.Error("Battery.Gatt.Enumerate", exception);
    }
}
