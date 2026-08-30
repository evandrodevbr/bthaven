using BTHaven.Core.Audio;
using BTHaven.Core.Contracts;
using BTHaven.Windows.Diagnostics;
using NAudio.CoreAudioApi;

namespace BTHaven.Windows.Audio;

public sealed class AudioEndpointManager : IAudioEndpointService
{
    private readonly IWindowsDiagnosticLogger logger;

    public AudioEndpointManager(IWindowsDiagnosticLogger? logger = null)
    {
        this.logger = logger ?? NullDiagnosticLogger.Instance;
    }

    public Task<IReadOnlyList<AudioEndpointModel>> GetEndpointsAsync(
        AudioEndpointDirection direction,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dataFlow = direction == AudioEndpointDirection.Render ? DataFlow.Render : DataFlow.Capture;
        using var enumerator = new MMDeviceEnumerator();
        string? defaultId = null;
        try
        {
            using var defaultDevice = enumerator.GetDefaultAudioEndpoint(dataFlow, Role.Communications);
            defaultId = defaultDevice.ID;
        }
        catch (Exception exception)
        {
            logger.Error("Audio.DefaultEndpoint.Unavailable", exception, new Dictionary<string, object?>
            {
                ["direction"] = direction.ToString(),
            });
        }

        var endpoints = new List<AudioEndpointModel>();
        var nativeDevices = enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active);
        foreach (var nativeDevice in nativeDevices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (nativeDevice)
            {
                endpoints.Add(new AudioEndpointModel
                {
                    Id = nativeDevice.ID,
                    Name = nativeDevice.FriendlyName,
                    Direction = direction,
                    IsDefault = string.Equals(nativeDevice.ID, defaultId, StringComparison.OrdinalIgnoreCase),
                    IsActive = nativeDevice.State == DeviceState.Active,
                    Format = TryGetMixFormat(nativeDevice, logger),
                });
            }
        }

        logger.Info("Audio.Endpoints.Enumerated", new Dictionary<string, object?>
        {
            ["direction"] = direction.ToString(),
            ["count"] = endpoints.Count,
        });
        return Task.FromResult<IReadOnlyList<AudioEndpointModel>>(endpoints);
    }

    private static string? TryGetMixFormat(MMDevice device, IWindowsDiagnosticLogger logger)
    {
        try
        {
            var format = device.AudioClient.MixFormat;
            return $"{format.SampleRate}Hz/{format.Channels}ch/{format.Encoding}";
        }
        catch (Exception exception)
        {
            logger.Error("Audio.Endpoint.MixFormatUnavailable", exception, new Dictionary<string, object?>
            {
                ["deviceId"] = device.ID,
                ["friendlyName"] = device.FriendlyName,
            });
            return null;
        }
    }
}
