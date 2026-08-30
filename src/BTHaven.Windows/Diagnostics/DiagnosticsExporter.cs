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
    private const string SchemaVersion = "1";
    private readonly IBluetoothDeviceService deviceService;
    private readonly IAudioEndpointService endpointService;
    private readonly IWindowsDiagnosticLogger logger;

    public DiagnosticsExporter(
        IBluetoothDeviceService deviceService,
        IAudioEndpointService endpointService,
        IWindowsDiagnosticLogger? logger = null)
    {
        this.deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
        this.endpointService = endpointService ?? throw new ArgumentNullException(nameof(endpointService));
        this.logger = logger ?? NullDiagnosticLogger.Instance;
    }

    public async Task<string> ExportAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<object>();
        var devices = await TryGetDevicesAsync(errors, cancellationToken).ConfigureAwait(false);
        var renderEndpoints = await TryGetEndpointsAsync(AudioEndpointDirection.Render, errors, cancellationToken).ConfigureAwait(false);
        var captureEndpoints = await TryGetEndpointsAsync(AudioEndpointDirection.Capture, errors, cancellationToken).ConfigureAwait(false);
        var adapter = await TryGetAdapterAsync(errors, cancellationToken).ConfigureAwait(false);
        var hfp = await TryGetHfpAsync(errors, cancellationToken).ConfigureAwait(false);

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
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BTHaven",
            "Diagnostics");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"BTHaven-diagnostics-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        {
            WriteEntry(archive, "diagnostics.json", json);
            WriteEntry(archive, "README.txt", "BTHaven diagnostics export. Device identifiers and names are redacted. Audio buffers, phone numbers, caller IDs, and telemetry are not included.\r\n");
        }

        logger.Info("Diagnostics.ExportCompleted", new Dictionary<string, object?>
        {
            ["path"] = path,
            ["deviceCount"] = devices.Count,
            ["endpointCount"] = renderEndpoints.Count + captureEndpoints.Count,
            ["errorCount"] = errors.Count,
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

    private static void WriteEntry(System.IO.Compression.ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, System.IO.Compression.CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
