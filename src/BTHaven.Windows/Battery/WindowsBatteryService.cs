using BTHaven.Core.Battery;
using BTHaven.Core.Contracts;
using BTHaven.Core.Devices;
using BTHaven.Windows.Diagnostics;

namespace BTHaven.Windows.Battery;

public sealed class WindowsBatteryService : IBatteryService, IAsyncDisposable
{
    private readonly GattBatteryProvider gattProvider;
    private readonly BatteryService service;

    public WindowsBatteryService(IWindowsDiagnosticLogger? logger = null)
    {
        gattProvider = new GattBatteryProvider();
        service = new BatteryService(
            [new WindowsDevicePropertiesBatteryProvider(), gattProvider],
            (provider, exception) => logger?.Error("Battery.ProviderFailed", exception, new Dictionary<string, object?>
            {
                ["provider"] = provider,
            }));
        GattProvider = gattProvider;
    }

    public GattBatteryProvider GattProvider { get; }

    public Task<BatteryState> GetBatteryAsync(
        BluetoothDeviceModel device,
        CancellationToken cancellationToken = default)
    {
        return service.GetBatteryAsync(device, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return gattProvider.DisposeAsync();
    }
}
