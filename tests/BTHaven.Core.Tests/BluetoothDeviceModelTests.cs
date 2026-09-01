using BTHaven.Core.Devices;

namespace BTHaven.Core.Tests;

public sealed class BluetoothDeviceModelTests
{
    [Fact]
    public void Paired_and_connected_are_independent_observations()
    {
        var device = new BluetoothDeviceModel
        {
            Id = "container:TEST",
            Name = "Test phone",
            IsPaired = true,
            IsConnected = false,
            IsPresent = true,
            Transport = BluetoothTransport.DualMode,
            Endpoints =
            [
                new BluetoothEndpointReference
                {
                    Id = "classic-endpoint",
                    Transport = BluetoothTransport.Classic,
                },
                new BluetoothEndpointReference
                {
                    Id = "ble-endpoint",
                    Transport = BluetoothTransport.LowEnergy,
                },
            ],
        };

        Assert.True(device.IsPaired);
        Assert.False(device.IsConnected);
        Assert.True(device.IsPresent);
        Assert.Equal("container:TEST", device.Id);
        Assert.Collection(
            device.Endpoints,
            endpoint => Assert.Equal(BluetoothTransport.Classic, endpoint.Transport),
            endpoint => Assert.Equal(BluetoothTransport.LowEnergy, endpoint.Transport));
    }
}
