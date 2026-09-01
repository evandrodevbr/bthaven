using BTHaven.Core.Audio;
using BTHaven.Core.Devices;
using BTHaven.Windows.Audio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BTHaven_App;

public sealed partial class MainPage
{
    private async void InspectButton_Click(object sender, RoutedEventArgs e)
    {
        logger.Info("App.InspectionButton.Clicked", new Dictionary<string, object?>
        {
            ["deviceId"] = selectedDeviceId,
        });
        if (selectedDeviceId is null || !devices.TryGetValue(selectedDeviceId, out var device))
        {
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            StatusInfoBar.Message = "Selecione um dispositivo antes de iniciar a inspeção.";
            return;
        }

        InspectButton.IsEnabled = false;
        InspectionStatusText.Text = "Inspeção em andamento; GATT e RFCOMM podem demorar.";
        try
        {
            var snapshot = await deviceInspector.InspectAsync(device, lifetime.Token);
            if (selectedDeviceId is null || !string.Equals(selectedDeviceId, device.Id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            selectedInspection = snapshot;
            RenderInspection(snapshot);
            StatusInfoBar.Severity = snapshot.Diagnostics.Any(diagnostic => diagnostic.Status == "Failed")
                ? InfoBarSeverity.Warning
                : InfoBarSeverity.Success;
            StatusInfoBar.Message = $"Inspeção concluída: {snapshot.Endpoints.Count} endpoint(s), {snapshot.GattServices.Count} serviço(s) GATT, {snapshot.RfcommServices.Count} serviço(s) RFCOMM.";
            logger.Info("App.InspectionButton.Completed", new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
                ["endpointCount"] = snapshot.Endpoints.Count,
                ["gattServiceCount"] = snapshot.GattServices.Count,
                ["rfcommServiceCount"] = snapshot.RfcommServices.Count,
                ["diagnosticCount"] = snapshot.Diagnostics.Count,
            });
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.Error("App.InspectionButton.Failed", exception, new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
            });
            InspectionStatusText.Text = "Inspeção falhou; consulte Logs.";
            InspectionTextBox.Text = $"Falha na inspeção: {exception.Message}";
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Message = "A inspeção falhou; o HRESULT e a stack trace foram gravados nos Logs.";
        }
        finally
        {
            InspectButton.IsEnabled = selectedDeviceId is not null && !disposed;
        }
    }

    private async void MediaToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (suppressMediaToggleEvents
            || sender is not ToggleSwitch toggle
            || toggle.Tag is not string deviceId
            || !devices.TryGetValue(deviceId, out var device))
        {
            return;
        }

        toggle.IsEnabled = false;
        try
        {
            if (toggle.IsOn)
            {
                var targets = await a2dpService.GetAvailableDevicesAsync(lifetime.Token);
                var matches = targets.Where(target => MatchesDevice(device, target)).ToArray();
                if (matches.Length != 1)
                {
                    SetMediaToggleState(toggle, false);
                    StatusInfoBar.Severity = InfoBarSeverity.Warning;
                    StatusInfoBar.Message = matches.Length == 0
                        ? "Nenhum alvo A2DP oficial correspondeu a este dispositivo."
                        : $"Foram encontrados {matches.Length} alvos A2DP; a conexão não foi iniciada por ambiguidade.";
                    return;
                }

                var targetId = matches[0].Id;
                a2dpLogicalDeviceIds[targetId] = device.Id;
                if (string.Equals(selectedDeviceId, device.Id, StringComparison.OrdinalIgnoreCase))
                {
                    selectedA2dpDeviceId = targetId;
                }
                var connected = await a2dpService.ConnectAsync(targetId, lifetime.Token);
                if (connected)
                {
                    if (AutoReconnectCheckBox.IsChecked == true)
                    {
                        await autoReconnectService.EnableAsync(targetId, lifetime.Token);
                    }
                    activeMediaDeviceId = device.Id;
                    StatusInfoBar.Severity = InfoBarSeverity.Success;
                    StatusInfoBar.Message = $"Áudio A2DP ativo para {device.Name}. Reproduza mídia no telefone para exercitar o caminho.";
                }
                else
                {
                    SetMediaToggleState(toggle, false);
                    StatusInfoBar.Severity = InfoBarSeverity.Error;
                    StatusInfoBar.Message = "O Windows não abriu a conexão A2DP; consulte Logs para o HRESULT.";
                }
            }
            else if (string.Equals(activeMediaDeviceId, device.Id, StringComparison.OrdinalIgnoreCase))
            {
                await autoReconnectService.DisableAsync();
                await a2dpService.DisconnectAsync(lifetime.Token);
                activeMediaDeviceId = null;
                StatusInfoBar.Severity = InfoBarSeverity.Informational;
                StatusInfoBar.Message = "Áudio A2DP desativado.";
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.Error("App.MediaToggle.Failed", exception, new Dictionary<string, object?>
            {
                ["deviceId"] = deviceId,
                ["isOn"] = toggle.IsOn,
            });
            SetMediaToggleState(toggle, false);
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Message = "Falha na ação de mídia; o HRESULT e a stack trace foram gravados nos Logs.";
        }
        finally
        {
            suppressMediaToggleEvents = true;
            try
            {
                RefreshRows();
            }
            finally
            {
                suppressMediaToggleEvents = false;
            }
        }
    }

    private async void RemoteVolumeButton_Click(object sender, RoutedEventArgs e)
    {
        logger.Info("App.RemoteVolumeButton.Clicked", new Dictionary<string, object?>
        {
            ["deviceId"] = selectedDeviceId,
            ["level"] = RemoteVolumeSlider.Value,
        });
        if (selectedDeviceId is null || !devices.TryGetValue(selectedDeviceId, out var device))
        {
            return;
        }
        if (remoteVolumeStatus?.CanControl != true)
        {
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            StatusInfoBar.Message = "O Windows não expôs um controlador oficial de volume remoto para este smartphone.";
            return;
        }

        RemoteVolumeButton.IsEnabled = false;
        try
        {
            var status = await remoteVolumeService.SetVolumeAsync(device, (float)RemoteVolumeSlider.Value, lifetime.Token);
            RenderRemoteVolumeStatus(status);
            StatusInfoBar.Severity = status.CanControl ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
            StatusInfoBar.Message = status.Message ?? $"Volume remoto: {status.Availability}.";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.Error("App.RemoteVolumeButton.Failed", exception, new Dictionary<string, object?>
            {
                ["deviceId"] = selectedDeviceId,
            });
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Message = "Falha ao alterar o volume remoto; consulte Logs.";
        }
        finally
        {
            RemoteVolumeButton.IsEnabled = remoteVolumeStatus?.CanControl == true && !disposed;
        }
    }

    private void SetMediaToggleState(ToggleSwitch toggle, bool isOn)
    {
        suppressMediaToggleEvents = true;
        try
        {
            toggle.IsOn = isOn;
        }
        finally
        {
            suppressMediaToggleEvents = false;
        }
    }
}
