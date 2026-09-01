using BTHaven.Core.Audio;
using BTHaven.Core.Contracts;
using BTHaven.Core.Devices;
using BTHaven.Windows.Diagnostics;

namespace BTHaven.Windows.Audio;

public sealed class WindowsRemoteVolumeService : IRemoteVolumeService
{
    private const string Source = "Windows.AVRCP";
    private const string UnsupportedMessage =
        "O Windows não expõe à aplicação um controlador AVRCP para alterar diretamente o volume interno do smartphone.";
    private readonly IWindowsDiagnosticLogger logger;

    public WindowsRemoteVolumeService(IWindowsDiagnosticLogger? logger = null)
    {
        this.logger = logger ?? NullDiagnosticLogger.Instance;
    }

    public Task<RemoteVolumeStatus> GetStatusAsync(
        BluetoothDeviceModel device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();
        logger.Info("RemoteVolume.Status.Requested", new Dictionary<string, object?>
        {
            ["deviceId"] = device.Id,
            ["name"] = device.Name,
            ["transport"] = device.Transport.ToString(),
        });

        var status = RemoteVolumeStatus.NotExposed(Source, UnsupportedMessage);
        logger.Info("RemoteVolume.Status.NotExposed", new Dictionary<string, object?>
        {
            ["deviceId"] = device.Id,
            ["source"] = Source,
            ["message"] = UnsupportedMessage,
        });
        return Task.FromResult(status);
    }

    public Task<RemoteVolumeStatus> SetVolumeAsync(
        BluetoothDeviceModel device,
        float level,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        RemoteVolumeStatus.ValidateLevel(level);
        cancellationToken.ThrowIfCancellationRequested();
        logger.Info("RemoteVolume.Set.Requested", new Dictionary<string, object?>
        {
            ["deviceId"] = device.Id,
            ["level"] = level,
            ["source"] = Source,
        });

        var status = RemoteVolumeStatus.NotExposed(Source, UnsupportedMessage);
        logger.Warning("RemoteVolume.Set.NotExposed", new Dictionary<string, object?>
        {
            ["deviceId"] = device.Id,
            ["level"] = level,
            ["source"] = Source,
            ["message"] = UnsupportedMessage,
        });
        return Task.FromResult(status);
    }
}
