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

    [Fact]
    public void Preferred_connection_chooses_connected_endpoint_before_transport_order()
    {
        var observedAt = DateTimeOffset.Parse("2026-09-01T12:00:00Z");
        var device = Device(
            Endpoint("classic", BluetoothTransport.Classic, connected: false, present: true, observedAt.AddMinutes(1)),
            Endpoint("ble", BluetoothTransport.LowEnergy, connected: true, present: true, observedAt));

        var selected = BluetoothEndpointSelection.SelectPreferredConnection(device);

        Assert.Equal("ble", selected!.Id);
    }

    [Fact]
    public void Preferred_connection_uses_present_then_newest_then_transport_and_id_ties()
    {
        var observedAt = DateTimeOffset.Parse("2026-09-01T12:00:00Z");
        var device = Device(
            Endpoint("absent", BluetoothTransport.Classic, connected: false, present: false, observedAt.AddMinutes(3)),
            Endpoint("older", BluetoothTransport.LowEnergy, connected: false, present: true, observedAt),
            Endpoint("z-classic", BluetoothTransport.Classic, connected: false, present: true, observedAt.AddMinutes(2)),
            Endpoint("b-ble", BluetoothTransport.LowEnergy, connected: false, present: true, observedAt.AddMinutes(2)),
            Endpoint("a-ble", BluetoothTransport.LowEnergy, connected: false, present: true, observedAt.AddMinutes(2)));

        var selected = BluetoothEndpointSelection.SelectPreferredConnection(device);

        Assert.Equal("z-classic", selected!.Id);
        var idTieDevice = Device(
            Endpoint("z-classic", BluetoothTransport.Classic, connected: false, present: true, observedAt),
            Endpoint("a-classic", BluetoothTransport.Classic, connected: false, present: true, observedAt));

        Assert.Equal(
            "a-classic",
            BluetoothEndpointSelection.SelectPreferredConnection(idTieDevice)!.Id);
    }

    private static BluetoothDeviceModel Device(params BluetoothEndpointReference[] endpoints) => new()
    {
        Id = "container:TEST",
        Name = "Test phone",
        Endpoints = endpoints,
    };

    private static BluetoothEndpointReference Endpoint(
        string id,
        BluetoothTransport transport,
        bool connected,
        bool present,
        DateTimeOffset observedAt) => new()
        {
            Id = id,
            Transport = transport,
            IsConnected = connected,
            IsPresent = present,
            ObservedAt = observedAt,
        };

}
