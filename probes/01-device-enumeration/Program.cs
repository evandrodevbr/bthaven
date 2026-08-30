using BTHaven.Probes.Common;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

const string IsConnected = "System.Devices.Aep.IsConnected";
const string IsPaired = "System.Devices.Aep.IsPaired";
const string IsPresent = "System.Devices.Aep.IsPresent";
const string DeviceAddress = "System.Devices.Aep.DeviceAddress";
const string Manufacturer = "System.Devices.Aep.Manufacturer";
const string ModelName = "System.Devices.Aep.ModelName";
const string SignalStrength = "System.Devices.Aep.SignalStrength";
const string ContainerId = "System.Devices.Aep.ContainerId";
const string ProtocolId = "System.Devices.Aep.ProtocolId";

ProbeLog.Header("01-device-enumeration");
var arguments = ProbeArguments.Parse(args);
try
{
    var adapter = await BluetoothAdapter.GetDefaultAsync();
    ProbeLog.Event("Bluetooth.Adapter.Observed", adapter is null
        ? new { available = false, reason = "BluetoothAdapter.GetDefaultAsync returned null" }
        : new
        {
            available = true,
            adapter.IsClassicSupported,
            adapter.IsLowEnergySupported,
            adapter.BluetoothAddress,
        });
}
catch (Exception exception)
{
    ProbeLog.Error("BluetoothAdapter.GetDefaultAsync", exception);
}

var requestedProperties = new[]
{
    IsConnected, IsPaired, IsPresent, DeviceAddress, Manufacturer, ModelName,
    SignalStrength, ContainerId, ProtocolId,
};

var selectors = new (string Name, string Selector)[]
{
    ("classic-paired", BluetoothDevice.GetDeviceSelectorFromPairingState(true)),
    ("classic-connected", BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected)),
    ("ble-paired", BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)),
    ("ble-connected", BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected)),
};

var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
foreach (var (name, selector) in selectors)
{
    try
    {
        var devices = await DeviceInformation.FindAllAsync(selector, requestedProperties);
        ProbeLog.Event("Bluetooth.Snapshot.Completed", new { selectorName = name, count = devices.Count, selector });
        foreach (var device in devices)
        {
            var properties = DevicePropertySnapshot.Read(device);
            var firstObservation = seen.Add(device.Id);
            ProbeLog.Event(firstObservation ? "Bluetooth.Device.Observed" : "Bluetooth.Device.ObservedAgain", new
            {
                selectorName = name,
                device.Id,
                device.Name,
                kind = device.Kind.ToString(),
                pairingIsPaired = device.Pairing?.IsPaired,
                isConnected = DevicePropertySnapshot.GetBool(properties, IsConnected),
                isPaired = DevicePropertySnapshot.GetBool(properties, IsPaired),
                isPresent = DevicePropertySnapshot.GetBool(properties, IsPresent),
                address = DevicePropertySnapshot.Get(properties, DeviceAddress),
                containerId = DevicePropertySnapshot.Get(properties, ContainerId),
                manufacturer = DevicePropertySnapshot.Get(properties, Manufacturer),
                model = DevicePropertySnapshot.Get(properties, ModelName),
                signalStrength = DevicePropertySnapshot.GetInt(properties, SignalStrength),
                rawProperties = properties,
            });
        }
    }
    catch (Exception exception)
    {
        ProbeLog.Error($"snapshot:{name}", exception, new { selector });
    }
}

var watchSeconds = Math.Clamp(arguments.GetInt("--watch-seconds", 5), 1, 120);
foreach (var (name, selector) in new[]
{
    ("classic-paired", BluetoothDevice.GetDeviceSelectorFromPairingState(true)),
    ("ble-paired", BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)),
})
{
    await WatchAsync(name, selector, requestedProperties, watchSeconds);
}

ProbeLog.Event("Probe.Completed", new { uniqueDeviceCount = seen.Count, watchSeconds });

static async Task WatchAsync(string name, string selector, IReadOnlyList<string> requestedProperties, int seconds)
{
    DeviceWatcher? watcher = null;
    try
    {
        watcher = DeviceInformation.CreateWatcher(selector, requestedProperties);
        var enumerationCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Added += (_, device) => ProbeLog.Event("DeviceWatcher.Added", new { selectorName = name, device.Id, device.Name });
        watcher.Updated += (_, update) => ProbeLog.Event("DeviceWatcher.Updated", new { selectorName = name, update.Id, update.Properties });
        watcher.Removed += (_, update) => ProbeLog.Event("DeviceWatcher.Removed", new { selectorName = name, update.Id });
        watcher.EnumerationCompleted += (_, _) =>
        {
            ProbeLog.Event("DeviceWatcher.EnumerationCompleted", new { selectorName = name, status = watcher.Status.ToString() });
            enumerationCompleted.TrySetResult(true);
        };
        watcher.Stopped += (_, _) => ProbeLog.Event("DeviceWatcher.Stopped", new { selectorName = name, status = watcher.Status.ToString() });

        ProbeLog.Event("DeviceWatcher.Starting", new { selectorName = name, selector, seconds });
        watcher.Start();
        await Task.WhenAny(enumerationCompleted.Task, Task.Delay(TimeSpan.FromSeconds(seconds)));
        ProbeLog.Event("DeviceWatcher.StopRequested", new { selectorName = name, status = watcher.Status.ToString() });
        watcher.Stop();
        await Task.Delay(250);
    }
    catch (Exception exception)
    {
        ProbeLog.Error($"watch:{name}", exception, new { selector });
        if (watcher is not null)
        {
            try
            {
                watcher.Stop();
            }
            catch (Exception stopException)
            {
                ProbeLog.Error($"watch:{name}:stop", stopException, new { selector });
            }
        }
    }
}
