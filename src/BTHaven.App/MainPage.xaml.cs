using System.Collections.ObjectModel;
using BTHaven.Core.Audio;
using BTHaven.Core.Calls;
using BTHaven.Core.Battery;
using BTHaven.Core.Devices;
using BTHaven.Windows.Audio;
using BTHaven.Windows.Battery;
using BTHaven.Windows.Bluetooth;
using BTHaven.Windows.Diagnostics;
using BTHaven.Windows.Telephony;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BTHaven_App;

public sealed partial class MainPage : Page
{
    private readonly BluetoothDeviceManager deviceManager;
    private readonly WindowsBatteryService batteryService;
    private readonly AudioEndpointManager endpointManager;
    private readonly A2dpSinkService a2dpService;
    private readonly A2dpAutoReconnectService autoReconnectService;
    private readonly WindowsRemoteVolumeService remoteVolumeService;
    private readonly BluetoothDeviceInspector deviceInspector;
    private readonly HfpPhoneTransportService hfpService;
    private readonly DiagnosticsExporter diagnosticsExporter;
    private readonly TraceDiagnosticLogger logger;
    private readonly Dictionary<string, BluetoothDeviceModel> devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly OperationLifetime operations = new();
    private readonly MediaConnectionCoordinator media;
    private ContentDialog? activeDialog;
    private string? selectedDeviceId;
    private string? selectedA2dpDeviceId;
    private string? selectedHfpTransportId;
    private string? selectedOutputEndpointId;
    private BluetoothDeviceInspectionSnapshot? selectedInspection;
    private RemoteVolumeStatus? remoteVolumeStatus;
    private bool suppressMediaToggleEvents;
    private bool loaded;
    private bool ready;
    private bool isCompactLayout;
    private bool disposed;

    public ObservableCollection<DeviceRowViewModel> Rows { get; } = [];

    public MainPage()
    {
        InitializeComponent();
        DetailSelectorBar.SelectedItem = SummarySelectorItem;
        preferredDeviceId = LoadPreferredDeviceId();

        logger = TraceDiagnosticLogger.Instance;
        deviceManager = new BluetoothDeviceManager(logger);
        batteryService = new WindowsBatteryService(logger);
        batteryTelemetry = new BatteryTelemetryCoordinator(batteryService);
        batteryTelemetry.Changed += BatteryTelemetry_Changed;
        endpointManager = new AudioEndpointManager(logger);
        a2dpService = new A2dpSinkService(logger);
        a2dpService.StateChanged += A2dpService_StateChanged;
        autoReconnectService = new A2dpAutoReconnectService(a2dpService, logger);
        media = new MediaConnectionCoordinator(a2dpService, autoReconnectService);
        hfpService = new HfpPhoneTransportService(logger);
        remoteVolumeService = new WindowsRemoteVolumeService(logger);
        deviceInspector = new BluetoothDeviceInspector(
            logger,
            batteryService,
            a2dpService,
            hfpService,
            remoteVolumeService);
        diagnosticsExporter = new DiagnosticsExporter(deviceManager, endpointManager, logger, deviceInspector);
        logger.Info("App.MainPage.Created", new Dictionary<string, object?>
        {
            ["defaultFilter"] = BluetoothDeviceFilter.Connected.ToString(),
        });

        DeviceList.ItemsSource = Rows;
        FilterComboBox.SelectedIndex = 1;
        ready = true;
        Loaded += MainPage_Loaded;
        Unloaded += MainPage_Unloaded;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e) =>
        _ = RunOperationAsync(LoadAsync);

    private async Task LoadAsync()
    {
        logger.Info("App.MainPage.Loaded", new Dictionary<string, object?>
        {
            ["alreadyLoaded"] = loaded,
        });
        if (loaded)
        {
            return;
        }

        loaded = true;
        await RefreshAsync();
        _ = RunOperationAsync(() => ConsumeDeviceChangesAsync(lifetime.Token));
        logger.Info("App.DeviceWatch.Started");
    }

