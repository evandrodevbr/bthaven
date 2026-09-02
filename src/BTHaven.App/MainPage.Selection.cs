using BTHaven.Core.Devices;
using Microsoft.UI.Xaml;
using Windows.Storage;

namespace BTHaven_App;

public sealed partial class MainPage
{
    private const string PreferredDeviceIdSettingKey = "PreferredBluetoothDeviceId";
    private bool isReconcilingSelection;
    private long selectionEpoch;
    private string? preferredDeviceId;

    private static string? LoadPreferredDeviceId() =>
        ApplicationData.Current.LocalSettings.Values[PreferredDeviceIdSettingKey] as string;

    private static void SavePreferredDeviceId(string deviceId) =>
        ApplicationData.Current.LocalSettings.Values[PreferredDeviceIdSettingKey] = deviceId;

    private void RefreshRows()
    {
        var visible = GetVisibleDevices();
        ReconcileRowsAndSelection(visible);

        logger.Debug("App.Rows.Refreshed", new Dictionary<string, object?>
        {
            ["filter"] = GetSelectedFilter().ToString(),
            ["visibleCount"] = visible.Length,
            ["knownCount"] = devices.Count,
        });
    }

    private BluetoothDeviceModel[] GetVisibleDevices()
    {
        var filter = GetSelectedFilter();
        return devices.Values
            .Where(device => BluetoothDeviceFilterMatcher.Matches(device, filter))
            .OrderByDescending(device => device.IsConnected)
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void ReconcileRowsAndSelection(IReadOnlyList<BluetoothDeviceModel> visible)
    {
        var nextId = BluetoothDeviceSelectionPolicy.Resolve(visible, selectedDeviceId, preferredDeviceId);
        isReconcilingSelection = true;
        try
        {
            SynchronizeRows(visible);
            DeviceList.SelectedItem = Rows.FirstOrDefault(row => SameId(row.Id, nextId));
        }
        finally
        {
            isReconcilingSelection = false;
        }

        if (!SameId(selectedDeviceId, nextId))
        {
            SetSelectedDevice(nextId);
        }

        AssertSelectionInvariant();
    }

    private void SynchronizeRows(IReadOnlyList<BluetoothDeviceModel> visible)
    {
        suppressMediaToggleEvents = true;
        try
        {
            Rows.Clear();
            foreach (var device in visible)
            {
                var telemetry = batteryTelemetry.TryGet(device.Id, out var entry) ? entry : null;
                Rows.Add(new DeviceRowViewModel(
                    device,
                    telemetry,
                    SameId(activeMediaDeviceId, device.Id)));
            }
        }
        finally
        {
            suppressMediaToggleEvents = false;
        }

        DeviceCountText.Text = visible.Count == 1 ? "1 dispositivo" : $"{visible.Count} dispositivos";
        EmptyState.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetSelectedDevice(string? deviceId)
    {
        if (SameId(selectedDeviceId, deviceId))
        {
            return;
        }

        selectedDeviceId = deviceId;
        selectionEpoch++;
        if (deviceId is null || !devices.TryGetValue(deviceId, out var device))
        {
            ClearSelection();
            return;
        }

        selectedInspection = null;
        selectedA2dpDeviceId = null;
        selectedHfpTransportId = null;
        MediaAudioButton.IsEnabled = false;
        MediaAudioButton.Content = "Consultando alvo A2DP...";
        A2dpTargetText.Text = "Alvo A2DP: aguardando consulta";
        HfpEnableButton.IsEnabled = false;
        HfpEnableButton.Content = "Consultando transporte HFP...";
        HfpTransportText.Text = "Transporte HFP: aguardando consulta";
        RenderRemoteVolumeStatus(null);
        logger.Info("App.Device.Selected", new Dictionary<string, object?>
        {
            ["deviceId"] = device.Id,
            ["name"] = device.Name,
            ["transport"] = device.Transport.ToString(),
            ["connected"] = device.IsConnected,
            ["paired"] = device.IsPaired,
            ["present"] = device.IsPresent,
        });
        RenderSelection(device);
        RefreshBatteryTelemetryForSelection(device, selectionEpoch);
        _ = RefreshSelectedDeviceCapabilitiesAsync(device.Id, selectionEpoch);
    }

    private bool IsCurrentSelection(string deviceId, long epoch) =>
        epoch == selectionEpoch
        && SameId(selectedDeviceId, deviceId)
        && devices.ContainsKey(deviceId);

    private static bool HasSameConnectionProvenance(
        BluetoothDeviceModel previous,
        BluetoothDeviceModel current)
    {
        if (previous.IsConnected != current.IsConnected
            || previous.Endpoints.Count != current.Endpoints.Count)
        {
            return false;
        }
        var previousPreferred = BluetoothEndpointSelection.SelectPreferredConnection(previous);
        var currentPreferred = BluetoothEndpointSelection.SelectPreferredConnection(current);
        if (!SameId(previousPreferred?.Id, currentPreferred?.Id)
            || previousPreferred?.Transport != currentPreferred?.Transport)
        {
            return false;
        }

        return previous.Endpoints.All(previousEndpoint => current.Endpoints.Any(currentEndpoint =>
            SameId(previousEndpoint.Id, currentEndpoint.Id)
            && previousEndpoint.Transport == currentEndpoint.Transport
            && previousEndpoint.IsConnected == currentEndpoint.IsConnected
            && previousEndpoint.IsPresent == currentEndpoint.IsPresent));
    }

    private static bool SameId(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private void AssertSelectionInvariant()
    {
        System.Diagnostics.Debug.Assert(
            selectedDeviceId is null
                ? DeviceList.SelectedItem is null
                : DeviceList.SelectedItem is DeviceRowViewModel row
                    && SameId(row.Id, selectedDeviceId));
    }
}
