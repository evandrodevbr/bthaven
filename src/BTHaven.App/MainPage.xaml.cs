using System.Collections.ObjectModel;
using BTHaven.Core.Audio;
using BTHaven.Core.Devices;
using BTHaven.Windows.Audio;
using BTHaven.Windows.Battery;
using BTHaven.Windows.Bluetooth;
using BTHaven.Windows.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BTHaven_App;

public sealed partial class MainPage : Page
{
    private readonly BluetoothDeviceManager deviceManager;
    private readonly WindowsBatteryService batteryService;
    private readonly AudioEndpointManager endpointManager;
    private readonly A2dpSinkService a2dpService;
    private readonly DiagnosticsExporter diagnosticsExporter;
    private readonly Dictionary<string, BluetoothDeviceModel> devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource lifetime = new();
    private Task? watchTask;
    private string? selectedDeviceId;
    private string? selectedA2dpDeviceId;
    private string? selectedOutputEndpointId;
    private bool loaded;
    private bool disposed;

    public ObservableCollection<DeviceRowViewModel> Rows { get; } = [];

    public MainPage()
    {
        InitializeComponent();

        var logger = TraceDiagnosticLogger.Instance;
        deviceManager = new BluetoothDeviceManager(logger);
        batteryService = new WindowsBatteryService(logger);
        endpointManager = new AudioEndpointManager(logger);
        a2dpService = new A2dpSinkService(logger);
        diagnosticsExporter = new DiagnosticsExporter(deviceManager, endpointManager, logger);

        DeviceList.ItemsSource = Rows;
        FilterComboBox.SelectedIndex = 1;
        Loaded += MainPage_Loaded;
        Unloaded += MainPage_Unloaded;
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (loaded)
        {
            return;
        }

        loaded = true;
        await RefreshAsync();
        watchTask = ConsumeDeviceChangesAsync(lifetime.Token);
    }

