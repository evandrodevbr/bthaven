using BTHaven.Probes.Common;
using NAudio.CoreAudioApi;
using NAudio.Wave;

ProbeLog.Header("05-call-audio-routing");
using var enumerator = new MMDeviceEnumerator();

foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
{
    foreach (var state in new[] { DeviceState.Active, DeviceState.Disabled, DeviceState.Unplugged })
    {
        try
        {
            var devices = enumerator.EnumerateAudioEndPoints(flow, state);
            ProbeLog.Event("CoreAudio.EndpointsFound", new { flow = flow.ToString(), state = state.ToString(), count = devices.Count });
            foreach (var device in devices)
            {
                ProbeLog.Event("CoreAudio.EndpointObserved", new
                {
                    flow = flow.ToString(),
                    state = state.ToString(),
                    device.ID,
                    device.FriendlyName,
                    dataFlow = device.DataFlow.ToString(),
                    device.State,
                    mixFormat = SafeMixFormat(device),
                });
            }
        }
        catch (Exception exception)
        {
            ProbeLog.Error("CoreAudio.EnumerateEndpoints", exception, new { flow = flow.ToString(), state = state.ToString() });
        }
    }
}

foreach (var (flow, role) in new[]
{
    (DataFlow.Render, Role.Multimedia),
    (DataFlow.Render, Role.Communications),
    (DataFlow.Capture, Role.Communications),
})
{
    try
    {
        using var device = enumerator.GetDefaultAudioEndpoint(flow, role);
        ProbeLog.Event("CoreAudio.DefaultEndpoint", new
        {
            flow = flow.ToString(),
            role = role.ToString(),
            deviceId = device.ID,
            device.FriendlyName,
            mixFormat = SafeMixFormat(device),
        });
    }
    catch (Exception exception)
    {
        ProbeLog.Error("CoreAudio.GetDefaultEndpoint", exception, new { flow = flow.ToString(), role = role.ToString() });
    }
}

ProbeLog.Event("CoreAudio.RoutingBoundary", new
{
    endpointEnumeration = "supported-public-wasapi-wrapper",
    callAudioTransport = "not-proven",
    note = "This probe inventories WASAPI endpoints and formats; it does not claim HFP call audio until a real bidirectional phone test succeeds.",
});
ProbeLog.Event("Probe.Completed");

static string SafeMixFormat(MMDevice device)
{
    try
    {
        WaveFormat format = device.AudioClient.MixFormat;
        return $"{format.SampleRate}Hz/{format.Channels}ch/{format.Encoding}";
    }
    catch (Exception exception)
    {
        return $"unavailable:0x{exception.HResult:X8}";
    }
}
