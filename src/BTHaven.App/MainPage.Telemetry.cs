using BTHaven.Core.Battery;
using BTHaven.Core.Devices;

namespace BTHaven_App;

public sealed partial class MainPage
{
    private readonly BatteryTelemetryCoordinator batteryTelemetry;
    private bool isRefreshingDeviceInventory;

    private void BatteryTelemetry_Changed(object? sender, BatteryTelemetryChangedEventArgs e)
    {
        var entry = e.Entry;
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                if (disposed
                    || !devices.ContainsKey(entry.DeviceId)
                    || !batteryTelemetry.TryGet(entry.DeviceId, out var latest))
                {
                    return;
                }

                var epoch = selectionEpoch;
                RefreshRows();
                if (IsCurrentSelection(latest.DeviceId, epoch))
                {
                    RenderSelectedTelemetry(latest);
                }
            }))
        {
            logger.Warning("App.BatteryTelemetry.ChangeNotApplied", new Dictionary<string, object?>
            {
                ["deviceId"] = entry.DeviceId,
                ["status"] = entry.Status.ToString(),
            });
        }
    }

    private void RefreshVisibleBatteryTelemetry(bool onlyMissing = false)
    {
        var visiblePresent = GetVisibleDevices()
            .Where(device => device.IsPresent)
            .Where(device => !onlyMissing
                || !batteryTelemetry.TryGet(device.Id, out var entry)
                || entry.Status == BatteryTelemetryStatus.NotRequested)
            .ToArray();
        QueueBatteryTelemetryRefresh(visiblePresent, selectedDeviceId);
    }

    private void RefreshBatteryTelemetryForSelection(BluetoothDeviceModel device, long epoch)
    {
        if (isRefreshingDeviceInventory
            || !device.IsPresent
            || !IsCurrentSelection(device.Id, epoch))
        {
            return;
        }
        if (batteryTelemetry.TryGet(device.Id, out var entry)
            && entry.Current is not null)
        {
            return;
        }

        QueueBatteryTelemetryRefresh([device], device.Id);
    }

    private void RefreshBatteryTelemetryForDevice(BluetoothDeviceModel device, bool replaceInFlight)
    {
        if (!device.IsPresent)
        {
            return;
        }
        if (!replaceInFlight
            && batteryTelemetry.TryGet(device.Id, out var entry)
            && entry.Status == BatteryTelemetryStatus.Loading)
        {
            return;
        }

        QueueBatteryTelemetryRefresh([device], SameId(selectedDeviceId, device.Id) ? device.Id : null);
    }

    private void QueueBatteryTelemetryRefresh(
        IReadOnlyList<BluetoothDeviceModel> devicesToRefresh,
        string? priorityDeviceId)
    {
        if (disposed || devicesToRefresh.Count == 0)
        {
            return;
        }

        _ = ObserveBatteryTelemetryRefreshAsync(devicesToRefresh, priorityDeviceId);
    }

    private async Task ObserveBatteryTelemetryRefreshAsync(
        IReadOnlyList<BluetoothDeviceModel> devicesToRefresh,
        string? priorityDeviceId)
    {
        try
        {
            await batteryTelemetry.RefreshAsync(devicesToRefresh, priorityDeviceId, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.Error("App.BatteryTelemetry.RefreshFailed", exception, new Dictionary<string, object?>
            {
                ["deviceCount"] = devicesToRefresh.Count,
                ["priorityDeviceId"] = priorityDeviceId,
            });
        }
    }

    private void RenderSelectedTelemetry(BatteryTelemetryEntry? entry)
    {
        BatteryTelemetryStatusText.Text = entry?.Status switch
        {
            BatteryTelemetryStatus.Loading => "Carregando",
            BatteryTelemetryStatus.Available => "Atual",
            BatteryTelemetryStatus.Unavailable => "Indisponível",
            BatteryTelemetryStatus.Failed => "Falha",
            _ => "Não consultada",
        };

        if (entry?.Current is { } current)
        {
            BatteryText.Text = current.Percentage is int percentage
                ? current.IsCharging == true ? $"{percentage}% · carregando" : $"{percentage}%"
                : current.IsCharging switch
                {
                    true => "Carregando · porcentagem indisponível",
                    false => "Porcentagem indisponível · não carregando",
                    _ => "Indisponível",
                };
            BatterySourceText.Text = current.Source;
            BatteryConfidenceText.Text = current.Confidence.ToString();
            BatteryObservedText.Text = $"Leitura atual: {current.LastUpdated.ToLocalTime():HH:mm:ss}";
            return;
        }

        if (entry?.LastAvailable is { } lastAvailable)
        {
            BatteryText.Text = lastAvailable.Percentage is int percentage
                ? $"Última leitura: {percentage}%"
                : lastAvailable.IsCharging switch
                {
                    true => "Última leitura: carregando · porcentagem indisponível",
                    false => "Última leitura: porcentagem indisponível · não carregando",
                    _ => "Última leitura: indisponível",
                };
            BatterySourceText.Text = $"Última fonte: {lastAvailable.Source}";
            BatteryConfidenceText.Text = $"Última confiança: {lastAvailable.Confidence}";
            BatteryObservedText.Text = $"Última leitura: {lastAvailable.LastUpdated.ToLocalTime():HH:mm:ss}";
            return;
        }

        BatteryText.Text = entry?.Status == BatteryTelemetryStatus.Loading ? "Carregando…" : "Indisponível";
        BatterySourceText.Text = "Não disponível";
        BatteryConfidenceText.Text = "Não disponível";
        BatteryObservedText.Text = entry is { AttemptedAt: var attemptedAt } && attemptedAt != default
            ? $"Tentativa: {attemptedAt.ToLocalTime():HH:mm:ss}"
            : "Nenhuma leitura";
    }

    private static string FormatRssi(int value) =>
        value < 0 ? $"−{-(long)value} dBm" : $"{value} dBm";

    private static bool HasSameEndpointSet(BluetoothDeviceModel previous, BluetoothDeviceModel current) =>
        previous.Endpoints.Count == current.Endpoints.Count
        && previous.Endpoints.All(previousEndpoint => current.Endpoints.Any(currentEndpoint =>
            SameId(previousEndpoint.Id, currentEndpoint.Id)
            && previousEndpoint.Transport == currentEndpoint.Transport));
}
