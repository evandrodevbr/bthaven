using BTHaven.Core.Audio;
using BTHaven.Windows.Audio;

namespace BTHaven.IntegrationTests;

public sealed class WindowsAudioServicesSmokeTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Endpoint_manager_returns_unique_render_and_capture_endpoints()
    {
        var manager = new AudioEndpointManager();

        var render = await manager.GetEndpointsAsync(AudioEndpointDirection.Render);
        var capture = await manager.GetEndpointsAsync(AudioEndpointDirection.Capture);

        Assert.Equal(render.Count, render.Select(endpoint => endpoint.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(capture.Count, capture.Select(endpoint => endpoint.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A2dp_service_starts_disabled_without_claiming_a_connection()
    {
        await using var service = new A2dpSinkService();

        Assert.False(service.IsEnabled);
        Assert.Null(service.DeviceId);
        Assert.Equal(MediaAudioSinkState.Disabled, service.GetConnectionState());

        var devices = await service.GetAvailableDevicesAsync();
        Assert.NotNull(devices);
    }
}
