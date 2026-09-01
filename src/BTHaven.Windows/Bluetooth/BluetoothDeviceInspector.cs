using System.Collections;
using BTHaven.Core.Audio;
using BTHaven.Core.Calls;
using BTHaven.Core.Contracts;
using BTHaven.Core.Devices;
using BTHaven.Windows.Audio;
using BTHaven.Windows.Diagnostics;
using BTHaven.Windows.Telephony;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;

namespace BTHaven.Windows.Bluetooth;

internal enum BluetoothInspectionMatchStatus
{
    None,
    Matched,
    Ambiguous,
}

internal sealed record BluetoothInspectionCandidate(
    string Id,
    string Name,
    string? ContainerId,
    string? Address);

internal sealed record BluetoothInspectionMatchResult(
    BluetoothInspectionMatchStatus Status,
    IReadOnlyList<BluetoothInspectionCandidate> Matches);

public sealed partial class BluetoothDeviceInspector : IBluetoothDeviceInspector
{
    private static readonly IReadOnlyList<string> EndpointProperties =
    [
        "System.ItemNameDisplay",
        "System.Devices.DeviceInstanceId",
        "System.Devices.InterfaceEnabled",
        "System.Devices.Icon",
        "System.Devices.GlyphIcon",
        WindowsDevicePropertyNames.CanPair,
        WindowsDevicePropertyNames.ContainerId,
        WindowsDevicePropertyNames.IsConnected,
        WindowsDevicePropertyNames.IsPaired,
        WindowsDevicePropertyNames.IsPresent,
        WindowsDevicePropertyNames.DeviceAddress,
        WindowsDevicePropertyNames.Manufacturer,
        WindowsDevicePropertyNames.ModelName,
        WindowsDevicePropertyNames.SignalStrength,
        WindowsDevicePropertyNames.ProtocolId,
        WindowsDevicePropertyNames.BatteryLife,
        WindowsDevicePropertyNames.BatteryPlusCharging,
        WindowsDevicePropertyNames.ChargingState,
    ];

    private readonly IWindowsDiagnosticLogger logger;
    private readonly IBatteryService? batteryService;
    private readonly A2dpSinkService? a2dpService;
    private readonly HfpPhoneTransportService? hfpService;
    private readonly IRemoteVolumeService remoteVolumeService;

    public BluetoothDeviceInspector(
        IWindowsDiagnosticLogger? logger = null,
        IBatteryService? batteryService = null,
        A2dpSinkService? a2dpService = null,
        HfpPhoneTransportService? hfpService = null,
        IRemoteVolumeService? remoteVolumeService = null)
    {
        this.logger = logger ?? NullDiagnosticLogger.Instance;
        this.batteryService = batteryService;
        this.a2dpService = a2dpService;
        this.hfpService = hfpService;
        this.remoteVolumeService = remoteVolumeService ?? new WindowsRemoteVolumeService(this.logger);
    }

