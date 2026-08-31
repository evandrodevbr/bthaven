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
        logger.Info("Audio.Endpoints.EnumerationStarted", new Dictionary<string, object?>
        {
            ["direction"] = direction.ToString(),
            ["dataFlow"] = dataFlow.ToString(),
        });

        using var enumerator = new MMDeviceEnumerator();
        string? defaultId = null;
        try
        {
            using var defaultDevice = enumerator.GetDefaultAudioEndpoint(dataFlow, Role.Communications);
            defaultId = defaultDevice.ID;
            logger.Info("Audio.DefaultEndpoint.Observed", new Dictionary<string, object?>
            {
                ["direction"] = direction.ToString(),
                ["endpointId"] = defaultDevice.ID,
                ["name"] = defaultDevice.FriendlyName,
            });
        }
        catch (Exception exception)
        {
            logger.Error("Audio.DefaultEndpoint.Unavailable", exception, new Dictionary<string, object?>
            {
                ["direction"] = direction.ToString(),
            });
        }

        var endpoints = new List<AudioEndpointModel>();
        try
        {
            var nativeDevices = enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active);
            foreach (var nativeDevice in nativeDevices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (nativeDevice)
                {
                    var endpoint = new AudioEndpointModel
                    {
                        Id = nativeDevice.ID,
                        Name = nativeDevice.FriendlyName,
                        Direction = direction,
                        IsDefault = string.Equals(nativeDevice.ID, defaultId, StringComparison.OrdinalIgnoreCase),
                        IsActive = nativeDevice.State == DeviceState.Active,
                        Format = TryGetMixFormat(nativeDevice),
                    };
                    endpoints.Add(endpoint);
                    logger.Debug("Audio.Endpoint.Observed", new Dictionary<string, object?>
                    {
                        ["direction"] = endpoint.Direction.ToString(),
                        ["endpointId"] = endpoint.Id,
                        ["name"] = endpoint.Name,
                        ["isDefault"] = endpoint.IsDefault,
                        ["isActive"] = endpoint.IsActive,
                        ["format"] = endpoint.Format,
                    });
                }
            }
        }
        catch (Exception exception)
        {
            logger.Error("Audio.Endpoints.EnumerationFailed", exception, new Dictionary<string, object?>
            {
                ["direction"] = direction.ToString(),
            });
            throw;
        }

        logger.Info("Audio.Endpoints.Enumerated", new Dictionary<string, object?>
        {
            ["direction"] = direction.ToString(),
            ["count"] = endpoints.Count,
            ["defaultFound"] = defaultId is not null,
        });
        return Task.FromResult<IReadOnlyList<AudioEndpointModel>>(endpoints);
    }

    private string? TryGetMixFormat(MMDevice device)
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
