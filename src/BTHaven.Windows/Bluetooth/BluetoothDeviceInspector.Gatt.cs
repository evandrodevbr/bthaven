using BTHaven.Core.Devices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace BTHaven.Windows.Bluetooth;

public sealed partial class BluetoothDeviceInspector
{
    private async Task InspectBleAsync(
        BluetoothDeviceModel model,
        AssociationEndpoint association,
        List<BluetoothEndpointInspection> endpoints,
        List<BluetoothGattServiceInspection> gattServices,
        List<BluetoothInspectionDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        BluetoothLEDevice? bluetoothDevice = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            bluetoothDevice = await BluetoothLEDevice.FromIdAsync(association.Id);
            if (bluetoothDevice is null)
            {
                AddDiagnostic(diagnostics, "BluetoothLEDevice.FromIdAsync", "NotAvailable", null, null, null, "BluetoothLE.Device");
                return;
            }

            var observedProperties = ToProperties(association.Info.Properties, "BluetoothLE.Device");
            endpoints.Add(new BluetoothEndpointInspection
            {
                Id = bluetoothDevice.DeviceId,
                Kind = "BluetoothLE",
                Name = bluetoothDevice.Name,
                Transport = BluetoothTransport.LowEnergy,
                Source = "BluetoothLE.Device",
                ContainerId = association.ContainerId,
                Address = FormatAddress(bluetoothDevice.BluetoothAddress) ?? association.Address,
                IsPaired = association.IsPaired,
                IsConnected = bluetoothDevice.ConnectionStatus.ToString().Equals("Connected", StringComparison.OrdinalIgnoreCase),
                IsPresent = association.IsPresent,
                Manufacturer = association.Manufacturer,
                Model = association.Model,
                Rssi = association.Rssi,
                ProtocolId = association.ProtocolId,
                Status = bluetoothDevice.ConnectionStatus.ToString(),
                ObservedAt = DateTimeOffset.UtcNow,
                Properties = observedProperties,
            });
            AddDiagnostic(diagnostics, "BluetoothLEDevice.Properties", bluetoothDevice.ConnectionStatus.ToString(), null, null, null, "BluetoothLE.Device");
            logger.Info("Bluetooth.Gatt.DiscoveryStarted", new Dictionary<string, object?>
            {
                ["deviceId"] = association.Id,
                ["name"] = bluetoothDevice.Name,
            });

            GattDeviceServicesResult servicesResult;
            try
            {
                servicesResult = await bluetoothDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached);
                AddDiagnostic(diagnostics, "BluetoothLEDevice.GetGattServicesAsync", servicesResult.Status.ToString(), null, null, null, "BluetoothLE.GATT");
                logger.Info("Bluetooth.Gatt.ServicesEnumerated", new Dictionary<string, object?>
                {
                    ["deviceId"] = association.Id,
                    ["status"] = servicesResult.Status.ToString(),
                    ["count"] = servicesResult.Services.Count,
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                AddDiagnostic(diagnostics, "BluetoothLEDevice.GetGattServicesAsync", "Failed", exception, null, null, "BluetoothLE.GATT");
                return;
            }

            if (servicesResult.Status != GattCommunicationStatus.Success)
            {
                return;
            }

            foreach (var service in servicesResult.Services)
            {
                var serviceStatus = "Unknown";
                string? serviceHResult = null;
                string? serviceMessage = null;
                var characteristics = new List<BluetoothGattCharacteristicInspection>();
                try
                {
                    var characteristicsResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                    serviceStatus = characteristicsResult.Status.ToString();
                    AddDiagnostic(diagnostics, $"GATT.Service[{service.Uuid}].GetCharacteristicsAsync", serviceStatus, null, null, null, "BluetoothLE.GATT");
                    if (characteristicsResult.Status == GattCommunicationStatus.Success)
                    {
                        foreach (var characteristic in characteristicsResult.Characteristics)
                        {
                            try
                            {
                                characteristics.Add(await InspectCharacteristicAsync(characteristic, characteristicsResult.Status.ToString(), diagnostics, cancellationToken).ConfigureAwait(false));
                            }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception exception)
                            {
                                AddDiagnostic(diagnostics, $"GATT.Characteristic[{characteristic.Uuid}]", "Failed", exception, null, null, "BluetoothLE.GATT");
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    serviceStatus = "Failed";
                    serviceHResult = $"0x{exception.HResult:X8}";
                    serviceMessage = exception.Message;
                    AddDiagnostic(diagnostics, $"GATT.Service[{service.Uuid}].GetCharacteristicsAsync", "Failed", exception, null, null, "BluetoothLE.GATT");
                }
                finally
                {
                    gattServices.Add(new BluetoothGattServiceInspection
                    {
                        Uuid = service.Uuid.ToString(),
                        AttributeHandle = service.AttributeHandle,
                        Status = serviceStatus,
                        Source = "BluetoothLE.GATT",
                        ObservedAt = DateTimeOffset.UtcNow,
                        HResult = serviceHResult,
                        Message = serviceMessage,
                        Characteristics = characteristics,
                    });
                    service.Dispose();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AddDiagnostic(diagnostics, "BluetoothLE.Inspection", "Failed", exception, null, null, "BluetoothLE.Device");
            logger.Error("Bluetooth.Gatt.InspectionFailed", exception, new Dictionary<string, object?>
            {
                ["deviceId"] = model.Id,
                ["bleId"] = association.Id,
            });
        }
        finally
        {
            bluetoothDevice?.Dispose();
        }
    }

    private static async Task<BluetoothGattCharacteristicInspection> InspectCharacteristicAsync(
        GattCharacteristic characteristic,
        string characteristicStatus,
        List<BluetoothInspectionDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var descriptors = new List<BluetoothGattDescriptorInspection>();
        var status = characteristicStatus;
        string? descriptorStatus = null;
        string? hResult = null;
        string? message = null;
        try
        {
            var descriptorResult = await characteristic.GetDescriptorsAsync(BluetoothCacheMode.Uncached);
            descriptorStatus = descriptorResult.Status.ToString();
            AddDiagnostic(diagnostics, $"GATT.Characteristic[{characteristic.Uuid}].GetDescriptorsAsync", descriptorStatus, null, null, null, "BluetoothLE.GATT");
            if (descriptorResult.Status == GattCommunicationStatus.Success)
            {
                foreach (var descriptor in descriptorResult.Descriptors)
                {
                    descriptors.Add(new BluetoothGattDescriptorInspection
                    {
                        Uuid = descriptor.Uuid.ToString(),
                        AttributeHandle = descriptor.AttributeHandle,
                        Source = "BluetoothLE.GATT",
                        ObservedAt = DateTimeOffset.UtcNow,
                    });
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            descriptorStatus = "Failed";
            hResult = $"0x{exception.HResult:X8}";
            message = exception.Message;
            AddDiagnostic(diagnostics, $"GATT.Characteristic[{characteristic.Uuid}].GetDescriptorsAsync", descriptorStatus, exception, null, null, "BluetoothLE.GATT");
        }

        return new BluetoothGattCharacteristicInspection
        {
            Uuid = characteristic.Uuid.ToString(),
            AttributeHandle = characteristic.AttributeHandle,
            Properties = characteristic.CharacteristicProperties.ToString(),
            UserDescription = characteristic.UserDescription,
            Status = status,
            DescriptorStatus = descriptorStatus,
            Source = "BluetoothLE.GATT",
            ObservedAt = DateTimeOffset.UtcNow,
            HResult = hResult,
            Message = message,
            Descriptors = descriptors,
        };
    }
}
