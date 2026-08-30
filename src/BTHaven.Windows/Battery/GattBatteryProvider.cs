using BTHaven.Core.Battery;
using BTHaven.Core.Contracts;
using BTHaven.Core.Devices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace BTHaven.Windows.Battery;

public sealed class GattBatteryProvider : IBatteryProvider, IAsyncDisposable
{
    private readonly object sync = new();
    private readonly Dictionary<string, Subscription> subscriptions = new(StringComparer.OrdinalIgnoreCase);

    public string Name => "gatt-0x180f-0x2a19";

    public event EventHandler<BatteryState>? BatteryChanged;

    public async Task<BatteryState> GetBatteryAsync(
        BluetoothDeviceModel device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();

        if (device.Transport == BluetoothTransport.Classic)
        {
            return BatteryState.Unavailable(Name);
        }

        var bluetoothDevice = await BluetoothLEDevice.FromIdAsync(device.Id);
        if (bluetoothDevice is null)
        {
            return BatteryState.Unavailable(Name);
        }

        try
        {
            var servicesResult = await bluetoothDevice.GetGattServicesForUuidAsync(
                GattServiceUuids.Battery,
                BluetoothCacheMode.Uncached);
            if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
            {
                return BatteryState.Unavailable(Name);
            }

            foreach (var service in servicesResult.Services)
            {
                var characteristicsResult = await service.GetCharacteristicsForUuidAsync(
                    GattCharacteristicUuids.BatteryLevel,
                    BluetoothCacheMode.Uncached);
                if (characteristicsResult.Status != GattCommunicationStatus.Success)
                {
                    continue;
                }

                foreach (var characteristic in characteristicsResult.Characteristics)
                {
                    var readResult = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
                    if (readResult.Status == GattCommunicationStatus.Success && TryReadLevel(readResult.Value, out var level))
                    {
                        return CreateState(level);
                    }
                }
            }

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
        if (device.Transport == BluetoothTransport.Classic)
        {
            return false;
        }

        var bluetoothDevice = await BluetoothLEDevice.FromIdAsync(device.Id);
        if (bluetoothDevice is null)
        {
            return false;
        }

        Subscription? createdSubscription = null;
        try
        {
            var servicesResult = await bluetoothDevice.GetGattServicesForUuidAsync(
                GattServiceUuids.Battery,
                BluetoothCacheMode.Uncached);
            if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
            {
                return false;
            }

            foreach (var service in servicesResult.Services)
            {
                var characteristicsResult = await service.GetCharacteristicsForUuidAsync(
                    GattCharacteristicUuids.BatteryLevel,
                    BluetoothCacheMode.Uncached);
                if (characteristicsResult.Status != GattCommunicationStatus.Success)
                {
                    continue;
                }

                var characteristic = characteristicsResult.Characteristics.FirstOrDefault(candidate =>
                    candidate.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify)
                    || candidate.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate));
                if (characteristic is null)
                {
                    continue;
                }

                TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> handler = (_, args) =>
                {
                    if (!TryReadLevel(args.CharacteristicValue, out var level))
                    {
                        return;
                    }

                    var state = CreateState(level);
                    onChanged?.Invoke(state);
                    BatteryChanged?.Invoke(this, state);
                };
                characteristic.ValueChanged += handler;
                var configuration = characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify)
                    ? GattClientCharacteristicConfigurationDescriptorValue.Notify
                    : GattClientCharacteristicConfigurationDescriptorValue.Indicate;
                var status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(configuration);
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
                return true;
            }

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
