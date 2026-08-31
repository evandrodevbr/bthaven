using BTHaven.Core.Battery;
using BTHaven.Core.Contracts;
using BTHaven.Core.Devices;
using BTHaven.Windows.Diagnostics;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace BTHaven.Windows.Battery;

public sealed class GattBatteryProvider : IBatteryProvider, IAsyncDisposable
{
    private readonly object sync = new();
    private readonly Dictionary<string, Subscription> subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly IWindowsDiagnosticLogger logger;

    public GattBatteryProvider(IWindowsDiagnosticLogger? logger = null)
    {
        this.logger = logger ?? NullDiagnosticLogger.Instance;
    }

    public string Name => "gatt-0x180f-0x2a19";

    public event EventHandler<BatteryState>? BatteryChanged;

    public async Task<BatteryState> GetBatteryAsync(
        BluetoothDeviceModel device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();
        logger.Debug("Battery.Gatt.QueryStarted", new Dictionary<string, object?>
        {
            ["deviceId"] = device.Id,
            ["name"] = device.Name,
            ["serviceUuid"] = GattServiceUuids.Battery,
            ["characteristicUuid"] = GattCharacteristicUuids.BatteryLevel,
        });

        if (device.Transport == BluetoothTransport.Classic)
        {
            logger.Info("Battery.Gatt.Unavailable", new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
                ["reason"] = "Device transport is Bluetooth Classic",
            });
            return BatteryState.Unavailable(Name);
        }

