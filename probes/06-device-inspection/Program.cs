using System.Text.Json;
using BTHaven.Core.Audio;
using BTHaven.Core.Devices;
using BTHaven.Probes.Common;
using BTHaven.Windows.Audio;
using BTHaven.Windows.Battery;
using BTHaven.Windows.Bluetooth;
using BTHaven.Windows.Diagnostics;
using BTHaven.Windows.Telephony;

ProbeLog.Header("06-device-inspection");
var arguments = ProbeArguments.Parse(args);
await using var manager = new BluetoothDeviceManager(TraceDiagnosticLogger.Instance);

try
{
    var devices = await manager.GetDevicesAsync(BluetoothDeviceFilter.All);
    var requestedId = arguments.Get("--device-id");
    var device = string.IsNullOrWhiteSpace(requestedId)
        ? devices.FirstOrDefault()
        : devices.FirstOrDefault(candidate => string.Equals(candidate.Id, requestedId, StringComparison.OrdinalIgnoreCase));

    ProbeLog.Event("Inspection.DeviceSelection", new
    {
        requestedId,
        availableCount = devices.Count,
        selectedId = device?.Id,
        selectedName = device?.Name,
    });

    if (device is null)
    {
        ProbeLog.Event("Inspection.NotExercised", new
        {
            reason = "No paired Bluetooth device was returned; pass --device-id or pair a device first",
        });
        return;
    }

    await using var batteryService = new WindowsBatteryService(TraceDiagnosticLogger.Instance);
    await using var a2dpService = new A2dpSinkService(TraceDiagnosticLogger.Instance);
    await using var hfpService = new HfpPhoneTransportService(TraceDiagnosticLogger.Instance);
    var remoteVolumeService = new WindowsRemoteVolumeService(TraceDiagnosticLogger.Instance);
    var inspector = new BluetoothDeviceInspector(
        TraceDiagnosticLogger.Instance,
        batteryService,
        a2dpService,
        hfpService,
        remoteVolumeService);
    var snapshot = await inspector.InspectAsync(device);
    ProbeLog.Event("Inspection.Summary", new
    {
        endpointCount = snapshot.Endpoints.Count,
        rssi = snapshot.Rssi,
        classicEndpointCount = snapshot.Endpoints.Count(endpoint => endpoint.Transport == BluetoothTransport.Classic),
        bleEndpointCount = snapshot.Endpoints.Count(endpoint => endpoint.Transport == BluetoothTransport.LowEnergy),
        gattServiceCount = snapshot.GattServices.Count,
        rfcommServiceCount = snapshot.RfcommServices.Count,
        battery = snapshot.BatteryObservations.Select(observation => new { observation.Source, observation.Status, observation.Percentage, observation.IsCharging, confidence = observation.Confidence.ToString() }),
        profiles = snapshot.ProfileObservations.Select(observation => new { observation.Profile, observation.Source, observation.Status }),
        remoteVolumeAvailability = snapshot.RemoteVolume?.Availability.ToString(),
        remoteVolumeSource = snapshot.RemoteVolume?.Source,
        diagnosticCount = snapshot.Diagnostics.Count,
    });
    ProbeLog.Event("Inspection.Completed", snapshot);
}
catch (Exception exception)
{
    ProbeLog.Error("Inspection", exception);
}
