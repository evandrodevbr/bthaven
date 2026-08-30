using BTHaven.Core.Devices;
using BTHaven.Windows.Bluetooth;

namespace BTHaven.IntegrationTests;

public sealed class BluetoothDeviceManagerSmokeTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Manager_returns_a_unique_snapshot_from_the_live_watcher()
    {
        await using var manager = new BluetoothDeviceManager();

        var devices = await manager.GetDevicesAsync(BluetoothDeviceFilter.All);

        Assert.NotNull(devices);
        Assert.Equal(devices.Count, devices.Select(device => device.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(devices, device => Assert.False(string.IsNullOrWhiteSpace(device.Id)));
    }
}
