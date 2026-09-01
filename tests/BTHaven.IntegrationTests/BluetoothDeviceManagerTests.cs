using BTHaven.Core.Devices;
using BTHaven.Windows.Bluetooth;

namespace BTHaven.IntegrationTests;

public sealed class BluetoothDeviceManagerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Dual_mode_endpoints_in_any_order_produce_one_stable_model(bool classicFirst)
    {
        await using var manager = new BluetoothDeviceManager();
        var classic = Observation(
            "classic-endpoint",
            BluetoothTransport.Classic,
            "container-01",
            "80:54:2D:51:3B:D6",
            isPaired: true,
            isConnected: false,
            capabilities: BluetoothCapabilities.Classic,
            services: ["RFCOMM"],
            profiles: ["A2DP"]);
        var ble = Observation(
            "ble-endpoint",
            BluetoothTransport.LowEnergy,
            "container-01",
            "80-54-2d-51-3b-d6",
            isPaired: false,
            isConnected: true,
            capabilities: BluetoothCapabilities.Ble | BluetoothCapabilities.Battery,
            services: ["GATT"],
            profiles: ["Battery"]);

        manager.ApplyObservationForTesting(classicFirst ? classic : ble);
        manager.ApplyObservationForTesting(classicFirst ? ble : classic);

        var model = Assert.Single(manager.GetModelsForTesting());
        Assert.Equal("container:CONTAINER-01", model.Id);
        Assert.Equal(BluetoothTransport.DualMode, model.Transport);
        Assert.True(model.IsPaired);
        Assert.True(model.IsConnected);
        Assert.True(model.IsPresent);
        Assert.Equal(BluetoothCapabilities.Classic | BluetoothCapabilities.Ble | BluetoothCapabilities.Battery, model.Capabilities);
        Assert.Equal(2, model.Endpoints.Count);
        Assert.Contains(model.Endpoints, endpoint => endpoint.Id == "classic-endpoint" && endpoint.Transport == BluetoothTransport.Classic);
        Assert.Contains(model.Endpoints, endpoint => endpoint.Id == "ble-endpoint" && endpoint.Transport == BluetoothTransport.LowEnergy);
        Assert.Contains("RFCOMM", model.Services);
        Assert.Contains("GATT", model.Services);
        Assert.Contains("A2DP", model.Profiles);
        Assert.Contains("Battery", model.Profiles);

        var added = ReadChange(manager);
        Assert.Equal(BluetoothDeviceChangeKind.Added, added.Kind);
        Assert.Null(added.EndpointId);
        var updated = ReadChange(manager);
        Assert.Equal(BluetoothDeviceChangeKind.Updated, updated.Kind);
        Assert.Equal(model.Id, updated.DeviceId);
        Assert.Null(updated.EndpointId);
    }

    [Fact]
    public async Task Partial_removal_emits_updated_with_same_logical_id_and_one_remaining_line()
    {
        await using var manager = new BluetoothDeviceManager();
        var classic = Observation("classic-endpoint", BluetoothTransport.Classic, "container-01", "001122334455");
        var ble = Observation("ble-endpoint", BluetoothTransport.LowEnergy, "container-01", "00-11-22-33-44-55");
        manager.ApplyObservationForTesting(classic);
        manager.ApplyObservationForTesting(ble);
        DrainChanges(manager, 2);

        manager.RemoveObservationForTesting(classic.Id, classic.Transport);

        var change = ReadChange(manager);
        var model = Assert.Single(manager.GetModelsForTesting());
        Assert.Equal(BluetoothDeviceChangeKind.Updated, change.Kind);
        Assert.Equal(classic.Id, change.EndpointId);
        Assert.Equal("container:CONTAINER-01", change.DeviceId);
        Assert.Equal(change.DeviceId, model.Id);
        Assert.Single(model.Endpoints);
        Assert.Equal(ble.Id, model.Endpoints[0].Id);
    }

    [Fact]
    public async Task Removing_the_final_endpoint_emits_removed_and_no_models()
    {
        await using var manager = new BluetoothDeviceManager();
        var classic = Observation("classic-endpoint", BluetoothTransport.Classic, "container-01", "001122334455");
        var ble = Observation("ble-endpoint", BluetoothTransport.LowEnergy, "container-01", "00-11-22-33-44-55");
        manager.ApplyObservationForTesting(classic);
        manager.ApplyObservationForTesting(ble);
        DrainChanges(manager, 2);
        manager.RemoveObservationForTesting(classic.Id, classic.Transport);
        _ = ReadChange(manager);

        manager.RemoveObservationForTesting(ble.Id, ble.Transport);

        var change = ReadChange(manager);
        Assert.Equal(BluetoothDeviceChangeKind.Removed, change.Kind);
        Assert.Equal(ble.Id, change.EndpointId);
        Assert.Equal("container:CONTAINER-01", change.DeviceId);
        Assert.Empty(manager.GetModelsForTesting());
    }

    [Fact]
    public async Task Incomplete_update_keeps_logical_identity_for_same_endpoint()
    {
        await using var manager = new BluetoothDeviceManager();
        var original = Observation("endpoint", BluetoothTransport.Classic, "container-stable", "001122334455");
        manager.ApplyObservationForTesting(original);
        _ = ReadChange(manager);

        manager.ApplyObservationForTesting(original with
        {
            ContainerId = null,
            Address = null,
        });

        var updated = ReadChange(manager);
        Assert.Equal(BluetoothDeviceChangeKind.Updated, updated.Kind);
        Assert.Equal("container:CONTAINER-STABLE", updated.DeviceId);
        Assert.NotNull(updated.Device);
        Assert.Equal(updated.DeviceId, updated.Device!.Id);
        var endpoint = Assert.Single(updated.Device.Endpoints);
        Assert.Equal(original.Id, endpoint.Id);
        Assert.Equal(original.Transport, endpoint.Transport);
        Assert.Equal(original.ContainerId, endpoint.ContainerId);
        Assert.Equal(original.Address, endpoint.Address);
        Assert.False(manager.TryReadChangeForTesting(out _));
    }

    [Fact]
    public async Task Endpoint_identity_change_emits_removed_old_then_added_new()
    {
        await using var manager = new BluetoothDeviceManager();
        var oldObservation = Observation("endpoint", BluetoothTransport.Classic, "container-old", "001122334455");
        var newObservation = oldObservation with
        {
            ContainerId = "container-new",
            Address = "AABBCCDDEEFF",
        };
        manager.ApplyObservationForTesting(oldObservation);
        _ = ReadChange(manager);

        manager.ApplyObservationForTesting(newObservation);

        var removed = ReadChange(manager);
        var added = ReadChange(manager);
        Assert.Equal(BluetoothDeviceChangeKind.Removed, removed.Kind);
        Assert.Equal("container:CONTAINER-OLD", removed.DeviceId);
        Assert.Equal(BluetoothDeviceChangeKind.Added, added.Kind);
        Assert.Equal("container:CONTAINER-NEW", added.DeviceId);
        Assert.Single(manager.GetModelsForTesting());
        Assert.Equal(added.DeviceId, manager.GetModelsForTesting().Single().Id);
    }

    [Fact]
    public async Task Equal_names_without_identity_data_remain_separate_models()
    {
        await using var manager = new BluetoothDeviceManager();
        var first = Observation("endpoint-a", BluetoothTransport.Classic, null, null) with { Name = "Phone" };
        var second = Observation("endpoint-b", BluetoothTransport.LowEnergy, null, null) with { Name = "Phone" };

        manager.ApplyObservationForTesting(first);
        manager.ApplyObservationForTesting(second);

        var models = manager.GetModelsForTesting();
        Assert.Equal(2, models.Count);
        Assert.Equal(2, models.Select(model => model.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(2, Enumerable.Range(0, 2).Select(_ => ReadChange(manager).Kind).Count(kind => kind == BluetoothDeviceChangeKind.Added));
    }

    private static BluetoothDeviceObservation Observation(
        string id,
        BluetoothTransport transport,
        string? containerId,
        string? address,
        bool isPaired = true,
        bool isConnected = true,
        BluetoothCapabilities capabilities = BluetoothCapabilities.None,
        IReadOnlyList<string>? services = null,
        IReadOnlyList<string>? profiles = null) => new()
        {
            Id = id,
            ContainerId = containerId,
            Name = "Device",
            Address = address,
            Transport = transport,
            Category = BluetoothDeviceCategory.Smartphone,
            IsPaired = isPaired,
            IsConnected = isConnected,
            IsPresent = true,
            Capabilities = capabilities,
            Services = services ?? [],
            Profiles = profiles ?? [],
        };

    private static BluetoothDeviceChange ReadChange(BluetoothDeviceManager manager)
    {
        Assert.True(manager.TryReadChangeForTesting(out var change));
        return change!;
    }

    private static void DrainChanges(BluetoothDeviceManager manager, int count)
    {
        for (var index = 0; index < count; index++)
        {
            _ = ReadChange(manager);
        }
    }
}