    private async void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetime.Cancel();
        try
        {
            if (watchTask is not null)
            {
                await watchTask;
            }
            await deviceManager.DisposeAsync();
            await batteryService.DisposeAsync();
            await a2dpService.DisposeAsync();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshAsync()
    {
        RefreshButton.IsEnabled = false;
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.IsOpen = true;
        StatusInfoBar.Message = "Atualizando dispositivos e endpoints de áudio...";

        try
        {
            var currentDevices = await deviceManager.GetDevicesAsync(BluetoothDeviceFilter.All, lifetime.Token);
            devices.Clear();
            foreach (var device in currentDevices)
            {
                devices[device.Id] = device;
            }
            RefreshRows();

            var renderEndpoints = await endpointManager.GetEndpointsAsync(AudioEndpointDirection.Render, lifetime.Token);
            OutputEndpointComboBox.ItemsSource = renderEndpoints;
            var defaultEndpoint = renderEndpoints.FirstOrDefault(endpoint => endpoint.IsDefault);
            if (defaultEndpoint is not null)
            {
                OutputEndpointComboBox.SelectedItem = defaultEndpoint;
                selectedOutputEndpointId = defaultEndpoint.Id;
            }

            var adapterState = currentDevices.Count == 0
                ? "Nenhum dispositivo emparelhado foi retornado pelo Windows."
                : $"{currentDevices.Count} dispositivo(s) observado(s).";
            StatusInfoBar.Message = $"{adapterState} As alterações serão atualizadas em tempo real.";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            TraceDiagnosticLogger.Instance.Error("App.RefreshFailed", exception);
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Message = $"Não foi possível atualizar os dispositivos: {exception.Message}";
        }
        finally
        {
            RefreshButton.IsEnabled = !disposed;
        }
    }

    private async Task ConsumeDeviceChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var change in deviceManager.WatchAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!DispatcherQueue.TryEnqueue(() => ApplyDeviceChange(change)))
                {
                    TraceDiagnosticLogger.Instance.Info("App.DeviceChange.NotApplied", new Dictionary<string, object?>
                    {
                        ["deviceId"] = change.DeviceId,
                        ["reason"] = "DispatcherQueue was unavailable",
                    });
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            TraceDiagnosticLogger.Instance.Error("App.DeviceWatchFailed", exception);
            if (DispatcherQueue is not null)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    StatusInfoBar.Severity = InfoBarSeverity.Error;
                    StatusInfoBar.Message = $"O watcher de dispositivos foi interrompido: {exception.Message}";
                });
            }
        }
    }

    private void ApplyDeviceChange(BluetoothDeviceChange change)
    {
        if (change.Kind == BluetoothDeviceChangeKind.Removed)
        {
            devices.Remove(change.DeviceId);
            if (string.Equals(selectedDeviceId, change.DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                selectedDeviceId = null;
                selectedA2dpDeviceId = null;
                ClearSelection();
            }
        }
        else if (change.Device is not null)
        {
            devices[change.DeviceId] = change.Device;
        }

        RefreshRows();
    }

    private void RefreshRows()
    {
        var filter = GetSelectedFilter();
        var visible = devices.Values
            .Where(device => BluetoothDeviceFilterMatcher.Matches(device, filter))
            .OrderByDescending(device => device.IsConnected)
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .Select(device => new DeviceRowViewModel(device))
            .ToArray();

        Rows.Clear();
        foreach (var row in visible)
        {
            Rows.Add(row);
        }

        DeviceCountText.Text = visible.Length == 1 ? "1 dispositivo" : $"{visible.Length} dispositivos";
        EmptyState.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (selectedDeviceId is not null)
        {
            DeviceList.SelectedItem = Rows.FirstOrDefault(row =>
                string.Equals(row.Id, selectedDeviceId, StringComparison.OrdinalIgnoreCase));
        }
    }

    private async void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceList.SelectedItem is not DeviceRowViewModel row || !devices.TryGetValue(row.Id, out var device))
        {
            selectedDeviceId = null;
            selectedA2dpDeviceId = null;
            ClearSelection();
            return;
        }

        selectedDeviceId = device.Id;
        RenderSelection(device);
        await RefreshSelectedDeviceCapabilitiesAsync(device);
    }

    private async Task RefreshSelectedDeviceCapabilitiesAsync(BluetoothDeviceModel device)
    {
        try
        {
            var battery = await batteryService.GetBatteryAsync(device, lifetime.Token);
            if (selectedDeviceId is null || !string.Equals(selectedDeviceId, device.Id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var updated = device with { Battery = battery };
            devices[device.Id] = updated;
            RenderSelection(updated);
            RefreshRows();

            var audioTargets = await a2dpService.GetAvailableDevicesAsync(lifetime.Token);
            var matchingTargets = audioTargets
                .Where(target => string.Equals(target.Name, device.Name, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            selectedA2dpDeviceId = matchingTargets.Length == 1 ? matchingTargets[0].Id : null;
            MediaAudioButton.IsEnabled = selectedA2dpDeviceId is not null;
            MediaAudioButton.Content = selectedA2dpDeviceId is null
                ? "Nenhum alvo A2DP confirmado"
                : "Ativar áudio do smartphone";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            TraceDiagnosticLogger.Instance.Error("App.SelectedDeviceCapabilitiesFailed", exception, new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
            });
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async void DiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowDiagnosticsAsync();
    }

    public async Task ShowDiagnosticsAsync()
    {
        DiagnosticsButton.IsEnabled = false;
        try
        {
            var currentDevices = await deviceManager.GetDevicesAsync(BluetoothDeviceFilter.All, lifetime.Token);
            var renderEndpoints = await endpointManager.GetEndpointsAsync(AudioEndpointDirection.Render, lifetime.Token);
            var captureEndpoints = await endpointManager.GetEndpointsAsync(AudioEndpointDirection.Capture, lifetime.Token);
            var summary = $"Dispositivos observados: {currentDevices.Count}\n" +
                          $"Endpoints de saída ativos: {renderEndpoints.Count}\n" +
                          $"Endpoints de entrada ativos: {captureEndpoints.Count}\n" +
                          "HFP: API presente, transporte telefônico não exposto neste sistema\n" +
                          "Privacidade: IDs, nomes e endereços são redigidos no ZIP; áudio não é coletado.";
            var dialog = new ContentDialog
            {
                Title = "Diagnósticos",
                Content = new ScrollViewer
                {
                    MaxHeight = 360,
                    Content = new TextBlock
                    {
                        Text = summary,
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
                PrimaryButtonText = "Exportar ZIP redigido",
                CloseButtonText = "Fechar",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var path = await diagnosticsExporter.ExportAsync(lifetime.Token);
                StatusInfoBar.Severity = InfoBarSeverity.Success;
                StatusInfoBar.Message = $"Diagnósticos exportados localmente para: {path}";
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            TraceDiagnosticLogger.Instance.Error("App.DiagnosticsExportFailed", exception);
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Message = $"Falha ao gerar diagnósticos: {exception.Message}";
        }
        finally
        {
            DiagnosticsButton.IsEnabled = !disposed;
        }
    }

    private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loaded)
        {
            RefreshRows();
        }
    }

    private async void MediaAudioButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedA2dpDeviceId is null)
        {
            return;
        }

        MediaAudioButton.IsEnabled = false;
        try
        {
            var connected = await a2dpService.ConnectAsync(selectedA2dpDeviceId, lifetime.Token);
            StatusInfoBar.Severity = connected ? InfoBarSeverity.Success : InfoBarSeverity.Error;
            StatusInfoBar.Message = connected
                ? "A2DP ativo. O Windows está usando o endpoint padrão de reprodução."
                : "O Windows não abriu a conexão A2DP; consulte os logs estruturados.";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            TraceDiagnosticLogger.Instance.Error("App.MediaAudioActivationFailed", exception);
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Message = $"Falha ao ativar o áudio de mídia: {exception.Message}";
        }
        finally
        {
            MediaAudioButton.IsEnabled = selectedA2dpDeviceId is not null && !disposed;
        }
    }

    private void OutputEndpointComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OutputEndpointComboBox.SelectedItem is not AudioEndpointModel endpoint)
        {
            return;
        }

        selectedOutputEndpointId = endpoint.Id;
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Message = $"Endpoint selecionado: {endpoint.Name}. A2DP usa o endpoint padrão do Windows até existir um roteador dedicado validado.";
    }

    private BluetoothDeviceFilter GetSelectedFilter()
    {
        return (FilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
        {
            nameof(BluetoothDeviceFilter.All) => BluetoothDeviceFilter.All,
            nameof(BluetoothDeviceFilter.Paired) => BluetoothDeviceFilter.Paired,
            nameof(BluetoothDeviceFilter.Ble) => BluetoothDeviceFilter.Ble,
            nameof(BluetoothDeviceFilter.Classic) => BluetoothDeviceFilter.Classic,
            nameof(BluetoothDeviceFilter.Audio) => BluetoothDeviceFilter.Audio,
            nameof(BluetoothDeviceFilter.Smartphones) => BluetoothDeviceFilter.Smartphones,
            nameof(BluetoothDeviceFilter.Peripherals) => BluetoothDeviceFilter.Peripherals,
            _ => BluetoothDeviceFilter.Connected,
        };
    }

    private void RenderSelection(BluetoothDeviceModel device)
    {
        SelectedDeviceName.Text = device.Name;
        SelectedDeviceSubtitle.Text = $"{device.Category} · observado em {device.LastUpdated.ToLocalTime():HH:mm:ss}";
        ConnectionStateText.Text = device.IsConnected
            ? "Conectado"
            : device.IsPaired && device.IsPresent
                ? "Emparelhado / presente"
                : device.IsPaired
                    ? "Emparelhado / desconectado"
                    : "Desconectado";
        TransportText.Text = device.Transport.ToString();
        AddressText.Text = device.Address ?? "Não exposto pelo Windows";
        ContainerIdText.Text = device.ContainerId ?? "Não exposto pelo Windows";
        BatteryText.Text = device.Battery?.Percentage is int percentage
            ? device.Battery.IsCharging == true ? $"{percentage}% · carregando" : $"{percentage}%"
            : device.Battery?.IsCharging == true ? "Carregando · porcentagem indisponível" : "Indisponível";
        BatterySourceText.Text = device.Battery?.Source ?? "Aguardando consulta";
        CapabilitiesText.Text = FormatCapabilities(device);
    }

    private void ClearSelection()
    {
        SelectedDeviceName.Text = "Selecione um dispositivo";
        SelectedDeviceSubtitle.Text = "A lista usa observações do Windows, não polling agressivo.";
        ConnectionStateText.Text = "—";
        TransportText.Text = "—";
        AddressText.Text = "—";
        ContainerIdText.Text = "—";
        BatteryText.Text = "—";
        BatterySourceText.Text = "—";
        CapabilitiesText.Text = "—";
        selectedA2dpDeviceId = null;
        MediaAudioButton.IsEnabled = false;
        MediaAudioButton.Content = "Nenhum alvo A2DP confirmado";
    }

    private static string FormatCapabilities(BluetoothDeviceModel device)
    {
        var values = new List<string>();
        if (device.Capabilities.HasFlag(BluetoothCapabilities.Classic)) values.Add("Bluetooth Classic");
        if (device.Capabilities.HasFlag(BluetoothCapabilities.Ble)) values.Add("Bluetooth LE");
        if (device.Capabilities.HasFlag(BluetoothCapabilities.MediaAudio)) values.Add("Media audio");
        if (device.Capabilities.HasFlag(BluetoothCapabilities.PhoneCalls)) values.Add("Phone calls");
        if (device.Capabilities.HasFlag(BluetoothCapabilities.Battery)) values.Add("Battery property");
        return values.Count == 0 ? "Nenhuma capacidade de perfil confirmada" : string.Join(" · ", values);
    }
}