    public async Task<BluetoothDeviceInspectionSnapshot> InspectAsync(
        BluetoothDeviceModel device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();
        logger.Info("Bluetooth.Inspection.Started", new Dictionary<string, object?>
        {
            ["deviceId"] = device.Id,
            ["name"] = device.Name,
            ["transport"] = device.Transport.ToString(),
            ["containerId"] = device.ContainerId,
        });

        var diagnostics = new List<BluetoothInspectionDiagnostic>();
        var associations = await ResolveAssociationEndpointsAsync(device, diagnostics, cancellationToken).ConfigureAwait(false);
        var endpoints = associations
            .Select(endpoint => endpoint.ToInspection())
            .ToList();
        var properties = endpoints.SelectMany(endpoint => endpoint.Properties).ToList();
        var gattServices = new List<BluetoothGattServiceInspection>();
        var rfcommServices = new List<BluetoothRfcommServiceInspection>();
        string? classOfDevice = null;
        string? connectionStatus = null;
        string? hostName = null;
        bool? securePairing = null;

        var classicEndpoint = associations.FirstOrDefault(endpoint => endpoint.Transport == BluetoothTransport.Classic);
        if (classicEndpoint is not null)
        {
            var classic = await InspectClassicAsync(
                device,
                classicEndpoint,
                endpoints,
                rfcommServices,
                diagnostics,
                cancellationToken).ConfigureAwait(false);
            if (classic is not null)
            {
                classOfDevice = classic.ClassOfDevice;
                connectionStatus = classic.ConnectionStatus;
                hostName = classic.HostName;
                securePairing = classic.WasSecureConnectionUsedForPairing;
            }
        }

        var bleEndpoint = associations.FirstOrDefault(endpoint => endpoint.Transport == BluetoothTransport.LowEnergy);
        if (bleEndpoint is not null)
        {
            await InspectBleAsync(
                device,
                bleEndpoint,
                endpoints,
                gattServices,
                diagnostics,
                cancellationToken).ConfigureAwait(false);
        }

        var battery = await InspectBatteryAsync(device, diagnostics, cancellationToken).ConfigureAwait(false);
        var profiles = await InspectProfilesAsync(device, diagnostics, cancellationToken).ConfigureAwait(false);
        var remoteVolume = await InspectRemoteVolumeAsync(device, diagnostics, cancellationToken).ConfigureAwait(false);
        var isPaired = CollapseState(endpoints.Select(endpoint => endpoint.IsPaired).Append(device.IsPaired));
        var isConnected = CollapseState(endpoints.Select(endpoint => endpoint.IsConnected).Append(device.IsConnected));
        var isPresent = CollapseState(endpoints.Select(endpoint => endpoint.IsPresent).Append(device.IsPresent));

        var snapshot = new BluetoothDeviceInspectionSnapshot
        {
            DeviceId = device.Id,
            Name = device.Name,
            ContainerId = endpoints.Select(endpoint => endpoint.ContainerId).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? device.ContainerId,
            Manufacturer = endpoints.Select(endpoint => endpoint.Manufacturer).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? device.Manufacturer,
            Model = endpoints.Select(endpoint => endpoint.Model).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? device.Model,
            Address = endpoints.Select(endpoint => endpoint.Address).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? device.Address,
            ClassOfDevice = classOfDevice,
            ConnectionStatus = connectionStatus ?? FormatConnectionStatus(isPaired, isConnected, isPresent),
            IsPaired = isPaired,
            IsConnected = isConnected,
            IsPresent = isPresent,
            Rssi = SelectRssi(endpoints.Select(endpoint => endpoint.Rssi).Append(device.Rssi)),
            HostName = hostName,
            WasSecureConnectionUsedForPairing = securePairing,
            Transport = device.Transport,
            ObservedAt = DateTimeOffset.UtcNow,
            DeviceProperties = properties,
            Endpoints = endpoints,
            BatteryObservations = battery,
            ProfileObservations = profiles,
            RemoteVolume = remoteVolume,
            GattServices = gattServices,
            RfcommServices = rfcommServices,
            Diagnostics = diagnostics,
        };
        logger.Info("Bluetooth.Inspection.Completed", new Dictionary<string, object?>
        {
            ["deviceId"] = device.Id,
            ["endpointCount"] = endpoints.Count,
            ["gattServiceCount"] = gattServices.Count,
            ["rfcommServiceCount"] = rfcommServices.Count,
            ["batteryObservationCount"] = battery.Count,
            ["profileObservationCount"] = profiles.Count,
            ["diagnosticCount"] = diagnostics.Count,
        });
        return snapshot;
    }

