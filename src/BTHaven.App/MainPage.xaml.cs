using System.Collections.ObjectModel;
using BTHaven.Core.Audio;
using BTHaven.Core.Calls;
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
    private Task? watchTask;
    private string? selectedDeviceId;
    private string? selectedA2dpDeviceId;
    private string? selectedHfpTransportId;
    private string? selectedOutputEndpointId;
    private BluetoothDeviceInspectionSnapshot? selectedInspection;
    private RemoteVolumeStatus? remoteVolumeStatus;
    private string? activeMediaDeviceId;
    private bool suppressMediaToggleEvents;
    private bool loaded;
    private bool ready;
    private bool disposed;
    private readonly Dictionary<string, string> a2dpLogicalDeviceIds = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<DeviceRowViewModel> Rows { get; } = [];

    public MainPage()
    {
        InitializeComponent();

        logger = TraceDiagnosticLogger.Instance;
        deviceManager = new BluetoothDeviceManager(logger);
        batteryService = new WindowsBatteryService(logger);
        endpointManager = new AudioEndpointManager(logger);
        a2dpService = new A2dpSinkService(logger);
        a2dpService.StateChanged += A2dpService_StateChanged;
        autoReconnectService = new A2dpAutoReconnectService(a2dpService, logger);
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

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
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
        watchTask = ConsumeDeviceChangesAsync(lifetime.Token);
        logger.Info("App.DeviceWatch.Started");
    }

    private async void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (disposed)
        {
            return;
        }

        logger.Info("App.MainPage.Unloaded");
        disposed = true;
        lifetime.Cancel();
        try
        {
            if (watchTask is not null)
            {
                await watchTask;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            logger.Debug("App.DeviceWatch.CancelledDuringShutdown");
        }
        catch (Exception exception)
        {
            logger.Error("App.DeviceWatch.ShutdownFailed", exception);
        }
        finally
        {
            try
            {
                a2dpService.StateChanged -= A2dpService_StateChanged;
                await deviceManager.DisposeAsync();
                await batteryService.DisposeAsync();
                await autoReconnectService.DisposeAsync();
                await a2dpService.DisposeAsync();
                await hfpService.DisposeAsync();
                logger.Info("App.MainPage.Disposed");
            }
            catch (Exception exception)
            {
                logger.Error("App.Services.DisposeFailed", exception);
            }
        }
    }

    private async Task RefreshAsync()
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
            devices.Clear();
            foreach (var device in currentDevices)
            {
                devices[device.Id] = device;
            }
            foreach (var mapping in a2dpLogicalDeviceIds
                         .Where(mapping => !devices.ContainsKey(mapping.Value))
                         .Select(mapping => mapping.Key)
                         .ToArray())
            {
                a2dpLogicalDeviceIds.Remove(mapping);
            }
            if (selectedDeviceId is not null && !devices.ContainsKey(selectedDeviceId))
            {
                selectedDeviceId = null;
                DeviceList.SelectedItem = null;
                ClearSelection();
            }
            if (activeMediaDeviceId is not null && !devices.ContainsKey(activeMediaDeviceId))
            {
                activeMediaDeviceId = null;
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
                    StatusInfoBar.Severity = InfoBarSeverity.Error;
                    StatusInfoBar.Message = $"O watcher de dispositivos foi interrompido: {exception.Message}";
                });
            }
        }
    }

    private void ApplyDeviceChange(BluetoothDeviceChange change)
    {
        logger.Info("App.DeviceChange.Applying", new Dictionary<string, object?>
        {
            ["kind"] = change.Kind.ToString(),
            ["deviceId"] = change.DeviceId,
            ["name"] = change.Device?.Name,
            ["connected"] = change.Device?.IsConnected,
            ["paired"] = change.Device?.IsPaired,
            ["present"] = change.Device?.IsPresent,
        });
        if (change.Kind == BluetoothDeviceChangeKind.Removed)
        {
            devices.Remove(change.DeviceId);
            foreach (var mapping in a2dpLogicalDeviceIds
                         .Where(mapping => string.Equals(mapping.Value, change.DeviceId, StringComparison.OrdinalIgnoreCase))
                         .Select(mapping => mapping.Key)
                         .ToArray())
            {
                a2dpLogicalDeviceIds.Remove(mapping);
            }
            if (string.Equals(activeMediaDeviceId, change.DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                activeMediaDeviceId = null;
            }
            if (string.Equals(selectedDeviceId, change.DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                selectedDeviceId = null;
                ClearSelection();
            }
        }
        else if (change.Device is not null)
        {
            devices[change.DeviceId] = change.Device;
            if (string.Equals(selectedDeviceId, change.DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                if (selectedA2dpDeviceId is not null
                    && a2dpLogicalDeviceIds.TryGetValue(selectedA2dpDeviceId, out var mappedDeviceId)
                    && !string.Equals(mappedDeviceId, change.DeviceId, StringComparison.OrdinalIgnoreCase))
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
                RenderSelection(change.Device);
            }
        }

        RefreshRows();
    }

    private void A2dpService_StateChanged(MediaAudioSinkState state)
    {
        if (disposed)
        {
            return;
        }

        var eventDeviceId = a2dpService.DeviceId;
        var observedServiceState = a2dpService.State;
        var mappedLogicalDeviceId = eventDeviceId is not null
            && a2dpLogicalDeviceIds.TryGetValue(eventDeviceId, out var mappedDeviceId)
            && devices.ContainsKey(mappedDeviceId)
            ? mappedDeviceId
            : null;
        var matchingLogicalIds = state == MediaAudioSinkState.Opened && eventDeviceId is not null
            ? devices.Values
                .Where(device => device.Endpoints.Any(endpoint =>
                    string.Equals(endpoint.Id, eventDeviceId, StringComparison.OrdinalIgnoreCase)))
                .Select(device => device.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];
        var logicalDeviceId = mappedLogicalDeviceId
            ?? (matchingLogicalIds.Length == 1 ? matchingLogicalIds[0] : null);

        logger.Debug("App.A2DP.StateChanged", new Dictionary<string, object?>
        {
            ["state"] = state.ToString(),
            ["deviceId"] = eventDeviceId,
            ["logicalDeviceId"] = logicalDeviceId,
        });

        void ApplyState()
        {
            if (disposed || a2dpService.State != observedServiceState)
            {
                return;
            }
            if (state == MediaAudioSinkState.Opened
                && !string.Equals(a2dpService.DeviceId, eventDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (state == MediaAudioSinkState.Opened)
            {
                if (logicalDeviceId is null || eventDeviceId is null)
                {
                    logger.Warning("App.A2DP.StateChange.Unresolved", new Dictionary<string, object?>
                    {
                        ["deviceId"] = eventDeviceId,
                        ["matchingLogicalCount"] = matchingLogicalIds.Length,
                    });
                    return;
                }

                activeMediaDeviceId = logicalDeviceId;
            }
            else if (state is MediaAudioSinkState.Disabled or MediaAudioSinkState.Failed)
            {
                if (autoReconnectService.IsEnabled && activeMediaDeviceId is not null)
                {
                    logger.Info("App.A2DP.StateChanged.WaitingForReconnect", new Dictionary<string, object?>
                    {
                        ["state"] = state.ToString(),
                        ["logicalDeviceId"] = activeMediaDeviceId,
                    });
                }
                else
                {
                    activeMediaDeviceId = null;
                }
            }

            RefreshRows();
        }

        if (!DispatcherQueue.TryEnqueue(ApplyState))
        {
            logger.Warning("App.A2DP.StateChange.NotApplied", new Dictionary<string, object?>
            {
                ["state"] = state.ToString(),
                ["reason"] = "DispatcherQueue was unavailable",
            });
        }
    }

    private void RefreshRows()
    {
        var filter = GetSelectedFilter();
        var visible = devices.Values
            .Where(device => BluetoothDeviceFilterMatcher.Matches(device, filter))
            .OrderByDescending(device => device.IsConnected)
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .Select(device => new DeviceRowViewModel(
                device,
                string.Equals(activeMediaDeviceId, device.Id, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        suppressMediaToggleEvents = true;
        try
        {
            Rows.Clear();
            foreach (var row in visible)
            {
                Rows.Add(row);
            }
        }
        finally
        {
            suppressMediaToggleEvents = false;
        }

        DeviceCountText.Text = visible.Length == 1 ? "1 dispositivo" : $"{visible.Length} dispositivos";
        EmptyState.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        logger.Debug("App.Rows.Refreshed", new Dictionary<string, object?>
        {
            ["filter"] = filter.ToString(),
            ["visibleCount"] = visible.Length,
            ["knownCount"] = devices.Count,
        });

        if (selectedDeviceId is not null)
        {
            DeviceList.SelectedItem = Rows.FirstOrDefault(row =>
                string.Equals(row.Id, selectedDeviceId, StringComparison.OrdinalIgnoreCase));
        }
    }

    private async void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var previousSelectedDeviceId = selectedDeviceId;
        if (DeviceList.SelectedItem is not DeviceRowViewModel row || !devices.TryGetValue(row.Id, out var device))
        {
            logger.Info("App.Device.SelectionCleared");
            selectedDeviceId = null;
            ClearSelection();
            return;
        }

        selectedDeviceId = device.Id;
        if (!string.Equals(previousSelectedDeviceId, device.Id, StringComparison.OrdinalIgnoreCase))
        {
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
        }
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
        await RefreshSelectedDeviceCapabilitiesAsync(device);
    }

    private async Task RefreshSelectedDeviceCapabilitiesAsync(BluetoothDeviceModel device)
    {
        try
        {
            logger.Info("App.DeviceCapabilities.Started", new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
                ["name"] = device.Name,
            });
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
            if (selectedDeviceId is null || !string.Equals(selectedDeviceId, device.Id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            var matchingTargets = BluetoothDeviceCorrelation.FindMatches(device, audioTargets);
            if (matchingTargets.Length == 1 && !string.IsNullOrWhiteSpace(matchingTargets[0].Id))
            {
                a2dpLogicalDeviceIds[matchingTargets[0].Id] = device.Id;
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
                if (selectedDeviceId is null || !string.Equals(selectedDeviceId, device.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
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

            var remoteVolume = await remoteVolumeService.GetStatusAsync(device, lifetime.Token);
            if (selectedDeviceId is null || !string.Equals(selectedDeviceId, device.Id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            RenderRemoteVolumeStatus(remoteVolume);
            logger.Info("App.DeviceCapabilities.Completed", new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
                ["batterySource"] = battery.Source,
                ["batteryPercentage"] = battery.Percentage,
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
            logger.Error("App.DeviceCapabilities.Failed", exception, new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
                ["name"] = device.Name,
            });
            A2dpTargetText.Text = "Falha ao consultar o alvo A2DP; consulte Logs.";
            HfpTransportText.Text = "Falha ao consultar o transporte HFP; consulte Logs.";
            RemoteVolumeInfoBar.Severity = InfoBarSeverity.Error;
            RemoteVolumeInfoBar.Title = "Falha ao consultar volume remoto";
            RemoteVolumeInfoBar.Message = "O estado de volume não foi confirmado; consulte Logs.";
            RemoteVolumeStatusText.Text = "Volume remoto: falha na consulta";
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        logger.Info("App.RefreshButton.Clicked");
        await RefreshAsync();
    }

    private async void DiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        logger.Info("App.DiagnosticsButton.Clicked");
        await ShowDiagnosticsAsync();
    }

    private async void LogsButton_Click(object sender, RoutedEventArgs e)
    {
        logger.Info("App.LogsButton.Clicked");
        await ShowLogsAsync();
    }

    public async Task ShowDiagnosticsAsync()
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

    public async Task ShowLogsAsync()
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
        await dialog.ShowAsync();
        logger.Info("App.LogViewer.Closed", new Dictionary<string, object?>
        {
            ["displayedLines"] = lines.Count,
        });
    }

    private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var filter = GetSelectedFilter();
        logger.Info("App.Filter.Changed", new Dictionary<string, object?>
        {
            ["filter"] = filter.ToString(),
        });
        if (loaded)
        {
            RefreshRows();
        }
    }

    private async void MediaAudioButton_Click(object sender, RoutedEventArgs e)
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
        if (requestedDeviceId is not null)
        {
            a2dpLogicalDeviceIds[requestedA2dpDeviceId] = requestedDeviceId;
        }
        MediaAudioButton.IsEnabled = false;
        try
        {
            await autoReconnectService.DisableAsync();
            var connected = await a2dpService.ConnectAsync(requestedA2dpDeviceId, lifetime.Token);
            if (connected && AutoReconnectCheckBox.IsChecked == true)
            {
                await autoReconnectService.EnableAsync(requestedA2dpDeviceId, lifetime.Token);
            }
            if (connected && requestedDeviceId is not null && devices.ContainsKey(requestedDeviceId))
            {
                activeMediaDeviceId = requestedDeviceId;
                RefreshRows();
            }
            var endpoint = OutputEndpointComboBox.SelectedItem as AudioEndpointModel;
            logger.Info("App.MediaAudioButton.Completed", new Dictionary<string, object?>
            {
                ["deviceId"] = selectedDeviceId,
                ["a2dpDeviceId"] = selectedA2dpDeviceId,
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
                ["deviceId"] = selectedDeviceId,
                ["a2dpDeviceId"] = selectedA2dpDeviceId,
            });
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Message = "Falha ao ativar o áudio; o HRESULT e a stack trace foram gravados nos Logs.";
        }
        finally
        {
            MediaAudioButton.IsEnabled = selectedA2dpDeviceId is not null && !disposed;
        }
    }

    private async void AutoReconnectCheckBox_Changed(object sender, RoutedEventArgs e)
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
        if (enabled && selectedA2dpDeviceId is not null)
        {
            await autoReconnectService.EnableAsync(selectedA2dpDeviceId, lifetime.Token);
        }
        else
        {
            await autoReconnectService.DisableAsync();
        }
    }

    private async void HfpEnableButton_Click(object sender, RoutedEventArgs e)
    {
        logger.Info("App.HFP.EnableButton.Clicked", new Dictionary<string, object?>
        {
            ["deviceId"] = selectedDeviceId,
            ["transportDeviceId"] = selectedHfpTransportId,
        });
        HfpEnableButton.IsEnabled = false;
        try
        {
            if (selectedDeviceId is null || !devices.TryGetValue(selectedDeviceId, out var device))
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

            if (selectedHfpTransportId is null)
            {
                var targets = await hfpService.GetAvailableDevicesAsync(lifetime.Token);
                var matches = BluetoothDeviceCorrelation.FindMatches(device, targets);
                selectedHfpTransportId = matches.Length == 1 ? matches[0].Id : null;
            }

            if (selectedHfpTransportId is null)
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
            var result = await hfpService.ActivateAsync(selectedHfpTransportId, lifetime.Token);
            HfpStatusInfoBar.Severity = result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
            HfpStatusInfoBar.Title = result.Succeeded ? "HFP ativo" : $"HFP: {result.Status}";
            HfpStatusInfoBar.Message = result.Succeeded
                ? result.Message ?? "Transporte HFP conectado."
                : $"{result.Message} AccessStatus={result.AccessStatus ?? "unknown"}. Consulte Logs para HRESULT e stack trace.";
            HfpTransportText.Text = $"Transporte HFP: {result.Status}; conectado={result.IsConnected}; registrado={result.IsRegistered}";
            logger.Info("App.HFP.EnableButton.Completed", new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
                ["transportDeviceId"] = selectedHfpTransportId,
                ["status"] = result.Status,
                ["succeeded"] = result.Succeeded,
                ["accessStatus"] = result.AccessStatus,
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
                ["deviceId"] = selectedDeviceId,
                ["transportDeviceId"] = selectedHfpTransportId,
            });
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
        InspectButton.IsEnabled = !disposed;
        if (selectedInspection?.DeviceId == device.Id)
        {
            RenderInspection(selectedInspection);
        }
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
