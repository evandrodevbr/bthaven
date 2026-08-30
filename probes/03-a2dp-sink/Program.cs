using BTHaven.Probes.Common;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

ProbeLog.Header("03-a2dp-sink");
var arguments = ProbeArguments.Parse(args);
var selector = AudioPlaybackConnection.GetDeviceSelector();
try
{
    var devices = await DeviceInformation.FindAllAsync(selector);
    ProbeLog.Event("A2DP.Sink.DevicesFound", new { count = devices.Count, selector });
    foreach (var device in devices)
    {
        ProbeLog.Event("A2DP.Sink.DeviceObserved", new { device.Id, device.Name, kind = device.Kind.ToString() });
    }

    var deviceId = arguments.Get("--device-id");
    if (string.IsNullOrWhiteSpace(deviceId))
    {
        ProbeLog.Event("A2DP.Sink.NotExercised", new { reason = "Pass --device-id using an ID returned by this selector to exercise StartAsync/OpenAsync" });
        return;
    }

    var connection = AudioPlaybackConnection.TryCreateFromId(deviceId);
    if (connection is null)
    {
        ProbeLog.Event("A2DP.Sink.CreateUnavailable", new { deviceId, reason = "TryCreateFromId returned null; the ID does not expose an audio playback connection" });
        return;
    }

    using (connection)
    {
        connection.StateChanged += (_, _) => ProbeLog.Event("A2DP.Connection.StateChanged", new { deviceId, state = connection.State.ToString() });
        ProbeLog.Event("A2DP.Connection.Starting", new { deviceId, state = connection.State.ToString() });
        await connection.StartAsync();
        ProbeLog.Event("A2DP.Connection.Started", new { deviceId, state = connection.State.ToString() });

        var openResult = await connection.OpenAsync();
        ProbeLog.Event("A2DP.Connection.OpenResult", new { deviceId, status = openResult.Status.ToString(), state = connection.State.ToString(), holdSeconds = Math.Clamp(arguments.GetInt("--hold-seconds", 10), 1, 120) });
        if (openResult.Status == AudioPlaybackConnectionOpenResultStatus.Success)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(arguments.GetInt("--hold-seconds", 10), 1, 120)));
        }
    }

    ProbeLog.Event("A2DP.Connection.Disposed", new { deviceId });
}
catch (Exception exception)
{
    ProbeLog.Error("A2DP.Sink", exception, new { selector });
}
