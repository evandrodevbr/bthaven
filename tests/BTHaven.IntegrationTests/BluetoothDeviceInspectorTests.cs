using BTHaven.Core.Devices;
using BTHaven.Windows.Bluetooth;
using BTHaven.Windows.Diagnostics;

namespace BTHaven.IntegrationTests;

public sealed class BluetoothDeviceInspectorTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Inspects_a_real_paired_device_without_returning_winrt_objects()
    {
        await using var manager = new BluetoothDeviceManager(NullDiagnosticLogger.Instance);
        var devices = await manager.GetDevicesAsync(BluetoothDeviceFilter.All);
        Assert.NotEmpty(devices);

        var inspector = new BluetoothDeviceInspector(NullDiagnosticLogger.Instance);
        var snapshot = await inspector.InspectAsync(devices[0]);

        Assert.Equal(devices[0].Id, snapshot.DeviceId);
        Assert.Equal(devices[0].Name, snapshot.Name);
        Assert.NotEmpty(snapshot.Endpoints);
        Assert.NotNull(snapshot.Diagnostics);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("RequiresHardware", "Bluetooth")]
    public async Task Inspection_includes_classic_and_ble_endpoints_for_the_connected_phone()
    {
        await using var manager = new BluetoothDeviceManager(NullDiagnosticLogger.Instance);
        var devices = await manager.GetDevicesAsync(BluetoothDeviceFilter.All);
        Assert.NotEmpty(devices);

        var snapshot = await new BluetoothDeviceInspector(NullDiagnosticLogger.Instance)
            .InspectAsync(devices[0]);

        Assert.Contains(snapshot.Endpoints, endpoint => endpoint.Transport == BluetoothTransport.Classic);
        Assert.Contains(snapshot.Endpoints, endpoint => endpoint.Transport == BluetoothTransport.LowEnergy);
        Assert.Equal(devices[0].IsPaired, snapshot.IsPaired);
        Assert.Equal(devices[0].IsPresent, snapshot.IsPresent);
    }
}
