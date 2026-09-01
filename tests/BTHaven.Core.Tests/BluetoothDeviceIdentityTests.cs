using BTHaven.Core.Devices;

namespace BTHaven.Core.Tests;

public sealed class BluetoothDeviceIdentityTests
{
    [Fact]
    public void Container_id_has_priority_over_address_and_endpoint_id()
    {
        var observation = Observation(
            id: "endpoint-classic",
            containerId: "container-01",
            address: "80:54:2D:51:3B:D6",
            transport: BluetoothTransport.Classic);

        var logicalId = BluetoothDeviceIdentity.GetLogicalId(observation);

        Assert.Equal("container:CONTAINER-01", logicalId);
    }

    [Fact]
    public void Address_is_normalized_when_container_id_is_missing()
    {
        var observation = Observation(
            id: "endpoint-ble",
            containerId: null,
            address: "80-54-2d-51-3b-d6",
            transport: BluetoothTransport.LowEnergy);

        var logicalId = BluetoothDeviceIdentity.GetLogicalId(observation);

        Assert.Equal("address:80542D513BD6", logicalId);
        Assert.Equal(logicalId, BluetoothDeviceIdentity.GetLogicalId(observation with
        {
            Address = "80:54:2D:51:3B:D6",
        }));
    }

    [Fact]
    public void Isolated_endpoint_id_is_namespaced_by_transport_without_identity()
    {
        var observation = Observation(
            id: "endpoint",
            containerId: null,
            address: null,
            transport: BluetoothTransport.LowEnergy);

        var logicalId = BluetoothDeviceIdentity.GetLogicalId(observation);

        Assert.Equal("endpoint:LowEnergy:endpoint", logicalId);
    }

    [Fact]
    public void Equal_names_do_not_merge_devices_with_different_identity_data()
    {
        var first = Observation(
            id: "endpoint-a",
            containerId: null,
            address: "00:11:22:33:44:55",
            transport: BluetoothTransport.Classic) with { Name = "Phone" };
        var second = Observation(
            id: "endpoint-b",
            containerId: null,
            address: "AA:BB:CC:DD:EE:FF",
            transport: BluetoothTransport.LowEnergy) with { Name = "Phone" };

        Assert.NotEqual(
            BluetoothDeviceIdentity.GetLogicalId(first),
            BluetoothDeviceIdentity.GetLogicalId(second));
    }

    [Fact]
    public void Projection_exposes_explicit_classic_and_ble_endpoint_references()
    {
        var classic = BluetoothDeviceProjection.ToModel(Observation(
            id: "classic-endpoint",
            containerId: "container-01",
            address: "80:54:2D:51:3B:D6",
            transport: BluetoothTransport.Classic));
        var ble = BluetoothDeviceProjection.ToModel(Observation(
            id: "ble-endpoint",
            containerId: "container-01",
            address: "80-54-2d-51-3b-d6",
            transport: BluetoothTransport.LowEnergy));

        Assert.Equal(classic.Id, ble.Id);
        Assert.Collection(
            classic.Endpoints,
            endpoint =>
            {
                Assert.Equal("classic-endpoint", endpoint.Id);
                Assert.Equal(BluetoothTransport.Classic, endpoint.Transport);
            });
        Assert.Collection(
            ble.Endpoints,
            endpoint =>
            {
                Assert.Equal("ble-endpoint", endpoint.Id);
                Assert.Equal(BluetoothTransport.LowEnergy, endpoint.Transport);
            });
    }

    private static BluetoothDeviceObservation Observation(
        string id,
        string? containerId,
        string? address,
        BluetoothTransport transport) => new()
        {
            Id = id,
            ContainerId = containerId,
            Name = "Device",
            Address = address,
            Transport = transport,
        };
}
