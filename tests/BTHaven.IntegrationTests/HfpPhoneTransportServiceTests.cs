using BTHaven.Windows.Diagnostics;
using BTHaven.Windows.Telephony;

namespace BTHaven.IntegrationTests;

public sealed class HfpPhoneTransportServiceTests
{
    [Fact]
    [Trait("Category", "Integration")]
    [Trait("RequiresHardware", "Bluetooth")]
    public async Task Discovers_phone_transport_devices_without_claiming_access()
    {
        Assert.True(
            HfpTransportAdapters.IsSupported(),
            "PhoneLineTransportDevice hardware API support required.");

        await using var service = new HfpPhoneTransportService(NullDiagnosticLogger.Instance);

        var devices = await service.GetAvailableDevicesAsync();
        Assert.True(
            devices.Count > 0,
            "Paired Bluetooth phone-line transport hardware device required.");
        Assert.All(devices, device =>
        {
            Assert.False(string.IsNullOrWhiteSpace(device.Id));
            Assert.False(string.IsNullOrWhiteSpace(device.Name));
            Assert.False(string.IsNullOrWhiteSpace(device.AudioRoutingStatus));
        });
    }
}