    private async void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Unloaded e evento da arvore visual (Hide()/minimizar nao o disparam); o
        // teardown definitivo mora em ShutdownAsync, chamado por ExitApplication antes
        // de Close(). Este e apenas o fallback de fechamento real.
        if (MainWindow.IsShuttingDown)
        {
            try { await ShutdownAsync(); }
            catch (Exception exception) { logger.Error("App.Shutdown.Failed", exception); }
        }
    }

    public Task ShutdownAsync() => operations.ShutdownAsync(
        () =>
        {
            disposed = true;
            selectionEpoch++;
            a2dpService.StateChanged -= A2dpService_StateChanged;
            batteryTelemetry.Changed -= BatteryTelemetry_Changed;
            // Dialogs otherwise keep tracked operations alive until user input.
            try { activeDialog?.Hide(); }
            finally { lifetime.Cancel(); }
        },
        () => autoReconnectService.DisposeAsync(),
        () => a2dpService.DisposeAsync(),
        () => hfpService.DisposeAsync(),
        () => deviceManager.DisposeAsync(),
        () => batteryService.DisposeAsync(),
        () =>
        {
            lifetime.Dispose();
            refreshGate.Dispose();
            media.Dispose();
            logger.Info("App.MainPage.Disposed");
            return ValueTask.CompletedTask;
        });

    private Task RunOperationAsync(Func<Task> operation) =>
        operations.RunAsync(async () =>
        {
            try { await operation(); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception exception) { logger.Error("App.Operation.Failed", exception); }
        });

    private Task RefreshAsync() => RunOperationAsync(RefreshCoreAsync);

    private async Task RefreshCoreAsync()
    {
        await refreshGate.WaitAsync(lifetime.Token);
        logger.Info("App.Refresh.Started", new Dictionary<string, object?>
        {
            ["filter"] = GetSelectedFilter().ToString(),
        });
        RefreshButton.IsEnabled = false;
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.IsOpen = true;
        StatusInfoBar.Message = "Atualizando dispositivos e endpoints de áudio...";

        try
        {
            var currentDevices = await deviceManager.GetDevicesAsync(BluetoothDeviceFilter.All, lifetime.Token);
            var previousSelectedDevice = selectedDeviceId is not null
                && devices.TryGetValue(selectedDeviceId, out var previous)
                    ? previous
                    : null;
            devices.Clear();
            foreach (var device in currentDevices)
            {
                devices[device.Id] = device;
            }
            media.Prune(devices);
            var selectedConnectionChanged = previousSelectedDevice is not null
                && devices.TryGetValue(previousSelectedDevice.Id, out var refreshedSelectedDevice)
                && !HasSameConnectionProvenance(previousSelectedDevice, refreshedSelectedDevice);
            if (selectedConnectionChanged)
            {
                selectionEpoch++;
                selectedInspection = null;
            }
            isRefreshingDeviceInventory = true;
            try
            {
                RefreshRows();
            }
            finally
            {
                isRefreshingDeviceInventory = false;
            }
            batteryTelemetry.Prune(devices.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase));
            RefreshVisibleBatteryTelemetry();
            if (previousSelectedDevice is not null
                && selectedDeviceId is not null
                && SameId(previousSelectedDevice.Id, selectedDeviceId)
                && devices.TryGetValue(selectedDeviceId, out var selectedDevice))
            {
                RenderSelection(selectedDevice);
                if (selectedConnectionChanged)
                {
                    _ = RefreshSelectedDeviceCapabilitiesAsync(selectedDevice.Id, selectionEpoch);
                }
            }

            var renderEndpoints = await endpointManager.GetEndpointsAsync(AudioEndpointDirection.Render, lifetime.Token);
            OutputEndpointComboBox.ItemsSource = renderEndpoints;
            var defaultEndpoint = renderEndpoints.FirstOrDefault(endpoint => endpoint.IsDefault);
            if (defaultEndpoint is not null)
            {
                OutputEndpointComboBox.SelectedItem = defaultEndpoint;
                selectedOutputEndpointId = defaultEndpoint.Id;
            }

            var connectedCount = currentDevices.Count(device => device.IsConnected);
            var pairedCount = currentDevices.Count(device => device.IsPaired);
            var adapterState = currentDevices.Count == 0
                ? "Nenhum dispositivo emparelhado foi retornado pelo Windows."
                : $"{currentDevices.Count} dispositivo(s) observado(s).";
            StatusInfoBar.Message = $"{adapterState} Conectados: {connectedCount}; emparelhados: {pairedCount}. As alterações serão atualizadas em tempo real.";
            logger.Info("App.Refresh.Completed", new Dictionary<string, object?>
            {
                ["total"] = currentDevices.Count,
                ["connected"] = connectedCount,
                ["paired"] = pairedCount,
                ["renderEndpoints"] = renderEndpoints.Count,
            });
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.Error("App.Refresh.Failed", exception);
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Message = $"Não foi possível atualizar os dispositivos: {exception.Message}";
        }
        finally
        {
            RefreshButton.IsEnabled = !disposed;
            refreshGate.Release();
        }
    }

    private async Task ConsumeDeviceChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var change in deviceManager.WatchAsync(cancellationToken).ConfigureAwait(false))
            {
                logger.Debug("App.DeviceWatch.ChangeReceived", new Dictionary<string, object?>
                {
                    ["kind"] = change.Kind.ToString(),
                    ["deviceId"] = change.DeviceId,
                    ["hasDevice"] = change.Device is not null,
                });
                if (!DispatcherQueue.TryEnqueue(() => ApplyDeviceChange(change)))
                {
                    logger.Warning("App.DeviceChange.NotApplied", new Dictionary<string, object?>
                    {
                        ["deviceId"] = change.DeviceId,
                        ["reason"] = "DispatcherQueue was unavailable",
                    });
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.Info("App.DeviceWatch.Cancelled");
        }
        catch (Exception exception)
        {
            logger.Error("App.DeviceWatch.Failed", exception);
            if (DispatcherQueue is not null)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (disposed) return;
                    StatusInfoBar.Severity = InfoBarSeverity.Error;
                    StatusInfoBar.Message = $"O watcher de dispositivos foi interrompido: {exception.Message}";
                });
            }
        }
    }

    private void ApplyDeviceChange(BluetoothDeviceChange change)
    {
        if (disposed) return;
        logger.Info("App.DeviceChange.Applying", new Dictionary<string, object?>
        {
            ["kind"] = change.Kind.ToString(),
            ["deviceId"] = change.DeviceId,
            ["name"] = change.Device?.Name,
            ["connected"] = change.Device?.IsConnected,
            ["paired"] = change.Device?.IsPaired,
            ["present"] = change.Device?.IsPresent,
        });

        var selectedDeviceUpdated = false;
        var selectedConnectionChanged = false;
        var refreshChangedDeviceTelemetry = false;
        var replaceInFlightTelemetry = false;
        if (change.Kind == BluetoothDeviceChangeKind.Removed)
        {
            var selectedTargetMapsRemovedDevice = selectedA2dpDeviceId is not null
                && media.MapsTo(selectedA2dpDeviceId, change.DeviceId);
            var selectedTargetIsRemovedEndpoint = selectedA2dpDeviceId is not null
                && !string.IsNullOrWhiteSpace(change.EndpointId)
                && SameId(selectedA2dpDeviceId, change.EndpointId);

            devices.Remove(change.DeviceId);
            media.RemoveDevice(change.DeviceId);
            if (selectedTargetMapsRemovedDevice || selectedTargetIsRemovedEndpoint)
            {
                selectedA2dpDeviceId = null;
                MediaAudioButton.IsEnabled = false;
                A2dpTargetText.Text = "Alvo A2DP removido; aguardando nova consulta";
            }
        }
        else if (change.Device is not null)
        {
            devices.TryGetValue(change.DeviceId, out var previousDevice);
            devices[change.DeviceId] = change.Device;
            var connectionProvenanceChanged = previousDevice is not null
                && !HasSameConnectionProvenance(previousDevice, change.Device);
            replaceInFlightTelemetry = connectionProvenanceChanged;
            refreshChangedDeviceTelemetry = previousDevice is null || connectionProvenanceChanged;
            selectedDeviceUpdated = SameId(selectedDeviceId, change.DeviceId);
            selectedConnectionChanged = selectedDeviceUpdated && connectionProvenanceChanged;
            if (connectionProvenanceChanged)
            {
                batteryTelemetry.Invalidate(change.Device.Id);
            }
            if (selectedConnectionChanged)
            {
                selectionEpoch++;
                selectedInspection = null;
            }

            if (selectedDeviceUpdated)
            {
                var selectedTargetIsUpdatedEndpoint = selectedA2dpDeviceId is not null
                    && !string.IsNullOrWhiteSpace(change.EndpointId)
                    && SameId(selectedA2dpDeviceId, change.EndpointId);
                var selectedTargetMappingsChangedDevice = selectedA2dpDeviceId is not null
                    && media.MapsToDifferentDevice(selectedA2dpDeviceId, change.DeviceId);
                if (selectedTargetIsUpdatedEndpoint || selectedTargetMappingsChangedDevice)
                {
                    selectedA2dpDeviceId = null;
                    MediaAudioButton.IsEnabled = false;
                    A2dpTargetText.Text = "Alvo A2DP: aguardando nova consulta";
                }
                if (change.Device.Category != BluetoothDeviceCategory.Smartphone)
                {
                    selectedHfpTransportId = null;
                    HfpEnableButton.IsEnabled = false;
                    HfpEnableButton.Content = "HFP disponível apenas para smartphones";
                    HfpTransportText.Text = "Transporte HFP: não aplicável a esta categoria";
                }
            }
        }

        RefreshRows();
        if (selectedDeviceUpdated
            && selectedDeviceId is not null
            && SameId(selectedDeviceId, change.DeviceId)
            && devices.TryGetValue(selectedDeviceId, out var selectedDevice))
        {
            RenderSelection(selectedDevice);
            if (selectedConnectionChanged)
            {
                _ = RefreshSelectedDeviceCapabilitiesAsync(selectedDevice.Id, selectionEpoch);
            }
        }
        if (change.Kind == BluetoothDeviceChangeKind.Removed)
        {
            batteryTelemetry.Prune(devices.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase));
        }
        else if (refreshChangedDeviceTelemetry && change.Device is not null)
        {
            RefreshBatteryTelemetryForDevice(change.Device, replaceInFlightTelemetry);
        }
    }

    private void A2dpService_StateChanged(MediaAudioSinkState state)
    {
        var targetId = a2dpService.DeviceId;
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                if (!disposed && media.ApplyState(state, targetId, devices))
                {
                    RefreshRows();
                }
            }))
        {
            logger.Warning("App.A2DP.StateChange.NotApplied");
        }
    }


    private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (disposed || isReconcilingSelection)
        {
            return;
        }

        if (DeviceList.SelectedItem is not DeviceRowViewModel row
            || !devices.TryGetValue(row.Id, out var device))
        {
            RefreshRows();
            return;
        }

        preferredDeviceId = device.Id;
        SavePreferredDeviceId(device.Id);
        SetSelectedDevice(device.Id);
        ShowCompactDeviceDetails(moveFocus: true);
    }


    private Task RefreshSelectedDeviceCapabilitiesAsync(string deviceId, long epoch) =>
        RunOperationAsync(() => RefreshSelectedDeviceCapabilitiesCoreAsync(deviceId, epoch));

    private async Task RefreshSelectedDeviceCapabilitiesCoreAsync(string deviceId, long epoch)
    {
        try
        {
            if (!IsCurrentSelection(deviceId, epoch)
                || !devices.TryGetValue(deviceId, out var device))
            {
                return;
            }

            logger.Info("App.DeviceCapabilities.Started", new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
                ["name"] = device.Name,
            });

            var audioTargets = await a2dpService.GetAvailableDevicesAsync(lifetime.Token);
            if (!IsCurrentSelection(deviceId, epoch)
                || !devices.TryGetValue(deviceId, out device))
            {
                return;
            }

            var matchingTargets = BluetoothDeviceCorrelation.FindMatches(device, audioTargets);
            if (matchingTargets.Length == 1)
            {
                media.ObserveTarget(matchingTargets[0].Id, device.Id);
            }
            selectedA2dpDeviceId = matchingTargets.Length == 1 ? matchingTargets[0].Id : null;
            A2dpTargetText.Text = matchingTargets.Length switch
            {
                1 => $"Alvo A2DP confirmado: {matchingTargets[0].Name}",
                > 1 => $"Alvos A2DP ambíguos: {matchingTargets.Length}; conexão não iniciada.",
                _ => $"Alvo A2DP não exposto para {device.Name}.",
            };
            MediaAudioButton.IsEnabled = selectedA2dpDeviceId is not null;
            MediaAudioButton.Content = selectedA2dpDeviceId is null
                ? "Nenhum alvo A2DP confirmado"
                : "Ativar áudio do smartphone";

            IReadOnlyList<PhoneLineTransportModel> hfpTargets = [];
            var matchingHfpTargets = Array.Empty<PhoneLineTransportModel>();
            if (device.Category == BluetoothDeviceCategory.Smartphone)
            {
                hfpTargets = await hfpService.GetAvailableDevicesAsync(lifetime.Token);
                if (!IsCurrentSelection(deviceId, epoch)
                    || !devices.TryGetValue(deviceId, out device))
                {
                    return;
                }
            }

            if (device.Category == BluetoothDeviceCategory.Smartphone)
            {
                matchingHfpTargets = BluetoothDeviceCorrelation.FindMatches(device, hfpTargets);
                selectedHfpTransportId = matchingHfpTargets.Length == 1 ? matchingHfpTargets[0].Id : null;
                HfpEnableButton.IsEnabled = true;
                HfpEnableButton.Content = selectedHfpTransportId is null
                    ? "Reconsultar transporte HFP"
                    : "Solicitar acesso HFP";
                HfpTransportText.Text = matchingHfpTargets.Length switch
                {
                    1 => $"Transporte HFP confirmado: {matchingHfpTargets[0].AudioRoutingStatus}",
                    > 1 => $"Transportes HFP ambíguos: {matchingHfpTargets.Length}; ação não iniciada.",
                    _ => "Nenhum PhoneLineTransportDevice correspondeu a este dispositivo.",
                };
                HfpStatusInfoBar.Severity = selectedHfpTransportId is null
                    ? InfoBarSeverity.Warning
                    : InfoBarSeverity.Informational;
                HfpStatusInfoBar.Title = selectedHfpTransportId is null
                    ? "HFP não exposto para este smartphone"
                    : "HFP disponível para teste";
                HfpStatusInfoBar.Message = selectedHfpTransportId is null
                    ? "Clique em Reconsultar transporte HFP para executar a descoberta real."
                    : "Clique em Solicitar acesso HFP para pedir a permissão documentada ao Windows.";
            }
            else
            {
                selectedHfpTransportId = null;
                HfpEnableButton.IsEnabled = false;
                HfpEnableButton.Content = "HFP disponível apenas para smartphones";
                HfpTransportText.Text = "Transporte HFP: não aplicável a esta categoria";
                HfpStatusInfoBar.Severity = InfoBarSeverity.Informational;
                HfpStatusInfoBar.Title = "HFP não aplicável";
                HfpStatusInfoBar.Message = "A ativação HFP só é suportada para dispositivos classificados como smartphone.";
            }

            if (!devices.TryGetValue(deviceId, out device))
            {
                return;
            }
            var remoteVolume = await remoteVolumeService.GetStatusAsync(device, lifetime.Token);
            if (!IsCurrentSelection(deviceId, epoch))
            {
                return;
            }
            RenderRemoteVolumeStatus(remoteVolume);
            logger.Info("App.DeviceCapabilities.Completed", new Dictionary<string, object?>
            {
                ["deviceId"] = deviceId,
                ["a2dpCandidates"] = audioTargets.Count,
                ["a2dpMatches"] = matchingTargets.Length,
                ["hfpCandidates"] = hfpTargets.Count,
                ["hfpMatches"] = matchingHfpTargets.Length,
            });
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            devices.TryGetValue(deviceId, out var device);
            logger.Error("App.DeviceCapabilities.Failed", exception, new Dictionary<string, object?>
            {
                ["deviceId"] = deviceId,
                ["name"] = device?.Name,
            });
            if (!IsCurrentSelection(deviceId, epoch))
            {
                return;
            }

            A2dpTargetText.Text = "Falha ao consultar o alvo A2DP; consulte Logs.";
            HfpTransportText.Text = "Falha ao consultar o transporte HFP; consulte Logs.";
            RemoteVolumeInfoBar.Severity = InfoBarSeverity.Error;
            RemoteVolumeInfoBar.Title = "Falha ao consultar volume remoto";
            RemoteVolumeInfoBar.Message = "O estado de volume não foi confirmado; consulte Logs.";
            RemoteVolumeStatusText.Text = "Volume remoto: falha na consulta";
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        _ = RefreshAsync();

    private void DiagnosticsButton_Click(object sender, RoutedEventArgs e) =>
        _ = ShowDiagnosticsAsync();

    private void LogsButton_Click(object sender, RoutedEventArgs e) =>
        _ = ShowLogsAsync();

    public Task ShowDiagnosticsAsync() => RunOperationAsync(ShowDiagnosticsCoreAsync);

    private async Task ShowDiagnosticsCoreAsync()
    {
        DiagnosticsButton.IsEnabled = false;
        logger.Info("App.Diagnostics.Opened");
        try
        {
            var currentDevices = await deviceManager.GetDevicesAsync(BluetoothDeviceFilter.All, lifetime.Token);
            var renderEndpoints = await endpointManager.GetEndpointsAsync(AudioEndpointDirection.Render, lifetime.Token);
            var captureEndpoints = await endpointManager.GetEndpointsAsync(AudioEndpointDirection.Capture, lifetime.Token);
            var summary = $"Dispositivos observados: {currentDevices.Count}\n" +
                          $"Endpoints de saída ativos: {renderEndpoints.Count}\n" +
                          $"Endpoints de entrada ativos: {captureEndpoints.Count}\n" +
                          $"Logs: {logger.LogDirectory}\n" +
                          "HFP: o botão executa RequestAccessAsync e registra o resultado real\n" +
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
            if (await ShowDialogAsync(dialog) == ContentDialogResult.Primary)
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
            logger.Error("App.Diagnostics.Failed", exception);
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Message = $"Falha ao gerar diagnósticos: {exception.Message}";
        }
        finally
        {
            DiagnosticsButton.IsEnabled = !disposed;
            logger.Info("App.Diagnostics.Closed");
        }
    }

    public Task ShowLogsAsync() => RunOperationAsync(ShowLogsCoreAsync);

    private async Task ShowLogsCoreAsync()
    {
        var lines = logger.ReadRecent(maxLines: 1000, redactSensitive: false);
        logger.Info("App.LogViewer.Opened", new Dictionary<string, object?>
        {
            ["maxLines"] = 1000,
            ["returnedLines"] = lines.Count,
            ["redacted"] = false,
        });
        var logBox = new TextBox
        {
            Text = lines.Count == 0 ? "Nenhum evento gravado ainda." : string.Join(Environment.NewLine, lines),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinWidth = 720,
            MinHeight = 420,
            FontFamily = new FontFamily("Cascadia Mono"),
        };
        var dialog = new ContentDialog
        {
            Title = $"Logs locais ({lines.Count} eventos)",
            Content = new ScrollViewer
            {
                MaxWidth = 900,
                MaxHeight = 540,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = logBox,
            },
            CloseButtonText = "Fechar",
            XamlRoot = XamlRoot,
        };
        await ShowDialogAsync(dialog);
        logger.Info("App.LogViewer.Closed", new Dictionary<string, object?>
        {
            ["displayedLines"] = lines.Count,
        });
    }

    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        lifetime.Token.ThrowIfCancellationRequested();
        if (activeDialog is not null) return ContentDialogResult.None;
        activeDialog = dialog;
        try { return await dialog.ShowAsync(); }
        finally { activeDialog = null; }
    }

    private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var filter = GetSelectedFilter();
        logger.Info("App.Filter.Changed", new Dictionary<string, object?>
        {
            ["filter"] = filter.ToString(),
        });
        if (loaded && !disposed)
        {
            RefreshRows();
            RefreshVisibleBatteryTelemetry(onlyMissing: true);
        }
    }
    private void MediaAudioButton_Click(object sender, RoutedEventArgs e) =>
        _ = RunOperationAsync(MediaAudioAsync);

    private async Task MediaAudioAsync()
    {
        logger.Info("App.MediaAudioButton.Clicked", new Dictionary<string, object?>
        {
            ["deviceId"] = selectedDeviceId,
            ["a2dpDeviceId"] = selectedA2dpDeviceId,
            ["outputEndpointId"] = selectedOutputEndpointId,
        });
        var requestedA2dpDeviceId = selectedA2dpDeviceId;
        if (requestedA2dpDeviceId is null)
        {
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            StatusInfoBar.Message = "Nenhum alvo A2DP oficial foi encontrado para este dispositivo.";
            return;
        }

        var requestedDeviceId = selectedDeviceId;
        var epoch = selectionEpoch;
        if (requestedDeviceId is null || !IsCurrentSelection(requestedDeviceId, epoch))
        {
            return;
        }

        MediaAudioButton.IsEnabled = false;
        try
        {
            var connected = await media.ConnectAsync(
                requestedDeviceId, requestedA2dpDeviceId,
                AutoReconnectCheckBox.IsChecked == true, lifetime.Token,
                () => IsCurrentSelection(requestedDeviceId, epoch));
            if (!IsCurrentSelection(requestedDeviceId, epoch))
            {
                return;
            }
            if (connected)
            {
                RefreshRows();
            }
            var endpoint = OutputEndpointComboBox.SelectedItem as AudioEndpointModel;
            logger.Info("App.MediaAudioButton.Completed", new Dictionary<string, object?>
            {
                ["deviceId"] = requestedDeviceId,
                ["a2dpDeviceId"] = requestedA2dpDeviceId,
                ["connected"] = connected,
                ["state"] = a2dpService.State.ToString(),
                ["outputEndpointId"] = endpoint?.Id,
                ["outputIsDefault"] = endpoint?.IsDefault,
            });
            StatusInfoBar.Severity = connected ? InfoBarSeverity.Success : InfoBarSeverity.Error;
            StatusInfoBar.Message = connected
                ? endpoint?.IsDefault == true
                    ? "A2DP ativo. Reproduza mídia no telefone; o Windows deve entregá-la ao headset padrão."
                    : "A2DP ativo, mas o endpoint escolhido não é o padrão do Windows; altere o padrão para ouvir no headset."
                : "O Windows não abriu a conexão A2DP; consulte Logs para o HRESULT.";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.Error("App.MediaAudioActivation.Failed", exception, new Dictionary<string, object?>
            {
                ["deviceId"] = requestedDeviceId,
                ["a2dpDeviceId"] = requestedA2dpDeviceId,
            });
            if (!IsCurrentSelection(requestedDeviceId, epoch))
            {
                return;
            }
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Message = "Falha ao ativar o áudio; o HRESULT e a stack trace foram gravados nos Logs.";
        }
        finally
        {
            if (IsCurrentSelection(requestedDeviceId, epoch))
            {
                MediaAudioButton.IsEnabled = selectedA2dpDeviceId is not null && !disposed;
            }
        }
    }

    private void AutoReconnectCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (ready) _ = RunOperationAsync(UpdateAutoReconnectAsync);
    }

    private async Task UpdateAutoReconnectAsync()
    {
        if (!ready)
        {
            return;
        }

        var enabled = AutoReconnectCheckBox.IsChecked == true;
        logger.Info("App.A2DP.AutoReconnectSetting.Changed", new Dictionary<string, object?>
        {
            ["enabled"] = enabled,
            ["deviceId"] = selectedDeviceId,
            ["a2dpDeviceId"] = selectedA2dpDeviceId,
        });
        await media.SetReconnectAsync(
            enabled, selectedDeviceId, selectedA2dpDeviceId, lifetime.Token);
    }

    private void HfpEnableButton_Click(object sender, RoutedEventArgs e) =>
        _ = RunOperationAsync(EnableHfpAsync);

    private async Task EnableHfpAsync()
    {
        var requestedDeviceId = selectedDeviceId;
        var requestedTransportId = selectedHfpTransportId;
        var epoch = selectionEpoch;
        logger.Info("App.HFP.EnableButton.Clicked", new Dictionary<string, object?>
        {
            ["deviceId"] = requestedDeviceId,
            ["transportDeviceId"] = requestedTransportId,
        });
        HfpEnableButton.IsEnabled = false;
        try
        {
            if (requestedDeviceId is null || !devices.TryGetValue(requestedDeviceId, out var device))
            {
                HfpStatusInfoBar.Severity = InfoBarSeverity.Warning;
                HfpStatusInfoBar.Title = "Selecione um smartphone";
                HfpStatusInfoBar.Message = "Nenhum dispositivo foi selecionado para o teste HFP.";
                return;
            }
            if (device.Category != BluetoothDeviceCategory.Smartphone)
            {
                HfpStatusInfoBar.Severity = InfoBarSeverity.Informational;
                HfpStatusInfoBar.Title = "HFP não aplicável";
                HfpStatusInfoBar.Message = "A ativação HFP só é suportada para dispositivos classificados como smartphone.";
                logger.Info("App.HFP.Enable.NotApplicable", new Dictionary<string, object?>
                {
                    ["deviceId"] = device.Id,
                    ["category"] = device.Category.ToString(),
                });
                return;
            }

            if (requestedTransportId is null)
            {
                var targets = await hfpService.GetAvailableDevicesAsync(lifetime.Token);
                if (!IsCurrentSelection(device.Id, epoch)
                    || !devices.TryGetValue(device.Id, out device))
                {
                    return;
                }
                if (device.Category != BluetoothDeviceCategory.Smartphone)
                {
                    return;
                }

                var matches = BluetoothDeviceCorrelation.FindMatches(device, targets);
                requestedTransportId = matches.Length == 1 ? matches[0].Id : null;
                selectedHfpTransportId = requestedTransportId;
            }

            if (requestedTransportId is null)
            {
                HfpStatusInfoBar.Severity = InfoBarSeverity.Warning;
                HfpStatusInfoBar.Title = "HFP não exposto para este dispositivo";
                HfpStatusInfoBar.Message = "O seletor oficial não retornou um transporte compatível; consulte Logs.";
                logger.Info("App.HFP.Enable.NotAvailable", new Dictionary<string, object?>
                {
                    ["deviceId"] = device.Id,
                });
                return;
            }

            HfpStatusInfoBar.Severity = InfoBarSeverity.Informational;
            HfpStatusInfoBar.Title = "Solicitando acesso HFP";
            HfpStatusInfoBar.Message = "Solicitando a permissão documentada e tentando registrar o transporte...";
            var result = await hfpService.ActivateAsync(requestedTransportId, lifetime.Token);
            if (!IsCurrentSelection(device.Id, epoch))
            {
                return;
            }
            HfpStatusInfoBar.Severity = result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
            HfpStatusInfoBar.Title = result.Succeeded ? "HFP ativo" : $"HFP: {result.Status}";
            HfpStatusInfoBar.Message = result.Succeeded
                ? result.Message ?? "Transporte HFP conectado."
                : $"{(result.Message is null ? string.Empty : result.Message + " ")}AccessStatus={result.AccessStatus ?? "unknown"}{(result.HResult is null ? string.Empty : $" HRESULT={result.HResult}")}. Consulte Logs para HRESULT e stack trace.";
            HfpTransportText.Text = $"Transporte HFP: {result.Status}; conectado={result.IsConnected}; registrado={result.IsRegistered}";
            logger.Info("App.HFP.EnableButton.Completed", new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
                ["transportDeviceId"] = requestedTransportId,
                ["status"] = result.Status,
                ["succeeded"] = result.Succeeded,
                ["accessStatus"] = result.AccessStatus,
                ["hResult"] = result.HResult,
                ["isRegistered"] = result.IsRegistered,
                ["isConnected"] = result.IsConnected,
            });
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.Error("App.HFP.EnableButton.Failed", exception, new Dictionary<string, object?>
            {
                ["deviceId"] = requestedDeviceId,
                ["transportDeviceId"] = requestedTransportId,
            });
            if (requestedDeviceId is null || !IsCurrentSelection(requestedDeviceId, epoch))
            {
                return;
            }
            HfpStatusInfoBar.Severity = InfoBarSeverity.Error;
            HfpStatusInfoBar.Title = "Falha ao ativar HFP";
            HfpStatusInfoBar.Message = "A ativação falhou; o HRESULT e a stack trace foram gravados nos Logs.";
        }
        finally
        {
            HfpEnableButton.IsEnabled = !disposed
                && selectedDeviceId is not null
                && devices.TryGetValue(selectedDeviceId, out var currentDevice)
                && currentDevice.Category == BluetoothDeviceCategory.Smartphone;
        }
    }

    private void LayoutStates_CurrentStateChanged(
        object sender,
        VisualStateChangedEventArgs args)
    {
        isCompactLayout = ReferenceEquals(args.NewState, CompactState);
        // Aceleradores de controles ocultos continuam disparando (comportamento
        // documentado); sem isso, Escape fecharia um ContentDialog e navegaria de
        // volta ao mesmo tempo em compact.
        CompactBackAccelerator.IsEnabled = isCompactLayout;
        if (!isCompactLayout)
        {
            return;
        }

        if (selectedDeviceId is null)
        {
            ShowCompactDeviceList(moveFocus: false);
        }
        else
        {
            ShowCompactDeviceDetails(moveFocus: false);
        }
    }

    private void CompactBackButton_Click(object sender, RoutedEventArgs e) =>
        ShowCompactDeviceList(moveFocus: true);

    private void ShowCompactDeviceList(bool moveFocus)
    {
        if (!isCompactLayout)
        {
            return;
        }

        DevicePane.Visibility = Visibility.Visible;
        SelectedDevicePane.Visibility = Visibility.Collapsed;
        if (moveFocus)
        {
            DeviceList.Focus(FocusState.Programmatic);
        }
    }

    private void ShowCompactDeviceDetails(bool moveFocus)
    {
        if (!isCompactLayout || selectedDeviceId is null)
        {
            return;
        }

        DevicePane.Visibility = Visibility.Collapsed;
        SelectedDevicePane.Visibility = Visibility.Visible;
        if (moveFocus)
        {
            SelectedDeviceHeading.Focus(FocusState.Programmatic);
        }
    }

    private void DetailSelectorBar_SelectionChanged(
        SelectorBar sender,
        SelectorBarSelectionChangedEventArgs args)
    {
        var showAudio = ReferenceEquals(sender.SelectedItem, AudioSelectorItem);
        var showDiagnostics = ReferenceEquals(sender.SelectedItem, DiagnosticsSelectorItem);
        SummaryView.Visibility = showAudio || showDiagnostics ? Visibility.Collapsed : Visibility.Visible;
        AudioView.Visibility = showAudio ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsView.Visibility = showDiagnostics ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OutputEndpointComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OutputEndpointComboBox.SelectedItem is not AudioEndpointModel endpoint)
        {
            return;
        }

        selectedOutputEndpointId = endpoint.Id;
        logger.Info("App.AudioEndpoint.Selected", new Dictionary<string, object?>
        {
            ["endpointId"] = endpoint.Id,
            ["name"] = endpoint.Name,
            ["direction"] = endpoint.Direction.ToString(),
            ["isDefault"] = endpoint.IsDefault,
            ["isActive"] = endpoint.IsActive,
            ["format"] = endpoint.Format,
        });
        StatusInfoBar.Severity = endpoint.IsDefault ? InfoBarSeverity.Informational : InfoBarSeverity.Warning;
        StatusInfoBar.Message = endpoint.IsDefault
            ? $"Endpoint padrão observado: {endpoint.Name}."
            : $"Endpoint selecionado: {endpoint.Name}, mas ele não é o padrão do Windows; A2DP público usa o endpoint padrão.";
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
        SelectionEmptyState.Visibility = Visibility.Collapsed;
        DeviceDetailContent.Visibility = Visibility.Visible;
        SelectedDeviceStatus.Visibility = Visibility.Visible;
        DetailSelectorBar.Visibility = Visibility.Visible;
        SelectedDeviceName.Text = device.Name;
        SelectedDeviceSubtitle.Text = $"{device.Category} · observado em {device.LastUpdated.ToLocalTime():HH:mm:ss}";
        var connectedEndpointCount = device.Endpoints.Count(endpoint => endpoint.IsConnected == true);
        ConnectionStateText.Text = device.IsConnected
            ? connectedEndpointCount > 1
                ? $"Conectado · {connectedEndpointCount} endpoints ativos"
                : "Conectado"
            : device.IsPaired && device.IsPresent
                ? "Emparelhado / presente"
                : device.IsPaired
                    ? "Emparelhado / desconectado"
                    : "Desconectado";
        TransportText.Text = device.Transport.ToString();
        ConnectionTransportText.Text =
            BluetoothEndpointSelection.SelectPreferredConnection(device)?.Transport.ToString()
            ?? "Não exposto pelo Windows";
        RssiText.Text = device.Rssi is int rssi ? FormatRssi(rssi) : "Não exposto pelo Windows";
        DeviceObservedText.Text = device.LastUpdated.ToLocalTime().ToString("HH:mm:ss");
        AddressText.Text = device.Address ?? "Não exposto pelo Windows";
        ContainerIdText.Text = device.ContainerId ?? "Não exposto pelo Windows";
        RenderSelectedTelemetry(
            batteryTelemetry.TryGet(device.Id, out var telemetry) ? telemetry : null);
        CapabilitiesText.Text = FormatCapabilities(device);
        InspectButton.IsEnabled = !disposed;
        if (selectedInspection?.DeviceId == device.Id)
        {
            RenderInspection(selectedInspection);
        }
    }

    private void ClearSelection()
    {
        SelectionEmptyState.Visibility = Visibility.Visible;
        DeviceDetailContent.Visibility = Visibility.Collapsed;
        SelectedDeviceStatus.Visibility = Visibility.Collapsed;
        DetailSelectorBar.Visibility = Visibility.Collapsed;
        SelectedDeviceName.Text = "Selecione um dispositivo";
        SelectedDeviceSubtitle.Text = "Escolha um dispositivo na lista para consultar seus detalhes e controles de áudio.";
        ConnectionStateText.Text = "—";
        TransportText.Text = "—";
        ConnectionTransportText.Text = "—";
        RssiText.Text = "—";
        DeviceObservedText.Text = "—";
        AddressText.Text = "—";
        ContainerIdText.Text = "—";
        BatteryText.Text = "—";
        BatteryTelemetryStatusText.Text = "—";
        BatterySourceText.Text = "—";
        BatteryConfidenceText.Text = "—";
        BatteryObservedText.Text = "—";
        CapabilitiesText.Text = "—";
        selectedInspection = null;
        remoteVolumeStatus = null;
        selectedA2dpDeviceId = null;
        selectedHfpTransportId = null;
        MediaAudioButton.IsEnabled = false;
        MediaAudioButton.Content = "Nenhum alvo A2DP confirmado";
        A2dpTargetText.Text = "Alvo A2DP: aguardando seleção";
        HfpEnableButton.IsEnabled = false;
        HfpEnableButton.Content = "Testar / habilitar chamadas";
        HfpTransportText.Text = "Transporte HFP: aguardando seleção";
        HfpStatusInfoBar.Severity = InfoBarSeverity.Warning;
        HfpStatusInfoBar.Title = "HFP aguardando teste";
        HfpStatusInfoBar.Message = "Selecione um smartphone e use o botão para solicitar o acesso real ao transporte telefônico.";
        InspectButton.IsEnabled = false;
        InspectionStatusText.Text = "Nenhuma inspeção executada";
        InspectionTextBox.Text = "Selecione um dispositivo e execute a inspeção detalhada.";
        RenderRemoteVolumeStatus(null);
    }

    private void RenderRemoteVolumeStatus(RemoteVolumeStatus? status)
    {
        remoteVolumeStatus = status;
        if (status is null)
        {
            RemoteVolumeInfoBar.Severity = InfoBarSeverity.Informational;
            RemoteVolumeInfoBar.Title = "Volume remoto aguardando consulta";
            RemoteVolumeInfoBar.Message = "Selecione um dispositivo para consultar a capacidade real.";
            RemoteVolumeStatusText.Text = "Volume remoto: aguardando seleção";
            RemoteVolumeSlider.IsEnabled = false;
            RemoteVolumeButton.IsEnabled = false;
            return;
        }

        var canControl = status.CanControl && selectedDeviceId is not null && !disposed;
        RemoteVolumeInfoBar.Severity = status.Availability switch
        {
            RemoteVolumeAvailability.Available => InfoBarSeverity.Success,
            RemoteVolumeAvailability.Failed => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Warning,
        };
        RemoteVolumeInfoBar.Title = status.Availability switch
        {
            RemoteVolumeAvailability.Available => "Volume remoto disponível",
            RemoteVolumeAvailability.NotExposed => "Volume remoto não exposto",
            RemoteVolumeAvailability.Failed => "Falha no volume remoto",
            _ => "Volume remoto desconhecido",
        };
        RemoteVolumeInfoBar.Message = status.Message ?? "O Windows não retornou uma explicação adicional.";
        RemoteVolumeStatusText.Text = status.Level is float level
            ? $"Volume remoto: {level:P0} · origem: {status.Source}"
            : $"Volume remoto: {status.Availability} · origem: {status.Source}";
        RemoteVolumeSlider.IsEnabled = canControl;
        RemoteVolumeButton.IsEnabled = canControl;
    }

    private void RenderInspection(BluetoothDeviceInspectionSnapshot snapshot)
    {
        InspectionStatusText.Text = $"Concluída em {snapshot.ObservedAt.ToLocalTime():HH:mm:ss}: {snapshot.Endpoints.Count} endpoint(s), {snapshot.GattServices.Count} GATT, {snapshot.RfcommServices.Count} RFCOMM, {snapshot.Diagnostics.Count} diagnóstico(s).";
        InspectionTextBox.Text = BluetoothInspectionTextFormatter.Format(snapshot);
        RenderRemoteVolumeStatus(snapshot.RemoteVolume);
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
