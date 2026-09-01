using BTHaven.Windows.Battery;

namespace BTHaven.IntegrationTests;

public sealed class GattBatteryProviderTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void Battery_level_parser_rejects_empty_and_out_of_range_values()
    {
        Assert.False(GattBatteryProvider.TryParseBatteryLevel(ReadOnlySpan<byte>.Empty, out _));
        Assert.False(GattBatteryProvider.TryParseBatteryLevel(new byte[] { 101 }, out _));
        Assert.False(GattBatteryProvider.TryParseBatteryLevel(new byte[] { 255 }, out _));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Battery_level_parser_accepts_zero_and_one_hundred()
    {
        Assert.True(GattBatteryProvider.TryParseBatteryLevel(new byte[] { 0 }, out var empty));
        Assert.Equal(0, empty);
        Assert.True(GattBatteryProvider.TryParseBatteryLevel(new byte[] { 100 }, out var full));
        Assert.Equal(100, full);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Subscription_cleanup_detaches_and_disposes_each_resource_once()
    {
        var calls = new List<string>();
        using var cleanup = new GattSubscriptionCleanup(
            () => calls.Add("detach"),
            () => calls.Add("characteristic"),
            () => calls.Add("service"),
            () => calls.Add("device"));

        cleanup.Dispose();
        cleanup.Dispose();

        Assert.Equal(["detach", "characteristic", "service", "device"], calls);
    }
}
