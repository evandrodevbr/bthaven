using BTHaven.Core.Devices;

namespace BTHaven.Core.Tests;

public sealed class BluetoothDeviceModelTests
{
    [Fact]
    public void Paired_and_connected_are_independent_observations()
    {
        var device = new BluetoothDeviceModel
        {
            Id = "test-id",
            Name = "Test phone",
            IsPaired = true,
            IsConnected = false,
            IsPresent = true,
            Transport = BluetoothTransport.DualMode,
        };

        Assert.True(device.IsPaired);
        Assert.False(device.IsConnected);
        Assert.True(device.IsPresent);
    }
}
