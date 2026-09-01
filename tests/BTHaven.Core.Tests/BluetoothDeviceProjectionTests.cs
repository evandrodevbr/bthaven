using BTHaven.Core.Devices;

namespace BTHaven.Core.Tests;

public sealed class BluetoothDeviceProjectionTests
{
    [Fact]
    public void Projection_preserves_independent_paired_connected_present_states()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-30T19:00:00Z");
        var observation = new BluetoothDeviceObservation
        {
            Id = "endpoint-id",
            ContainerId = "container-id",
            Name = "Test phone",
            Manufacturer = "Test vendor",
            Model = "Test model",
            Address = "00:11:22:33:44:55",
            Transport = BluetoothTransport.DualMode,
            IsPaired = true,
            IsConnected = false,
            IsPresent = true,
            Rssi = -42,
            Categories = [BluetoothDeviceCategory.Smartphone],
            Capabilities = BluetoothCapabilities.Classic | BluetoothCapabilities.Ble,
            ObservedAt = observedAt,
        };

        var model = BluetoothDeviceProjection.ToModel(observation);

        Assert.Equal("container:CONTAINER-ID", model.Id);
        Assert.Equal("container-id", model.ContainerId);
        Assert.Collection(
            model.Endpoints,
            endpoint =>
            {
                Assert.Equal("endpoint-id", endpoint.Id);
                Assert.Equal(BluetoothTransport.DualMode, endpoint.Transport);
                Assert.Equal("container-id", endpoint.ContainerId);
            });
        Assert.True(model.IsPaired);
        Assert.False(model.IsConnected);
        Assert.True(model.IsPresent);
        Assert.Equal(BluetoothTransport.DualMode, model.Transport);
        Assert.Equal(BluetoothDeviceCategory.Smartphone, model.Category);
        Assert.Equal(observedAt, model.LastUpdated);
    }

    [Fact]
    public void Dual_mode_device_matches_both_transport_filters()
    {
        var device = new BluetoothDeviceModel
        {
            Id = "dual-mode",
            Name = "Dual mode",
            Transport = BluetoothTransport.DualMode,
        };

        Assert.True(BluetoothDeviceFilterMatcher.Matches(device, BluetoothDeviceFilter.Ble));
        Assert.True(BluetoothDeviceFilterMatcher.Matches(device, BluetoothDeviceFilter.Classic));
    }

    [Fact]
    public void Paired_filter_does_not_include_a_disconnected_unpaired_device()
    {
        var device = new BluetoothDeviceModel
        {
            Id = "unpaired",
            Name = "Not paired",
            IsPaired = false,
            IsConnected = false,
            IsPresent = true,
        };

        Assert.False(BluetoothDeviceFilterMatcher.Matches(device, BluetoothDeviceFilter.Paired));
        Assert.False(BluetoothDeviceFilterMatcher.Matches(device, BluetoothDeviceFilter.Connected));
    }

    [Fact]
    public void Audio_and_peripheral_filters_use_capabilities_and_category()
    {
        var headset = new BluetoothDeviceModel
        {
            Id = "headset",
            Name = "Headset",
            Category = BluetoothDeviceCategory.Headphones,
            Capabilities = BluetoothCapabilities.MediaAudio,
        };
        var mouse = new BluetoothDeviceModel
        {
            Id = "mouse",
            Name = "Mouse",
            Category = BluetoothDeviceCategory.Mouse,
        };

        Assert.True(BluetoothDeviceFilterMatcher.Matches(headset, BluetoothDeviceFilter.Audio));
        Assert.True(BluetoothDeviceFilterMatcher.Matches(mouse, BluetoothDeviceFilter.Peripherals));
        Assert.False(BluetoothDeviceFilterMatcher.Matches(mouse, BluetoothDeviceFilter.Audio));
    }
    [Fact]
    public void Gatt_selection_uses_a_real_low_energy_endpoint_instead_of_logical_id()
    {
        var device = new BluetoothDeviceModel
        {
            Id = "container:PHONE",
            Name = "Phone",
            Endpoints =
            [
                new BluetoothEndpointReference { Id = "classic-endpoint", Transport = BluetoothTransport.Classic },
                new BluetoothEndpointReference { Id = "dual-endpoint", Transport = BluetoothTransport.DualMode },
                new BluetoothEndpointReference { Id = "ble-endpoint", Transport = BluetoothTransport.LowEnergy },
                new BluetoothEndpointReference { Id = string.Empty, Transport = BluetoothTransport.LowEnergy },
            ],
        };

        var endpoint = BluetoothEndpointSelection.SelectGatt(device);

        Assert.NotNull(endpoint);
        Assert.Equal("ble-endpoint", endpoint!.Id);
        Assert.NotEqual(device.Id, endpoint.Id);
    }

    [Fact]
    public void Battery_property_selection_orders_real_endpoint_ids_deterministically()
    {
        var device = new BluetoothDeviceModel
        {
            Id = "container:PHONE",
            Name = "Phone",
            Endpoints =
            [
                new BluetoothEndpointReference { Id = "z-classic", Transport = BluetoothTransport.Classic },
                new BluetoothEndpointReference { Id = "b-dual", Transport = BluetoothTransport.DualMode },
                new BluetoothEndpointReference { Id = "a-low", Transport = BluetoothTransport.LowEnergy },
                new BluetoothEndpointReference { Id = " ", Transport = BluetoothTransport.LowEnergy },
            ],
        };

        var endpointIds = BluetoothEndpointSelection.SelectBatteryPropertyEndpoints(device)
            .Select(endpoint => endpoint.Id)
            .ToArray();

        Assert.Equal(["a-low", "b-dual", "z-classic"], endpointIds);
        Assert.DoesNotContain(device.Id, endpointIds);
    }

}
