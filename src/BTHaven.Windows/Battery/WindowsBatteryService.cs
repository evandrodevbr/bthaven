using BTHaven.Core.Battery;
using BTHaven.Core.Contracts;
using BTHaven.Core.Devices;
using BTHaven.Windows.Diagnostics;

namespace BTHaven.Windows.Battery;

public sealed class WindowsBatteryService : IBatteryService, IAsyncDisposable
{
    private readonly GattBatteryProvider gattProvider;
    private readonly BatteryService service;
    private readonly IWindowsDiagnosticLogger logger;

    public WindowsBatteryService(IWindowsDiagnosticLogger? logger = null)
    {
        this.logger = logger ?? NullDiagnosticLogger.Instance;
        gattProvider = new GattBatteryProvider(this.logger);
        service = new BatteryService(
            [new WindowsDevicePropertiesBatteryProvider(this.logger), gattProvider],
            (provider, exception) => this.logger.Error("Battery.ProviderFailed", exception, new Dictionary<string, object?>
            {
                ["provider"] = provider,
            }),
            (provider, state) => this.logger.Info("Battery.ProviderResult", new Dictionary<string, object?>
            {
                ["provider"] = provider,
                ["percentage"] = state.Percentage,
                ["isCharging"] = state.IsCharging,
                ["source"] = state.Source,
                ["confidence"] = state.Confidence.ToString(),
            }));
        GattProvider = gattProvider;
    }

    public GattBatteryProvider GattProvider { get; }

    public async Task<BatteryState> GetBatteryAsync(
        BluetoothDeviceModel device,
        CancellationToken cancellationToken = default)
    {
        logger.Info("Battery.Query.Started", new Dictionary<string, object?>
        {
            ["deviceId"] = device.Id,
            ["name"] = device.Name,
            ["transport"] = device.Transport.ToString(),
        });
        var state = await service.GetBatteryAsync(device, cancellationToken).ConfigureAwait(false);
        logger.Info("Battery.Query.Completed", new Dictionary<string, object?>
        {
            ["deviceId"] = device.Id,
            ["percentage"] = state.Percentage,
            ["isCharging"] = state.IsCharging,
            ["source"] = state.Source,
            ["confidence"] = state.Confidence.ToString(),
        });
        return state;
    }

    public ValueTask DisposeAsync()
    {
        logger.Info("Battery.Service.Disposed");
        return gattProvider.DisposeAsync();
    }
}