        var bluetoothDevice = await BluetoothLEDevice.FromIdAsync(device.Id);
        if (bluetoothDevice is null)
        {
            logger.Info("Battery.Gatt.Unavailable", new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
                ["reason"] = "BluetoothLEDevice.FromIdAsync returned null",
            });
            return BatteryState.Unavailable(Name);
        }

        try
        {
            var servicesResult = await bluetoothDevice.GetGattServicesForUuidAsync(
                GattServiceUuids.Battery,
                BluetoothCacheMode.Uncached);
            logger.Debug("Battery.Gatt.ServiceQuery", new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
                ["status"] = servicesResult.Status.ToString(),
                ["count"] = servicesResult.Services.Count,
            });
            if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
            {
                logger.Info("Battery.Gatt.Unavailable", new Dictionary<string, object?>
                {
                    ["deviceId"] = device.Id,
                    ["reason"] = "Battery Service was not returned",
                    ["status"] = servicesResult.Status.ToString(),
                });
                return BatteryState.Unavailable(Name);
            }

            foreach (var service in servicesResult.Services)
            {
                var characteristicsResult = await service.GetCharacteristicsForUuidAsync(
                    GattCharacteristicUuids.BatteryLevel,
                    BluetoothCacheMode.Uncached);
                logger.Debug("Battery.Gatt.CharacteristicQuery", new Dictionary<string, object?>
                {
                    ["deviceId"] = device.Id,
                    ["serviceUuid"] = service.Uuid,
                    ["status"] = characteristicsResult.Status.ToString(),
                    ["count"] = characteristicsResult.Characteristics.Count,
                });
                if (characteristicsResult.Status != GattCommunicationStatus.Success)
                {
                    continue;
                }

                foreach (var characteristic in characteristicsResult.Characteristics)
                {
                    var readResult = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
                    logger.Debug("Battery.Gatt.ReadResult", new Dictionary<string, object?>
                    {
                        ["deviceId"] = device.Id,
                        ["status"] = readResult.Status.ToString(),
                        ["valueLength"] = readResult.Value?.Length,
                    });
                    if (readResult.Status == GattCommunicationStatus.Success && TryReadLevel(readResult.Value, out var level))
                    {
                        var state = CreateState(level);
                        logger.Info("Battery.Gatt.Report", new Dictionary<string, object?>
                        {
                            ["deviceId"] = device.Id,
                            ["percentage"] = state.Percentage,
                        });
                        return state;
                    }
                }
            }

            logger.Info("Battery.Gatt.Unavailable", new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
                ["reason"] = "Battery Level characteristic did not return a usable value",
            });
            return BatteryState.Unavailable(Name);
        }
        finally
        {
            bluetoothDevice.Dispose();
        }
    }

    public async Task<bool> SubscribeAsync(
        BluetoothDeviceModel device,
        Action<BatteryState>? onChanged = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();
        logger.Debug("Battery.Gatt.SubscribeStarted", new Dictionary<string, object?>
        {
            ["deviceId"] = device.Id,
            ["name"] = device.Name,
        });
        if (device.Transport == BluetoothTransport.Classic)
        {
            logger.Info("Battery.Gatt.SubscribeUnavailable", new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
                ["reason"] = "Device transport is Bluetooth Classic",
            });
            return false;
        }

        var bluetoothDevice = await BluetoothLEDevice.FromIdAsync(device.Id);
        if (bluetoothDevice is null)
        {
            logger.Info("Battery.Gatt.SubscribeUnavailable", new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
                ["reason"] = "BluetoothLEDevice.FromIdAsync returned null",
            });
            return false;
        }

        Subscription? createdSubscription = null;
        try
        {
            var servicesResult = await bluetoothDevice.GetGattServicesForUuidAsync(
                GattServiceUuids.Battery,
                BluetoothCacheMode.Uncached);
            logger.Debug("Battery.Gatt.Subscribe.ServiceQuery", new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
                ["status"] = servicesResult.Status.ToString(),
                ["count"] = servicesResult.Services.Count,
            });
            if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
            {
                logger.Info("Battery.Gatt.SubscribeUnavailable", new Dictionary<string, object?>
                {
                    ["deviceId"] = device.Id,
                    ["reason"] = "Battery Service was not returned",
                    ["status"] = servicesResult.Status.ToString(),
                });
                return false;
            }

            foreach (var service in servicesResult.Services)
            {
                var characteristicsResult = await service.GetCharacteristicsForUuidAsync(
                    GattCharacteristicUuids.BatteryLevel,
                    BluetoothCacheMode.Uncached);
                logger.Debug("Battery.Gatt.Subscribe.CharacteristicQuery", new Dictionary<string, object?>
                {
                    ["deviceId"] = device.Id,
                    ["serviceUuid"] = service.Uuid,
                    ["status"] = characteristicsResult.Status.ToString(),
                    ["count"] = characteristicsResult.Characteristics.Count,
                });
                if (characteristicsResult.Status != GattCommunicationStatus.Success)
                {
                    continue;
                }

                var characteristic = characteristicsResult.Characteristics.FirstOrDefault(candidate =>
                    candidate.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify)
                    || candidate.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate));
                if (characteristic is null)
                {
                    logger.Info("Battery.Gatt.SubscribeUnavailable", new Dictionary<string, object?>
                    {
                        ["deviceId"] = device.Id,
                        ["reason"] = "Battery Level characteristic does not support notifications",
                    });
                    continue;
                }

                TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> handler = (_, args) =>
                {
                    if (!TryReadLevel(args.CharacteristicValue, out var level))
                    {
                        logger.Warning("Battery.Gatt.Notification.InvalidValue", new Dictionary<string, object?>
                        {
                            ["deviceId"] = device.Id,
                            ["valueLength"] = args.CharacteristicValue?.Length,
                        });
                        return;
                    }

                    var state = CreateState(level);
                    logger.Info("Battery.Gatt.Notification.ValueChanged", new Dictionary<string, object?>
                    {
                        ["deviceId"] = device.Id,
                        ["percentage"] = state.Percentage,
                    });
                    onChanged?.Invoke(state);
                    BatteryChanged?.Invoke(this, state);
                };
                characteristic.ValueChanged += handler;
                var configuration = characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify)
                    ? GattClientCharacteristicConfigurationDescriptorValue.Notify
                    : GattClientCharacteristicConfigurationDescriptorValue.Indicate;
                var status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(configuration);
                logger.Debug("Battery.Gatt.Subscribe.ConfigurationResult", new Dictionary<string, object?>
                {
                    ["deviceId"] = device.Id,
                    ["configuration"] = configuration.ToString(),
                    ["status"] = status.ToString(),
                });
                if (status != GattCommunicationStatus.Success)
                {
                    characteristic.ValueChanged -= handler;
                    continue;
                }

                var subscription = new Subscription(bluetoothDevice, service, characteristic, handler);
                lock (sync)
                {
                    if (subscriptions.Remove(device.Id, out var previous))
                    {
                        previous.Dispose();
                    }
                    subscriptions[device.Id] = subscription;
                }
                createdSubscription = subscription;
                logger.Info("Battery.Gatt.SubscribeCompleted", new Dictionary<string, object?>
                {
                    ["deviceId"] = device.Id,
                    ["configuration"] = configuration.ToString(),
                });
                return true;
            }

            logger.Info("Battery.Gatt.SubscribeUnavailable", new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
                ["reason"] = "No notifiable Battery Level characteristic was configured",
            });
            return false;
        }
        finally
        {
            if (createdSubscription is null)
            {
                bluetoothDevice.Dispose();
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        Subscription[] current;
        lock (sync)
        {
            current = subscriptions.Values.ToArray();
            subscriptions.Clear();
        }

        foreach (var subscription in current)
        {
            subscription.Dispose();
        }
        logger.Info("Battery.Gatt.ProviderDisposed", new Dictionary<string, object?>
        {
            ["subscriptionCount"] = current.Length,
        });
        return ValueTask.CompletedTask;
    }

    private BatteryState CreateState(byte level)
    {
        return new BatteryState
        {
            Percentage = level <= 100 ? level : null,
            IsCharging = null,
            Source = Name,
            LastUpdated = DateTimeOffset.UtcNow,
            Confidence = level <= 100 ? BatteryConfidence.High : BatteryConfidence.Unknown,
        };
    }

    private static bool TryReadLevel(IBuffer? buffer, out byte level)
    {
        if (buffer is null || buffer.Length < 1)
        {
            level = 0;
            return false;
        }

        var reader = DataReader.FromBuffer(buffer);
        level = reader.ReadByte();
        return true;
    }

    private sealed class Subscription : IDisposable
    {
        private readonly BluetoothLEDevice device;
        private readonly GattDeviceService service;
        private readonly GattCharacteristic characteristic;
        private readonly TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> handler;

        public Subscription(
            BluetoothLEDevice device,
            GattDeviceService service,
            GattCharacteristic characteristic,
            TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> handler)
        {
            this.device = device;
            this.service = service;
            this.characteristic = characteristic;
            this.handler = handler;
        }

        public void Dispose()
        {
            characteristic.ValueChanged -= handler;
            service.Dispose();
            device.Dispose();
        }
    }
}
