using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BTHaven.Core.Audio;
using BTHaven.Core.Contracts;
using BTHaven.Core.Devices;
using Windows.ApplicationModel.Calls;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Foundation.Metadata;

namespace BTHaven.Windows.Diagnostics;

public sealed class DiagnosticsExporter
{
    private const string SchemaVersion = "3";
    private readonly IBluetoothDeviceService deviceService;
    private readonly IAudioEndpointService endpointService;
    private readonly IWindowsDiagnosticLogger logger;
    private readonly IBluetoothDeviceInspector? deviceInspector;
    private readonly string outputDirectory;

    public DiagnosticsExporter(
        IBluetoothDeviceService deviceService,
        IAudioEndpointService endpointService,
        IWindowsDiagnosticLogger? logger = null,
        IBluetoothDeviceInspector? deviceInspector = null,
        string? outputDirectory = null)
    {
        this.deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
        this.endpointService = endpointService ?? throw new ArgumentNullException(nameof(endpointService));
        this.logger = logger ?? NullDiagnosticLogger.Instance;
        this.deviceInspector = deviceInspector;
        this.outputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BTHaven",
                "Diagnostics")
            : outputDirectory;
    }

    public async Task<string> ExportAsync(CancellationToken cancellationToken = default)
    {
        logger.Info("Diagnostics.ExportStarted", new Dictionary<string, object?>
        {
            ["maxLogLines"] = 5000,
            ["redacted"] = true,
            ["includesInspection"] = deviceInspector is not null,
        });

        var errors = new List<object>();
        var devices = await TryGetDevicesAsync(errors, cancellationToken).ConfigureAwait(false);
        var renderEndpoints = await TryGetEndpointsAsync(AudioEndpointDirection.Render, errors, cancellationToken).ConfigureAwait(false);
        var captureEndpoints = await TryGetEndpointsAsync(AudioEndpointDirection.Capture, errors, cancellationToken).ConfigureAwait(false);
        var adapter = await TryGetAdapterAsync(errors, cancellationToken).ConfigureAwait(false);
        var hfp = await TryGetHfpAsync(errors, cancellationToken).ConfigureAwait(false);
        var inspections = await TryGetInspectionsAsync(devices, errors, cancellationToken).ConfigureAwait(false);
        var recentLogs = logger.ReadRecent(maxLines: 5000, redactSensitive: true);

        var payload = new
        {
            schemaVersion = SchemaVersion,
            generatedAtUtc = DateTimeOffset.UtcNow,
            application = "BTHaven",
            operatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            adapter,
            devices = devices.Select(SanitizeDevice).ToArray(),
            inspections = inspections.Select(SanitizeInspection).ToArray(),
            audioEndpoints = renderEndpoints.Concat(captureEndpoints).Select(SanitizeEndpoint).ToArray(),
            hfp,
            errors,
            privacy = new
            {
                identifiers = "redacted-sha256-prefix",
                names = "redacted",
                phoneNumbers = "not-collected",
                audio = "not-collected",
                telemetry = "not-collected",
            },
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
        Directory.CreateDirectory(outputDirectory);
        using var stream = OpenUniqueArchiveFile(outputDirectory, out var path);
        using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create))
        {
            WriteEntry(archive, "diagnostics.json", json);
            WriteEntry(archive, "logs.jsonl", string.Join(Environment.NewLine, recentLogs) + (recentLogs.Count == 0 ? string.Empty : Environment.NewLine));
            WriteEntry(archive, "README.txt", "BTHaven diagnostics export. Device identifiers and names are redacted. Audio buffers, phone numbers, caller IDs, and telemetry are not included. Logs are JSONL and were exported with sensitive fields redacted.\r\n");
        }

        logger.Info("Diagnostics.ExportCompleted", new Dictionary<string, object?>
        {
            ["path"] = path,
            ["deviceCount"] = devices.Count,
            ["inspectionCount"] = inspections.Count,
            ["endpointCount"] = renderEndpoints.Count + captureEndpoints.Count,
            ["errorCount"] = errors.Count,
            ["logLineCount"] = recentLogs.Count,
        });
        return path;
    }

    private async Task<IReadOnlyList<BluetoothDeviceModel>> TryGetDevicesAsync(List<object> errors, CancellationToken cancellationToken)
    {
        try
        {
            return await deviceService.GetDevicesAsync(BluetoothDeviceFilter.All, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AddError(errors, "devices", exception);
            return [];
        }
    }

    private async Task<IReadOnlyList<AudioEndpointModel>> TryGetEndpointsAsync(
        AudioEndpointDirection direction,
        List<object> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            return await endpointService.GetEndpointsAsync(direction, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AddError(errors, $"audio:{direction}", exception);
            return [];
        }
    }

    private async Task<IReadOnlyList<BluetoothDeviceInspectionSnapshot>> TryGetInspectionsAsync(
        IReadOnlyList<BluetoothDeviceModel> devices,
        List<object> errors,
        CancellationToken cancellationToken)
    {
        if (deviceInspector is null)
        {
            return [];
        }

        var snapshots = new List<BluetoothDeviceInspectionSnapshot>(devices.Count);
        foreach (var device in devices)
        {
            try
            {
                snapshots.Add(await deviceInspector.InspectAsync(device, cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                AddError(errors, $"inspection:{Redact(device.Id)}", exception);
            }
        }
        return snapshots;
    }

    private static async Task<object> TryGetAdapterAsync(List<object> errors, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            return adapter is null
                ? new { available = false }
                : new
                {
                    available = true,
                    classicSupported = adapter.IsClassicSupported,
                    lowEnergySupported = adapter.IsLowEnergySupported,
                };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AddError(errors, "bluetooth-adapter", exception);
            return new { available = false };
        }
    }

    private static async Task<object> TryGetHfpAsync(List<object> errors, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var typePresent = ApiInformation.IsTypePresent("Windows.ApplicationModel.Calls.PhoneLineTransportDevice");
            var contractPresent = ApiInformation.IsApiContractPresent("Windows.ApplicationModel.Calls.CallsPhoneContract", 5);
            var selector = PhoneLineTransportDevice.GetDeviceSelector();
            var devices = await DeviceInformation.FindAllAsync(selector);
            return new
            {
                typePresent,
                callsPhoneContractV5Present = contractPresent,
                transportDeviceCount = devices.Count,
                transportOperations = "not-invoked",
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AddError(errors, "hfp-discovery", exception);
            return new { typePresent = false, callsPhoneContractV5Present = false, transportDeviceCount = 0, transportOperations = "not-invoked" };
        }
    }

    private static object SanitizeDevice(BluetoothDeviceModel device)
    {
        return new
        {
            id = Redact(device.Id),
            containerId = Redact(device.ContainerId),
            name = "<redacted>",
            manufacturer = device.Manufacturer is null ? null : "<redacted>",
            model = device.Model is null ? null : "<redacted>",
            address = "<redacted>",
            transport = device.Transport.ToString(),
            category = device.Category.ToString(),
            paired = device.IsPaired,
            connected = device.IsConnected,
            present = device.IsPresent,
            rssi = device.Rssi,
            capabilities = device.Capabilities.ToString(),
            services = device.Services,
            profiles = device.Profiles,
            batteryPercentage = device.Battery?.Percentage,
            batteryCharging = device.Battery?.IsCharging,
            batterySource = device.Battery?.Source,
            lastUpdated = device.LastUpdated,
        };
    }

    private static object SanitizeInspection(BluetoothDeviceInspectionSnapshot snapshot)
    {
        return new
        {
            deviceId = Redact(snapshot.DeviceId),
            name = "<redacted>",
            containerId = Redact(snapshot.ContainerId),
            manufacturer = snapshot.Manufacturer is null ? null : "<redacted>",
            model = snapshot.Model is null ? null : "<redacted>",
            address = "<redacted>",
            classOfDevice = snapshot.ClassOfDevice,
            connectionStatus = snapshot.ConnectionStatus,
            paired = snapshot.IsPaired,
            connected = snapshot.IsConnected,
            present = snapshot.IsPresent,
            rssi = snapshot.Rssi,
            hostName = snapshot.HostName is null ? null : "<redacted>",
            securePairing = snapshot.WasSecureConnectionUsedForPairing,
            transport = snapshot.Transport.ToString(),
            observedAt = snapshot.ObservedAt,
            properties = snapshot.DeviceProperties.Select(SanitizeProperty).ToArray(),
            endpoints = snapshot.Endpoints.Select(endpoint => new
            {
                id = Redact(endpoint.Id),
                kind = endpoint.Kind,
                name = "<redacted>",
                transport = endpoint.Transport.ToString(),
                source = endpoint.Source,
                containerId = Redact(endpoint.ContainerId),
                address = "<redacted>",
                manufacturer = endpoint.Manufacturer is null ? null : "<redacted>",
                model = endpoint.Model is null ? null : "<redacted>",
                rssi = endpoint.Rssi,
                paired = endpoint.IsPaired,
                connected = endpoint.IsConnected,
                present = endpoint.IsPresent,
                protocolId = Redact(endpoint.ProtocolId),
                status = endpoint.Status,
                observedAt = endpoint.ObservedAt,
                hResult = endpoint.HResult,
                message = endpoint.Message is null ? null : "<redacted>",
                properties = endpoint.Properties.Select(SanitizeProperty).ToArray(),
            }).ToArray(),
            battery = snapshot.BatteryObservations.Select(observation => new
            {
                source = observation.Source,
                status = observation.Status,
                percentage = observation.Percentage,
                charging = observation.IsCharging,
                confidence = observation.Confidence.ToString(),
                observedAt = observation.ObservedAt,
                hResult = observation.HResult,
                message = observation.Message is null ? null : "<redacted>",
            }).ToArray(),
            profiles = snapshot.ProfileObservations.Select(profile => new
            {
                profile = profile.Profile,
                source = profile.Source,
                status = profile.Status,
                deviceId = Redact(profile.DeviceId),
                observedAt = profile.ObservedAt,
                hResult = profile.HResult,
                message = profile.Message is null ? null : "<redacted>",
            }).ToArray(),
            remoteVolume = snapshot.RemoteVolume is null ? null : new
            {
                availability = snapshot.RemoteVolume.Availability.ToString(),
                source = snapshot.RemoteVolume.Source,
                level = snapshot.RemoteVolume.Level,
                observedAt = snapshot.RemoteVolume.ObservedAt,
                hResult = snapshot.RemoteVolume.HResult,
                message = snapshot.RemoteVolume.Message is null ? null : "<redacted>",
            },
            gatt = snapshot.GattServices.Select(service => new
            {
                uuid = Redact(service.Uuid) ?? "<redacted>",
                attributeHandle = service.AttributeHandle,
                status = service.Status,
                source = service.Source,
                observedAt = service.ObservedAt,
                hResult = service.HResult,
                message = service.Message is null ? null : "<redacted>",
                characteristics = service.Characteristics.Select(characteristic => new
                {
                    uuid = Redact(characteristic.Uuid) ?? "<redacted>",
                    attributeHandle = characteristic.AttributeHandle,
                    properties = characteristic.Properties,
                    userDescription = characteristic.UserDescription is null ? null : "<redacted>",
                    status = characteristic.Status,
                    descriptorStatus = characteristic.DescriptorStatus,
                    source = characteristic.Source,
                    observedAt = characteristic.ObservedAt,
                    hResult = characteristic.HResult,
                    message = characteristic.Message is null ? null : "<redacted>",
                    descriptors = characteristic.Descriptors.Select(descriptor => new
                    {
                        uuid = Redact(descriptor.Uuid) ?? "<redacted>",
                        attributeHandle = descriptor.AttributeHandle,
                        source = descriptor.Source,
                        observedAt = descriptor.ObservedAt,
                        hResult = descriptor.HResult,
                        message = descriptor.Message is null ? null : "<redacted>",
                    }).ToArray(),
                }).ToArray(),
            }).ToArray(),
            rfcomm = snapshot.RfcommServices.Select(service => new
            {
                serviceId = Redact(service.ServiceId),
                knownName = service.KnownName is null ? null : "<redacted>",
                deviceId = Redact(service.DeviceId),
                source = service.Source,
                status = service.Status,
                observedAt = service.ObservedAt,
                hResult = service.HResult,
                message = service.Message is null ? null : "<redacted>",
            }).ToArray(),
            diagnostics = snapshot.Diagnostics.Select(diagnostic => new
            {
                operation = diagnostic.Operation,
                source = diagnostic.Source,
                status = diagnostic.Status,
                hResult = diagnostic.HResult,
                exceptionType = diagnostic.ExceptionType,
                message = diagnostic.Message is null ? null : "<redacted>",
                observedAt = diagnostic.ObservedAt,
            }).ToArray(),
        };
    }

    private static object SanitizeProperty(BluetoothObservedProperty property)
    {
        var sensitive = property.Key.Contains("name", StringComparison.OrdinalIgnoreCase)
            || property.Key.Contains("address", StringComparison.OrdinalIgnoreCase)
            || property.Key.Contains("container", StringComparison.OrdinalIgnoreCase)
            || property.Key.Contains("manufacturer", StringComparison.OrdinalIgnoreCase)
            || property.Key.Contains("model", StringComparison.OrdinalIgnoreCase)
            || property.Key.Contains("friendly", StringComparison.OrdinalIgnoreCase)
            || property.Key.EndsWith("Id", StringComparison.OrdinalIgnoreCase);
        return new
        {
            key = property.Key,
            type = property.Type,
            value = sensitive ? Redact(property.Value) : property.Value,
            source = property.Source,
            status = property.Status,
            observedAt = property.ObservedAt,
            hResult = property.HResult,
            message = property.Message is null ? null : "<redacted>",
        };
    }

    private static object SanitizeEndpoint(AudioEndpointModel endpoint)
    {
        return new
        {
            id = Redact(endpoint.Id),
            name = "<redacted>",
            direction = endpoint.Direction.ToString(),
            isDefault = endpoint.IsDefault,
            isActive = endpoint.IsActive,
            format = endpoint.Format,
        };
    }

    private static string? Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"redacted:{Convert.ToHexString(bytes)[..16].ToLowerInvariant()}";
    }

    private static void AddError(List<object> errors, string operation, Exception exception)
    {
        errors.Add(new
        {
            operation,
            exceptionType = exception.GetType().FullName,
            hResult = $"0x{exception.HResult:X8}",
        });
    }

    private static FileStream OpenUniqueArchiveFile(string directory, out string path)
    {
        var prefix = Path.Combine(directory, $"BTHaven-diagnostics-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        path = string.Empty;
        for (var index = 0; index < 100; index++)
        {
            path = index == 0 ? $"{prefix}.zip" : $"{prefix}-{index:00}.zip";
            try
            {
                return new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    options: FileOptions.SequentialScan);
            }
            catch (IOException) when (index < 99)
            {
            }
        }

        throw new IOException("Could not create a unique diagnostics archive path.");
    }

    private static void WriteEntry(System.IO.Compression.ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, System.IO.Compression.CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
