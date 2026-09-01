using BTHaven.Core.Audio;

namespace BTHaven.Core.Tests;

public sealed class RemoteVolumeStatusTests
{
    [Fact]
    public void Not_exposed_volume_cannot_claim_remote_control()
    {
        var status = RemoteVolumeStatus.NotExposed(
            "Windows.AVRCP",
            "Windows does not expose an app-level AVRCP volume controller.");

        Assert.Equal(RemoteVolumeAvailability.NotExposed, status.Availability);
        Assert.False(status.CanControl);
        Assert.Null(status.Level);
        Assert.Equal("Windows.AVRCP", status.Source);
    }

    [Fact]
    public void Remote_volume_level_must_be_between_zero_and_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RemoteVolumeStatus.ValidateLevel(-0.01f));
        Assert.Throws<ArgumentOutOfRangeException>(() => RemoteVolumeStatus.ValidateLevel(1.01f));
        RemoteVolumeStatus.ValidateLevel(0f);
        RemoteVolumeStatus.ValidateLevel(1f);
    }
}
