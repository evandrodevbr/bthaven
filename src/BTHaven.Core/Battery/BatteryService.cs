using BTHaven.Core.Contracts;
using BTHaven.Core.Devices;

namespace BTHaven.Core.Battery;

public sealed class BatteryService : IBatteryService
{
    private readonly IReadOnlyList<IBatteryProvider> providers;
    private readonly Action<string, Exception>? providerError;
    private readonly Action<string, BatteryState>? providerResult;

    public BatteryService(
        IEnumerable<IBatteryProvider> providers,
        Action<string, Exception>? providerError = null,
        Action<string, BatteryState>? providerResult = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        this.providers = providers.ToArray();
        this.providerError = providerError;
        this.providerResult = providerResult;
    }

    public async Task<BatteryState> GetBatteryAsync(
        BluetoothDeviceModel device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var state = await provider.GetBatteryAsync(device, cancellationToken).ConfigureAwait(false);
                providerResult?.Invoke(provider.Name, state);
                if (state.Percentage.HasValue || state.IsCharging.HasValue)
                {
                    return state;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                providerError?.Invoke(provider.Name, exception);
            }
        }

        return BatteryState.Unavailable();
    }
}