    private async Task<IReadOnlyList<AssociationEndpoint>> ResolveAssociationEndpointsAsync(
        BluetoothDeviceModel model,
        List<BluetoothInspectionDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var endpoints = new List<AssociationEndpoint>();
        var inspectClassic = model.Transport is BluetoothTransport.Classic or BluetoothTransport.DualMode or BluetoothTransport.Unknown;
        var inspectBle = model.Transport is BluetoothTransport.LowEnergy or BluetoothTransport.DualMode or BluetoothTransport.Unknown;
        if (inspectClassic)
        {
            await AddAssociationEndpointsAsync(
                model,
                BluetoothDevice.GetDeviceSelectorFromPairingState(true),
                BluetoothTransport.Classic,
                "BluetoothClassic.Association",
                endpoints,
                diagnostics,
                cancellationToken).ConfigureAwait(false);
        }

        if (inspectBle)
        {
            await AddAssociationEndpointsAsync(
                model,
                BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
                BluetoothTransport.LowEnergy,
                "BluetoothLE.Association",
                endpoints,
                diagnostics,
                cancellationToken).ConfigureAwait(false);
        }

        return endpoints;
    }

    private async Task AddAssociationEndpointsAsync(
        BluetoothDeviceModel model,
        string selector,
        BluetoothTransport transport,
        string source,
        List<AssociationEndpoint> endpoints,
        List<BluetoothInspectionDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            var infos = await DeviceInformation.FindAllAsync(selector, EndpointProperties);
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = infos
                .Select(info => new BluetoothInspectionCandidate(
                    info.Id,
                    info.Name,
                    GetProperty(info, WindowsDevicePropertyNames.ContainerId),
                    GetProperty(info, WindowsDevicePropertyNames.DeviceAddress)))
                .ToArray();
            var match = MatchCandidates(model, candidates);
            var matchedIds = match.Matches
                .Select(candidate => candidate.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var matches = infos.Where(info => matchedIds.Contains(info.Id)).ToArray();
            foreach (var info in matches)
            {
                endpoints.Add(CreateAssociationEndpoint(info, transport, source));
            }

            var status = match.Status switch
            {
                BluetoothInspectionMatchStatus.Matched => "Success",
                BluetoothInspectionMatchStatus.Ambiguous => "Ambiguous",
                _ => "NotAvailable",
            };
            var message = match.Status switch
            {
                BluetoothInspectionMatchStatus.Ambiguous => "Multiple association endpoints matched the device name; no endpoint was selected.",
                BluetoothInspectionMatchStatus.None => "No association endpoint matched the device identity",
                _ => null,
            };
            AddDiagnostic(
                diagnostics,
                $"{source}.FindAllAsync",
                status,
                null,
                null,
                message,
                source);
            logger.Debug("Bluetooth.AssociationEndpoint.Enumerated", new Dictionary<string, object?>
            {
                ["source"] = source,
                ["selector"] = selector,
                ["matchStatus"] = match.Status.ToString(),
                ["matchedCount"] = matches.Length,
                ["availableCount"] = infos.Count,
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AddDiagnostic(diagnostics, $"{source}.FindAllAsync", "Failed", exception, null, null, source);
            logger.Error("Bluetooth.AssociationEndpoint.EnumerationFailed", exception, new Dictionary<string, object?>
            {
                ["source"] = source,
                ["selector"] = selector,
            });
        }
    }

    private async Task<ClassicInspectionData?> InspectClassicAsync(
        BluetoothDeviceModel model,
        AssociationEndpoint association,
        List<BluetoothEndpointInspection> endpoints,
        List<BluetoothRfcommServiceInspection> rfcommServices,
        List<BluetoothInspectionDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        BluetoothDevice? bluetoothDevice = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger.Debug("Bluetooth.Classic.FromIdStarted", new Dictionary<string, object?>
            {
                ["deviceId"] = association.Id,
            });
            bluetoothDevice = await BluetoothDevice.FromIdAsync(association.Id);
            if (bluetoothDevice is null)
            {
                AddDiagnostic(diagnostics, "BluetoothDevice.FromIdAsync", "NotAvailable", null, null, null, "BluetoothClassic.Device");
                return null;
            }

            var classOfDevice = bluetoothDevice.ClassOfDevice;
            var classOfDeviceText = classOfDevice is null
                ? null
                : $"raw=0x{classOfDevice.RawValue:X6}; major={classOfDevice.MajorClass}; minor={classOfDevice.MinorClass}; services={classOfDevice.ServiceCapabilities}";
            var observedProperties = ToProperties(association.Info.Properties, "BluetoothClassic.Device");
            endpoints.Add(new BluetoothEndpointInspection
            {
                Id = bluetoothDevice.DeviceId,
                Kind = "BluetoothClassic",
                Name = bluetoothDevice.Name,
                Transport = BluetoothTransport.Classic,
                Source = "BluetoothClassic.Device",
                ContainerId = association.ContainerId,
                Address = FormatAddress(bluetoothDevice.BluetoothAddress) ?? association.Address,
                IsPaired = association.IsPaired,
                IsConnected = bluetoothDevice.ConnectionStatus.ToString().Equals("Connected", StringComparison.OrdinalIgnoreCase),
                IsPresent = association.IsPresent,
                Manufacturer = association.Manufacturer,
                Model = association.Model,
                Rssi = association.Rssi,
                ProtocolId = association.ProtocolId,
                Status = bluetoothDevice.ConnectionStatus.ToString(),
                ObservedAt = DateTimeOffset.UtcNow,
                Properties = observedProperties,
            });
            AddDiagnostic(diagnostics, "BluetoothDevice.Properties", "Success", null, null, null, "BluetoothClassic.Device");

            var rfcommResult = await bluetoothDevice.GetRfcommServicesAsync(BluetoothCacheMode.Uncached);
            var rfcommStatus = rfcommResult.Error.ToString();
            AddDiagnostic(diagnostics, "BluetoothDevice.GetRfcommServicesAsync", rfcommStatus, null, null, null, "BluetoothClassic.RFCOMM");
            logger.Info("Bluetooth.Rfcomm.Enumerated", new Dictionary<string, object?>
            {
                ["deviceId"] = model.Id,
                ["status"] = rfcommStatus,
                ["count"] = rfcommResult.Services.Count,
            });
            foreach (var service in rfcommResult.Services)
            {
                using (service)
                {
                    rfcommServices.Add(new BluetoothRfcommServiceInspection
                    {
                        ServiceId = service.ServiceId.AsString(),
                        KnownName = KnownServiceName(service.ServiceId.AsString()),
                        DeviceId = model.Id,
                        Status = rfcommStatus,
                        ObservedAt = DateTimeOffset.UtcNow,
                    });
                }
            }

            return new ClassicInspectionData(
                classOfDeviceText,
                bluetoothDevice.ConnectionStatus.ToString(),
                bluetoothDevice.HostName?.ToString(),
                bluetoothDevice.WasSecureConnectionUsedForPairing);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AddDiagnostic(diagnostics, "BluetoothClassic.Inspection", "Failed", exception, null, null, "BluetoothClassic.Device");
            logger.Error("Bluetooth.Classic.InspectionFailed", exception, new Dictionary<string, object?>
            {
                ["deviceId"] = model.Id,
                ["endpointId"] = association.Id,
            });
            return null;
        }
        finally
        {
            bluetoothDevice?.Dispose();
        }
    }

    private async Task<IReadOnlyList<BluetoothBatteryObservation>> InspectBatteryAsync(
        BluetoothDeviceModel device,
        List<BluetoothInspectionDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (batteryService is null)
        {
            return
            [
                new BluetoothBatteryObservation
                {
                    Source = "battery-service",
                    Status = "NotConfigured",
                    Message = "Battery service was not provided to the inspector.",
                },
            ];
        }

        try
        {
            var state = await batteryService.GetBatteryAsync(device, cancellationToken).ConfigureAwait(false);
            var status = state.Percentage.HasValue || state.IsCharging.HasValue ? "Available" : "Unavailable";
            AddDiagnostic(diagnostics, "Battery.Aggregated", status, null, null, state.Source, "Battery");
            return
            [
                new BluetoothBatteryObservation
                {
                    Source = state.Source,
                    Status = status,
                    Percentage = state.Percentage,
                    IsCharging = state.IsCharging,
                    Confidence = state.Confidence,
                    ObservedAt = state.LastUpdated,
                    Message = status == "Unavailable" ? "No trustworthy battery value was returned." : null,
                },
            ];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AddDiagnostic(diagnostics, "Battery.Aggregated", "Failed", exception, null, null, "Battery");
            return
            [
                new BluetoothBatteryObservation
                {
                    Source = "battery-service",
                    Status = "Failed",
                    HResult = $"0x{exception.HResult:X8}",
                    Message = exception.Message,
                },
            ];
        }
    }

    private async Task<IReadOnlyList<BluetoothProfileObservation>> InspectProfilesAsync(
        BluetoothDeviceModel device,
        List<BluetoothInspectionDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var observations = new List<BluetoothProfileObservation>();
        if (a2dpService is null)
        {
            observations.Add(new BluetoothProfileObservation
            {
                Profile = "A2DP",
                Source = "AudioPlaybackConnection",
                Status = "NotConfigured",
                Message = "A2DP service was not provided to the inspector.",
            });
        }
        else
        {
            try
            {
                var targets = await a2dpService.GetAvailableDevicesAsync(cancellationToken).ConfigureAwait(false);
                var matches = targets.Where(target => MatchesDevice(device, target)).ToArray();
                var status = matches.Length == 1 ? "Available" : matches.Length == 0 ? "NotAvailable" : "Ambiguous";
                observations.Add(new BluetoothProfileObservation
                {
                    Profile = "A2DP",
                    Source = "AudioPlaybackConnection",
                    Status = status,
                    DeviceId = matches.Length == 1 ? matches[0].Id : null,
                    Message = status == "NotAvailable" ? "No official A2DP target matched this device." : null,
                });
                AddDiagnostic(diagnostics, "A2DP.Discovery", status, null, null, null, "AudioPlaybackConnection");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                observations.Add(new BluetoothProfileObservation
                {
                    Profile = "A2DP",
                    Source = "AudioPlaybackConnection",
                    Status = "Failed",
                    HResult = $"0x{exception.HResult:X8}",
                    Message = exception.Message,
                });
                AddDiagnostic(diagnostics, "A2DP.Discovery", "Failed", exception, null, null, "AudioPlaybackConnection");
            }
        }

        if (hfpService is null)
        {
            observations.Add(new BluetoothProfileObservation
            {
                Profile = "HFP",
                Source = "PhoneLineTransportDevice",
                Status = "NotConfigured",
                Message = "HFP service was not provided to the inspector.",
            });
        }
        else
        {
            try
            {
                var targets = await hfpService.GetAvailableDevicesAsync(cancellationToken).ConfigureAwait(false);
                var matches = targets.Where(target => MatchesDevice(device, target)).ToArray();
                var status = matches.Length == 1 ? "Discovered" : matches.Length == 0 ? "NotAvailable" : "Ambiguous";
                observations.Add(new BluetoothProfileObservation
                {
                    Profile = "HFP",
                    Source = "PhoneLineTransportDevice",
                    Status = status,
                    DeviceId = matches.Length == 1 ? matches[0].Id : null,
                    Message = status == "NotAvailable" ? "No PhoneLineTransportDevice matched this device." : null,
                });
                AddDiagnostic(diagnostics, "HFP.Discovery", status, null, null, null, "PhoneLineTransportDevice");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                observations.Add(new BluetoothProfileObservation
                {
                    Profile = "HFP",
                    Source = "PhoneLineTransportDevice",
                    Status = "Failed",
                    HResult = $"0x{exception.HResult:X8}",
                    Message = exception.Message,
                });
                AddDiagnostic(diagnostics, "HFP.Discovery", "Failed", exception, null, null, "PhoneLineTransportDevice");
            }
        }

        return observations;
    }

    private async Task<RemoteVolumeStatus> InspectRemoteVolumeAsync(
        BluetoothDeviceModel device,
        List<BluetoothInspectionDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            var status = await remoteVolumeService.GetStatusAsync(device, cancellationToken).ConfigureAwait(false);
            AddDiagnostic(diagnostics, "RemoteVolume.Status", status.Availability.ToString(), null, status.HResult, status.Message, status.Source);
            return status;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AddDiagnostic(diagnostics, "RemoteVolume.Status", "Failed", exception, null, null, "Windows.AVRCP");
            return new RemoteVolumeStatus
            {
                Availability = RemoteVolumeAvailability.Failed,
                Source = "Windows.AVRCP",
                HResult = $"0x{exception.HResult:X8}",
                Message = exception.Message,
            };
        }
    }

