using BTHaven.Core.Audio;
using BTHaven.Core.Devices;
using BTHaven.Windows.Audio;

namespace BTHaven.IntegrationTests;

public sealed class RemoteVolumeServiceTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Reports_remote_volume_as_not_exposed_by_windows()
    {
        var service = new WindowsRemoteVolumeService();
        var status = await service.GetStatusAsync(CreatePhone());

        Assert.Equal(RemoteVolumeAvailability.NotExposed, status.Availability);
        Assert.False(status.CanControl);
        Assert.Null(status.Level);
        Assert.NotEmpty(status.Message ?? string.Empty);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Does_not_claim_success_when_setting_remote_volume()
    {
        var service = new WindowsRemoteVolumeService();
        var status = await service.SetVolumeAsync(CreatePhone(), 0.5f);

        Assert.Equal(RemoteVolumeAvailability.NotExposed, status.Availability);
        Assert.False(status.CanControl);
    }

    private static BluetoothDeviceModel CreatePhone() => new()
    {
        Id = "phone",
        Name = "Test phone",
        Category = BluetoothDeviceCategory.Smartphone,
        Transport = BluetoothTransport.DualMode,
    };
}
