using BTHaven.Windows.Battery;
using BTHaven.Windows.Bluetooth;

namespace BTHaven.IntegrationTests;

public sealed class WindowsBatteryPropertyDecoderTests
{
    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(101, null)]
    public void Battery_life_uses_the_official_percentage_and_unknown_values(
        int raw,
        int? expectedPercentage)
    {
        var result = WindowsBatteryPropertyDecoder.Decode((byte)raw, null, null);

        Assert.Equal(expectedPercentage, result.Percentage);
        Assert.Null(result.IsCharging);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(0, 0, false)]
    [InlineData(100, 100, false)]
    [InlineData(101, 1, true)]
    [InlineData(150, 50, true)]
    [InlineData(200, 100, true)]
    [InlineData(201, null, null)]
    public void Battery_plus_charging_decodes_the_official_ranges(
        int raw,
        int? expectedPercentage,
        bool? expectedCharging)
    {
        var result = WindowsBatteryPropertyDecoder.Decode(null, (byte)raw, null);

        Assert.Equal(expectedPercentage, result.Percentage);
        Assert.Equal(expectedCharging, result.IsCharging);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, null)]
    public void Charging_state_decodes_the_official_enumeration(
        int raw,
        bool? expectedCharging)
    {
        var result = WindowsBatteryPropertyDecoder.Decode(null, null, (byte)raw);

        Assert.Null(result.Percentage);
        Assert.Equal(expectedCharging, result.IsCharging);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Dedicated_properties_take_precedence_and_unknown_values_fall_back()
    {
        var dedicated = WindowsBatteryPropertyDecoder.Decode(
            batteryLife: 80,
            batteryPlusCharging: 150,
            chargingState: 0);
        var fallback = WindowsBatteryPropertyDecoder.Decode(
            batteryLife: 101,
            batteryPlusCharging: 150,
            chargingState: 2);

        Assert.Equal(80, dedicated.Percentage);
        Assert.False(dedicated.IsCharging);
        Assert.Equal(50, fallback.Percentage);
        Assert.True(fallback.IsCharging);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Zero_and_false_remain_trustworthy_data()
    {
        var result = WindowsBatteryPropertyDecoder.Decode(
            batteryLife: 0,
            batteryPlusCharging: null,
            chargingState: 0);

        Assert.True(result.HasData);
        Assert.Equal(0, result.Percentage);
        Assert.False(result.IsCharging);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Unknown_or_out_of_contract_values_produce_no_data()
    {
        var documentedUnknown = WindowsBatteryPropertyDecoder.Decode(101, 201, 2);
        var invalid = WindowsBatteryPropertyDecoder.Decode(202, 202, 3);

        Assert.False(documentedUnknown.HasData);
        Assert.False(invalid.HasData);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Device_property_reader_accepts_only_the_official_projected_byte_type()
    {
        var properties = new Dictionary<string, object>
        {
            ["value"] = (byte)0,
        };

        Assert.Equal((byte)0, WindowsDevicePropertyReader.Byte(properties, "value"));

        properties["value"] = 1;
        Assert.Null(WindowsDevicePropertyReader.Byte(properties, "value"));

        properties["value"] = "1";
        Assert.Null(WindowsDevicePropertyReader.Byte(properties, "value"));
    }
}
