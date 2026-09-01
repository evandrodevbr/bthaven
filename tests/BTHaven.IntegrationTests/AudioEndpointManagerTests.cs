using BTHaven.Core.Audio;
using BTHaven.Windows.Audio;
using NAudio.CoreAudioApi;

namespace BTHaven.IntegrationTests;

public sealed class AudioEndpointManagerTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void Render_default_endpoint_uses_multimedia_role()
    {
        Assert.Equal(Role.Multimedia, AudioEndpointManager.GetDefaultRole(AudioEndpointDirection.Render));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Capture_default_endpoint_keeps_communications_role()
    {
        Assert.Equal(Role.Communications, AudioEndpointManager.GetDefaultRole(AudioEndpointDirection.Capture));
    }
}
