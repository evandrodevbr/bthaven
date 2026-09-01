using BTHaven.Core.Devices;
using BTHaven.Windows.Bluetooth;
using BTHaven.Windows.Diagnostics;

namespace BTHaven.IntegrationTests;

public sealed class BluetoothDeviceInspectorTests
{
    [Fact]
    [Trait("Category", "Integration")]
    [Trait("RequiresHardware", "Bluetooth")]
    public async Task Inspects_a_real_paired_device_without_returning_winrt_objects()
    {
        await using var manager = new BluetoothDeviceManager(NullDiagnosticLogger.Instance);
        var devices = await manager.GetDevicesAsync(BluetoothDeviceFilter.All);
        Assert.True(devices.Count > 0, "Paired Bluetooth hardware device required.");

        var inspector = new BluetoothDeviceInspector(NullDiagnosticLogger.Instance);
        var snapshot = await inspector.InspectAsync(devices[0]);

        Assert.Equal(devices[0].Id, snapshot.DeviceId);
        Assert.Equal(devices[0].Name, snapshot.Name);
        Assert.True(snapshot.Endpoints.Count > 0, "Inspectable Bluetooth hardware endpoint required.");
        Assert.NotNull(snapshot.Diagnostics);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("RequiresHardware", "Bluetooth")]
    public async Task Inspection_includes_classic_and_ble_endpoints_for_the_connected_phone()
    {
        await using var manager = new BluetoothDeviceManager(NullDiagnosticLogger.Instance);
        var devices = await manager.GetDevicesAsync(BluetoothDeviceFilter.All);
        Assert.True(devices.Count > 0, "Paired Bluetooth hardware device required.");

        var device = devices.FirstOrDefault(candidate =>
            candidate.Transport == BluetoothTransport.DualMode && candidate.IsConnected);
        Assert.True(
            device is not null,
            "Connected dual-mode Bluetooth hardware phone required.");

        var snapshot = await new BluetoothDeviceInspector(NullDiagnosticLogger.Instance)
            .InspectAsync(device!);

        Assert.Contains(snapshot.Endpoints, endpoint => endpoint.Transport == BluetoothTransport.Classic);
        Assert.Contains(snapshot.Endpoints, endpoint => endpoint.Transport == BluetoothTransport.LowEnergy);
        Assert.Equal(device!.IsPaired, snapshot.IsPaired);
        Assert.Equal(device.IsPresent, snapshot.IsPresent);
    }
}
