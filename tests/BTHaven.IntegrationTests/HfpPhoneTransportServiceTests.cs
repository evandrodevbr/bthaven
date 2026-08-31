using BTHaven.Windows.Diagnostics;
using BTHaven.Windows.Telephony;

namespace BTHaven.IntegrationTests;

public sealed class HfpPhoneTransportServiceTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Discovers_phone_transport_devices_without_claiming_access()
    {
        await using var service = new HfpPhoneTransportService(NullDiagnosticLogger.Instance);

        var devices = await service.GetAvailableDevicesAsync();

        Assert.NotNull(devices);
        Assert.All(devices, device =>
        {
            Assert.False(string.IsNullOrWhiteSpace(device.Id));
            Assert.False(string.IsNullOrWhiteSpace(device.Name));
            Assert.False(string.IsNullOrWhiteSpace(device.AudioRoutingStatus));
        });
    }
}