    internal static BluetoothInspectionMatchResult MatchCandidates(
        BluetoothDeviceModel model,
        IReadOnlyList<BluetoothInspectionCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(candidates);

        var endpointIds = model.Endpoints
            .Select(endpoint => endpoint.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var exactMatches = candidates
            .Where(candidate => endpointIds.Contains(candidate.Id))
            .ToArray();
        if (exactMatches.Length > 0)
        {
            return new(BluetoothInspectionMatchStatus.Matched, exactMatches);
        }

        if (!string.IsNullOrWhiteSpace(model.ContainerId))
        {
            var containerMatches = candidates
                .Where(candidate =>
                    !string.IsNullOrWhiteSpace(candidate.ContainerId)
                    && string.Equals(model.ContainerId, candidate.ContainerId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (containerMatches.Length > 0)
            {
                return new(BluetoothInspectionMatchStatus.Matched, containerMatches);
            }
        }

        var modelAddress = BluetoothDeviceIdentity.NormalizeAddress(model.Address);
        if (modelAddress.Length > 0)
        {
            var addressMatches = candidates
                .Where(candidate =>
                    BluetoothDeviceIdentity.NormalizeAddress(candidate.Address) == modelAddress)
                .ToArray();
            if (addressMatches.Length > 0)
            {
                return new(BluetoothInspectionMatchStatus.Matched, addressMatches);
            }
        }

        var nameMatches = candidates
            .Where(candidate => string.Equals(model.Name, candidate.Name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return nameMatches.Length switch
        {
            0 => new(BluetoothInspectionMatchStatus.None, []),
            1 => new(BluetoothInspectionMatchStatus.Matched, nameMatches),
            _ => new(BluetoothInspectionMatchStatus.Ambiguous, []),
        };
    }


    private static bool MatchesDevice(BluetoothDeviceModel device, RemoteAudioDeviceInfo target)
    {
        if (!string.IsNullOrWhiteSpace(device.ContainerId)
            && !string.IsNullOrWhiteSpace(target.ContainerId)
            && string.Equals(device.ContainerId, target.ContainerId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var address = NormalizeAddress(device.Address);
        if (!string.IsNullOrWhiteSpace(address))
        {
            var targetAddress = NormalizeAddress(target.Address);
            if (string.Equals(address, targetAddress, StringComparison.OrdinalIgnoreCase)
                || NormalizeAddress(target.Id).Contains(address, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return string.Equals(device.Name, target.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesDevice(BluetoothDeviceModel device, PhoneLineTransportModel target)
    {
        var address = NormalizeAddress(device.Address);
        if (!string.IsNullOrWhiteSpace(address)
            && (NormalizeAddress(target.Id).Contains(address, StringComparison.OrdinalIgnoreCase)
                || NormalizeAddress(target.DeviceId).Contains(address, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return string.Equals(device.Name, target.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static AssociationEndpoint CreateAssociationEndpoint(
        DeviceInformation info,
        BluetoothTransport transport,
        string source)
    {
        var properties = ToProperties(info.Properties, source);
        return new AssociationEndpoint
        {
            Info = info,
            Id = info.Id,
            Name = string.IsNullOrWhiteSpace(info.Name) ? "Bluetooth device" : info.Name,
            Transport = transport,
            Source = source,
            ContainerId = GetProperty(info, WindowsDevicePropertyNames.ContainerId),
            Address = GetProperty(info, WindowsDevicePropertyNames.DeviceAddress),
            Manufacturer = GetProperty(info, WindowsDevicePropertyNames.Manufacturer),
            Model = GetProperty(info, WindowsDevicePropertyNames.ModelName),
            Rssi = GetInt32(info.Properties, WindowsDevicePropertyNames.SignalStrength),
            IsPaired = GetBool(info.Properties, WindowsDevicePropertyNames.IsPaired),
            IsConnected = GetBool(info.Properties, WindowsDevicePropertyNames.IsConnected),
            IsPresent = GetBool(info.Properties, WindowsDevicePropertyNames.IsPresent),
            ProtocolId = GetProperty(info, WindowsDevicePropertyNames.ProtocolId),
            Status = FormatConnectionStatus(
                GetBool(info.Properties, WindowsDevicePropertyNames.IsPaired),
                GetBool(info.Properties, WindowsDevicePropertyNames.IsConnected),
                GetBool(info.Properties, WindowsDevicePropertyNames.IsPresent)),
            ObservedAt = DateTimeOffset.UtcNow,
            Properties = properties,
        };
    }

    private static IReadOnlyList<BluetoothObservedProperty> ToProperties(
        IReadOnlyDictionary<string, object>? source,
        string sourceName)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in EndpointProperties)
        {
            values[key] = source is not null && source.TryGetValue(key, out var value) ? value : null;
        }

        if (source is not null)
        {
            foreach (var pair in source)
            {
                values.TryAdd(pair.Key, pair.Value);
            }
        }

        var observedAt = DateTimeOffset.UtcNow;
        return values
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new BluetoothObservedProperty
            {
                Key = pair.Key,
                Type = pair.Value?.GetType().FullName ?? "null",
                Value = FormatPropertyValue(pair.Value),
                Source = sourceName,
                Status = pair.Value is null ? "NotExposed" : "Observed",
                ObservedAt = observedAt,
            })
            .ToArray();
    }

    private static string? GetProperty(DeviceInformation info, string key)
    {
        return info.Properties.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static bool? GetBool(IReadOnlyDictionary<string, object> properties, string key)
    {
        if (!properties.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value is bool boolean ? boolean : bool.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static int? GetInt32(IReadOnlyDictionary<string, object> properties, string key)
    {
        if (!properties.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value is int integer ? integer : int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static string? FormatPropertyValue(object? value)
    {
        if (value is null)
        {
            return null;
        }
        if (value is string text)
        {
            return text;
        }
        if (value is IEnumerable enumerable)
        {
            var values = new List<string>();
            foreach (var item in enumerable)
            {
                values.Add(item?.ToString() ?? "null");
            }
            return string.Join("; ", values);
        }
        return value.ToString();
    }

    private static string? FormatAddress(ulong address)
    {
        return address == 0 ? null : address.ToString("X12");
    }

    private static string NormalizeAddress(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }


    private static int? SelectRssi(IEnumerable<int?> values)
    {
        var valuesArray = values.ToArray();
        return valuesArray.FirstOrDefault(value => value is < 0)
            ?? valuesArray.FirstOrDefault(value => value.HasValue);
    }
    private static bool? CollapseState(IEnumerable<bool?> values)
    {
        var states = values.ToArray();
        if (states.Any(value => value == true))
        {
            return true;
        }
        return states.Any(value => value == false) ? false : null;
    }

    private static string FormatConnectionStatus(bool? paired, bool? connected, bool? present)
    {
        if (connected == true)
        {
            return "Connected";
        }
        if (paired == true && present == true)
        {
            return "PairedPresent";
        }
        if (paired == true)
        {
            return "PairedDisconnected";
        }
        return present == true ? "Present" : "Disconnected";
    }

    private static string? KnownServiceName(string serviceId)
    {
        var value = serviceId.ToLowerInvariant();
        if (value.Contains("0000111f", StringComparison.Ordinal)) return "Hands-Free Audio Gateway (HFP AG)";
        if (value.Contains("0000111e", StringComparison.Ordinal)) return "Hands-Free (HFP HF)";
        if (value.Contains("00001112", StringComparison.Ordinal)) return "Headset Audio Gateway";
        if (value.Contains("00001108", StringComparison.Ordinal)) return "Headset";
        if (value.Contains("0000110a", StringComparison.Ordinal)) return "Advanced Audio Distribution Source (A2DP)";
        if (value.Contains("0000110b", StringComparison.Ordinal)) return "Advanced Audio Distribution Sink (A2DP)";
        if (value.Contains("0000112f", StringComparison.Ordinal)) return "Phone Book Access Server (PBAP PSE)";
        if (value.Contains("0000112d", StringComparison.Ordinal)) return "SIM Access (SAP)";
        if (value.Contains("00001105", StringComparison.Ordinal)) return "OBEX Object Push";
        if (value.Contains("00001132", StringComparison.Ordinal)) return "Message Access Server (MAP)";
        return null;
    }

    private static void AddDiagnostic(
        List<BluetoothInspectionDiagnostic> diagnostics,
        string operation,
        string? status,
        Exception? exception,
        string? hResult,
        string? message,
        string? source)
    {
        diagnostics.Add(new BluetoothInspectionDiagnostic
        {
            Operation = operation,
            Source = source ?? "BluetoothDeviceInspector",
            Status = status,
            HResult = hResult ?? (exception is null ? null : $"0x{exception.HResult:X8}"),
            ExceptionType = exception?.GetType().FullName,
            Message = message ?? exception?.Message,
            ObservedAt = DateTimeOffset.UtcNow,
        });
    }

    private sealed record ClassicInspectionData(
        string? ClassOfDevice,
        string? ConnectionStatus,
        string? HostName,
        bool? WasSecureConnectionUsedForPairing);

    private sealed record AssociationEndpoint
    {
        public required DeviceInformation Info { get; init; }
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required BluetoothTransport Transport { get; init; }
        public required string Source { get; init; }
        public string? ContainerId { get; init; }
        public string? Address { get; init; }
        public string? Manufacturer { get; init; }
        public string? Model { get; init; }
        public int? Rssi { get; init; }
        public bool? IsPaired { get; init; }
        public bool? IsConnected { get; init; }
        public bool? IsPresent { get; init; }
        public string? ProtocolId { get; init; }
        public string? Status { get; init; }
        public DateTimeOffset ObservedAt { get; init; }
        public required IReadOnlyList<BluetoothObservedProperty> Properties { get; init; }

        public BluetoothEndpointInspection ToInspection() => new()
        {
            Id = Id,
            Kind = "AssociationEndpoint",
            Name = Name,
            Transport = Transport,
            Source = Source,
            ContainerId = ContainerId,
            Address = Address,
            Manufacturer = Manufacturer,
            Model = Model,
            Rssi = Rssi,
            IsPaired = IsPaired,
            IsConnected = IsConnected,
            IsPresent = IsPresent,
            ProtocolId = ProtocolId,
            Status = Status,
            ObservedAt = ObservedAt,
            Properties = Properties,
        };
    }
}
