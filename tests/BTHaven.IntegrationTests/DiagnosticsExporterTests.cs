using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BTHaven.Core.Audio;
using BTHaven.Core.Contracts;
using BTHaven.Core.Devices;
using BTHaven.Windows.Audio;
using BTHaven.Windows.Bluetooth;
using BTHaven.Windows.Diagnostics;

namespace BTHaven.IntegrationTests;


public sealed class DiagnosticsExporterTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Export_creates_a_local_redacted_zip_without_audio_payloads()
    {
        await using var devices = new BluetoothDeviceManager();
        var root = Path.Combine(Path.GetTempPath(), "bthaven-exporter-tests", Guid.NewGuid().ToString("N"));
        var exportDirectory = Path.Combine(root, "exports");
        Directory.CreateDirectory(root);
        try
        {
            var exporter = new DiagnosticsExporter(
                devices,
                new AudioEndpointManager(NullDiagnosticLogger.Instance),
                NullDiagnosticLogger.Instance,
                new BluetoothDeviceInspector(NullDiagnosticLogger.Instance),
                outputDirectory: exportDirectory);

            var path = await exporter.ExportAsync();
            try
            {
                Assert.True(File.Exists(path));
                using var archive = ZipFile.OpenRead(path);
                Assert.Contains(archive.Entries, entry => entry.FullName == "diagnostics.json");
                Assert.Contains(archive.Entries, entry => entry.FullName == "logs.jsonl");
                Assert.Contains(archive.Entries, entry => entry.FullName == "README.txt");
                Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("audio", StringComparison.OrdinalIgnoreCase));

                var jsonEntry = archive.GetEntry("diagnostics.json");
                Assert.NotNull(jsonEntry);
                using var reader = new StreamReader(jsonEntry!.Open(), Encoding.UTF8);
                var json = await reader.ReadToEndAsync();
                Assert.Contains("redacted", json, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("\"inspections\"", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("audioBuffer", json, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                File.Delete(path);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Export_uses_injected_directory_and_allocates_distinct_paths_concurrently()
    {
        var root = Path.Combine(Path.GetTempPath(), "bthaven-exporter-tests", Guid.NewGuid().ToString("N"));
        var exportDirectory = Path.Combine(root, "exports");
        var logDirectory = Path.Combine(root, "logs");
        Directory.CreateDirectory(root);
        try
        {
            var exporter = new DiagnosticsExporter(
                new StubDeviceService(),
                new StubEndpointService(),
                new TraceDiagnosticLogger(logDirectory),
                outputDirectory: exportDirectory);

            var paths = await Task.WhenAll(exporter.ExportAsync(), exporter.ExportAsync());

            Assert.Equal(2, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(paths, path =>
            {
                Assert.True(File.Exists(path));
                Assert.Equal(
                    Path.GetFullPath(exportDirectory),
                    Path.GetDirectoryName(Path.GetFullPath(path)),
                    StringComparer.OrdinalIgnoreCase);
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Export_redacts_exception_message_and_stack_trace_but_keeps_type_and_hresult()
    {
        var root = Path.Combine(Path.GetTempPath(), "bthaven-exporter-tests", Guid.NewGuid().ToString("N"));
        var exportDirectory = Path.Combine(root, "exports");
        var logDirectory = Path.Combine(root, "logs");
        Directory.CreateDirectory(root);
        const string deviceId = "device-sensitive-456";
        const string deviceName = "Kitchen Speaker";
        try
        {
            var logger = new TraceDiagnosticLogger(logDirectory);
            logger.Error("Test.SensitiveError", new InvalidOperationException($"deviceId={deviceId}; name={deviceName}"));
            var exporter = new DiagnosticsExporter(
                new StubDeviceService(),
                new StubEndpointService(),
                logger,
                outputDirectory: exportDirectory);

            var path = await exporter.ExportAsync();
            try
            {
                using var archive = ZipFile.OpenRead(path);
                var logsEntry = archive.GetEntry("logs.jsonl");
                Assert.NotNull(logsEntry);
                using var reader = new StreamReader(logsEntry!.Open(), Encoding.UTF8);
                var logs = await reader.ReadToEndAsync();

                Assert.DoesNotContain(deviceId, logs, StringComparison.Ordinal);
                Assert.DoesNotContain(deviceName, logs, StringComparison.Ordinal);
                Assert.Contains("[REDACTED]", logs, StringComparison.Ordinal);
                Assert.Contains("InvalidOperationException", logs, StringComparison.Ordinal);
                Assert.Contains("hResult", logs, StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(path);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Export_and_read_redact_endpoint_identity_fields_but_keep_exception_metadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "bthaven-exporter-tests", Guid.NewGuid().ToString("N"));
        var exportDirectory = Path.Combine(root, "exports");
        var logDirectory = Path.Combine(root, "logs");
        Directory.CreateDirectory(root);
        var rawValues = new[]
        {
            "raw-id",
            "raw-logical-key",
            "raw-logical-device-id",
            "raw-selector",
            "raw-endpoint-id",
            "raw-ble-id",
            "raw-transport-device-id",
            "raw-a2dp-device-id",
            "raw-device-instance-id",
        };
        try
        {
            var logger = new TraceDiagnosticLogger(logDirectory);
            logger.Info("Test.SensitiveEndpointFields", new Dictionary<string, object?>
            {
                ["id"] = rawValues[0],
                ["logicalKey"] = rawValues[1],
                ["logicalDeviceId"] = rawValues[2],
                ["selector"] = rawValues[3],
                ["endpointId"] = rawValues[4],
                ["bleId"] = rawValues[5],
                ["transportDeviceId"] = rawValues[6],
                ["a2dpDeviceId"] = rawValues[7],
                ["deviceInstanceId"] = rawValues[8],
                ["exceptionType"] = "InvalidOperationException",
                ["hResult"] = "0x80070005",
            });

            var readLine = Assert.Single(
                logger.ReadRecent(100, redactSensitive: true),
                line => line.Contains("Test.SensitiveEndpointFields", StringComparison.Ordinal));
            foreach (var rawValue in rawValues)
            {
                Assert.DoesNotContain(rawValue, readLine, StringComparison.Ordinal);
            }

            Assert.Contains("InvalidOperationException", readLine, StringComparison.Ordinal);
            Assert.Contains("0x80070005", readLine, StringComparison.Ordinal);

            var exporter = new DiagnosticsExporter(
                new StubDeviceService(),
                new StubEndpointService(),
                logger,
                outputDirectory: exportDirectory);
            var path = await exporter.ExportAsync();
            try
            {
                using var archive = ZipFile.OpenRead(path);
                var logsEntry = archive.GetEntry("logs.jsonl");
                Assert.NotNull(logsEntry);
                using var reader = new StreamReader(logsEntry!.Open(), Encoding.UTF8);
                var logs = await reader.ReadToEndAsync();

                foreach (var rawValue in rawValues)
                {
                    Assert.DoesNotContain(rawValue, logs, StringComparison.Ordinal);
                }

                Assert.Contains("InvalidOperationException", logs, StringComparison.Ordinal);
                Assert.Contains("0x80070005", logs, StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(path);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Export_preserves_service_identifiers_but_redacts_device_identity_and_free_text()
    {
        var root = Path.Combine(Path.GetTempPath(), "bthaven-exporter-tests", Guid.NewGuid().ToString("N"));
        var exportDirectory = Path.Combine(root, "exports");
        Directory.CreateDirectory(root);
        const string gattServiceUuid = "0000180f-0000-1000-8000-00805f9b34fb";
        const string gattCharacteristicUuid = "00002a19-0000-1000-8000-00805f9b34fb";
        const string gattDescriptorUuid = "00002901-0000-1000-8000-00805f9b34fb";
        const string userDescription = "gatt-user-description-sentinel";
        const string rfcommName = "rfcomm-name-sentinel";
        var device = new BluetoothDeviceModel
        {
            Id = "device-sentinel",
            Name = "Phone",
            Transport = BluetoothTransport.LowEnergy,
            Endpoints =
            [
                new BluetoothEndpointReference
                {
                    Id = "endpoint-sentinel",
                    Transport = BluetoothTransport.LowEnergy,
                },
            ],
        };
        var snapshot = new BluetoothDeviceInspectionSnapshot
        {
            DeviceId = device.Id,
            Name = device.Name,
            Transport = device.Transport,
            GattServices =
            [
                new BluetoothGattServiceInspection
                {
                    Uuid = gattServiceUuid,
                    AttributeHandle = 17,
                    Status = "Success",
                    HResult = "0x80070005",
                    Characteristics =
                    [
                        new BluetoothGattCharacteristicInspection
                        {
                            Uuid = gattCharacteristicUuid,
                            AttributeHandle = 23,
                            UserDescription = userDescription,
                            Status = "Success",
                            HResult = "0x80070006",
                            Descriptors =
                            [
                                new BluetoothGattDescriptorInspection
                                {
                                    Uuid = gattDescriptorUuid,
                                    AttributeHandle = 29,
                                },
                            ],
                        },
                    ],
                },
            ],
            RfcommServices =
            [
                new BluetoothRfcommServiceInspection
                {
                    ServiceId = "rfcomm-id-sentinel",
                    KnownName = rfcommName,
                    DeviceId = "rfcomm-device-sentinel",
                    Status = "Success",
                    HResult = "0x80070007",
                },
            ],
            Diagnostics =
            [
                new BluetoothInspectionDiagnostic
                {
                    Operation = "inspection.sentinel",
                    Status = "Failed",
                    HResult = "0x80070008",
                    ExceptionType = "SentinelException",
                    Message = "diagnostic-message-sentinel",
                },
            ],
        };
        try
        {
            var exporter = new DiagnosticsExporter(
                new StubDeviceService([device]),
                new StubEndpointService(),
                NullDiagnosticLogger.Instance,
                new StubInspector(snapshot),
                outputDirectory: exportDirectory);

            var path = await exporter.ExportAsync();
            try
            {
                using var archive = ZipFile.OpenRead(path);
                var jsonEntry = archive.GetEntry("diagnostics.json");
                Assert.NotNull(jsonEntry);
                using var reader = new StreamReader(jsonEntry!.Open(), Encoding.UTF8);
                var json = await reader.ReadToEndAsync();

                foreach (var sentinel in new[]
                {
                    userDescription,
                    rfcommName,
                    "rfcomm-device-sentinel",
                })
                {
                    Assert.DoesNotContain(sentinel, json, StringComparison.Ordinal);
                }

                using var document = JsonDocument.Parse(json);
                var inspection = document.RootElement.GetProperty("inspections")[0];
                var service = inspection.GetProperty("gatt")[0];
                Assert.Equal(gattServiceUuid, service.GetProperty("uuid").GetString());
                var characteristic = service.GetProperty("characteristics")[0];
                Assert.Equal(gattCharacteristicUuid, characteristic.GetProperty("uuid").GetString());
                Assert.Equal(gattDescriptorUuid, characteristic.GetProperty("descriptors")[0].GetProperty("uuid").GetString());
                Assert.Equal("rfcomm-id-sentinel", inspection.GetProperty("rfcomm")[0].GetProperty("serviceId").GetString());

                Assert.Contains("\"attributeHandle\": 17", json, StringComparison.Ordinal);
                Assert.Contains("\"status\": \"Success\"", json, StringComparison.Ordinal);
                Assert.Contains("\"hResult\": \"0x80070005\"", json, StringComparison.Ordinal);
                Assert.Contains("\"exceptionType\": \"SentinelException\"", json, StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(path);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Exports_share_identity_pseudonyms_with_logs_only_within_each_archive()
    {
        var root = Path.Combine(Path.GetTempPath(), "bthaven-exporter-tests", Guid.NewGuid().ToString("N"));
        var device = new BluetoothDeviceModel
        {
            Id = "private-device-identity",
            Name = "Private Phone Name",
            ContainerId = "private-container-identity",
            Transport = BluetoothTransport.LowEnergy,
        };
        const string endpointId = "private-endpoint-identity";
        var snapshot = new BluetoothDeviceInspectionSnapshot
        {
            DeviceId = device.Id,
            Name = device.Name,
            ContainerId = device.ContainerId,
            Endpoints =
            [
                new BluetoothEndpointInspection
                {
                    Id = endpointId,
                    Kind = "AssociationEndpoint",
                    ContainerId = device.ContainerId,
                    Properties =
                    [
                        new BluetoothObservedProperty { Key = "System.Devices.DeviceInstanceId", Type = "String", Value = device.Id },
                    ],
                },
            ],
        };
        try
        {
            var logger = new TraceDiagnosticLogger(Path.Combine(root, "logs"));
            logger.Info("Test.Identity", new Dictionary<string, object?>
            {
                ["deviceId"] = device.Id,
                ["containerId"] = device.ContainerId,
                ["endpointId"] = endpointId,
                ["name"] = device.Name,
                ["status"] = "Connected",
                ["count"] = 2,
            });
            var exporter = new DiagnosticsExporter(
                new StubDeviceService([device]), new StubEndpointService(), logger,
                new StubInspector(snapshot), Path.Combine(root, "exports"));
            var paths = await Task.WhenAll(exporter.ExportAsync(), exporter.ExportAsync());
            var identities = new List<string[]>();
            foreach (var path in paths)
            {
                using var archive = ZipFile.OpenRead(path);
                foreach (var entry in archive.Entries)
                {
                    using var entryReader = new StreamReader(entry.Open());
                    var content = await entryReader.ReadToEndAsync();
                    foreach (var identity in new[] { device.Id, device.ContainerId!, endpointId, device.Name })
                    {
                        Assert.DoesNotContain(identity, content, StringComparison.Ordinal);
                    }
                }

                using var reader = new StreamReader(archive.GetEntry("diagnostics.json")!.Open());
                using var document = JsonDocument.Parse(await reader.ReadToEndAsync());
                var exportedDevice = document.RootElement.GetProperty("devices")[0];
                var inspection = document.RootElement.GetProperty("inspections")[0];
                var exportedId = exportedDevice.GetProperty("id").GetString()!;
                var exportedContainer = exportedDevice.GetProperty("containerId").GetString()!;
                var exportedEndpoint = inspection.GetProperty("endpoints")[0].GetProperty("id").GetString()!;
                Assert.Equal(exportedId, inspection.GetProperty("deviceId").GetString());
                Assert.Equal(exportedContainer, inspection.GetProperty("containerId").GetString());
                Assert.Equal(exportedId, inspection.GetProperty("endpoints")[0].GetProperty("properties")[0].GetProperty("value").GetString());

                using var logReader = new StreamReader(archive.GetEntry("logs.jsonl")!.Open());
                var logLines = (await logReader.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
                using var log = JsonDocument.Parse(Assert.Single(logLines, line => line.Contains("Test.Identity", StringComparison.Ordinal)));
                var data = log.RootElement.GetProperty("data");
                Assert.Equal(exportedId, data.GetProperty("deviceId").GetString());
                Assert.Equal(exportedContainer, data.GetProperty("containerId").GetString());
                Assert.Equal(exportedEndpoint, data.GetProperty("endpointId").GetString());
                Assert.Equal("Connected", data.GetProperty("status").GetString());
                Assert.Equal(2, data.GetProperty("count").GetInt32());
                identities.Add([exportedId, exportedContainer, exportedEndpoint, log.RootElement.GetProperty("sessionId").GetString()!]);
                Assert.Equal(4, identities[^1].Distinct(StringComparer.Ordinal).Count());
            }

            Assert.Empty(identities[0].Intersect(identities[1], StringComparer.Ordinal));
            Assert.Contains(logger.ReadRecent(), line => line.Contains(device.Id, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubInspector(BluetoothDeviceInspectionSnapshot snapshot) : IBluetoothDeviceInspector
    {
        public Task<BluetoothDeviceInspectionSnapshot> InspectAsync(
            BluetoothDeviceModel device,
            CancellationToken cancellationToken = default)
            => Task.FromResult(snapshot);
    }

    private sealed class StubDeviceService(IReadOnlyList<BluetoothDeviceModel>? devices = null) : IBluetoothDeviceService
    {
        private readonly IReadOnlyList<BluetoothDeviceModel> devices = devices ?? [];

        public Task<IReadOnlyList<BluetoothDeviceModel>> GetDevicesAsync(
            BluetoothDeviceFilter filter = BluetoothDeviceFilter.Connected,
            CancellationToken cancellationToken = default)
            => Task.FromResult(devices);

        public IAsyncEnumerable<BluetoothDeviceChange> WatchAsync(CancellationToken cancellationToken = default)
            => EmptyChanges();

        private static async IAsyncEnumerable<BluetoothDeviceChange> EmptyChanges()
        {
            yield break;
        }
    }

    private sealed class StubEndpointService : IAudioEndpointService
    {
        public Task<IReadOnlyList<AudioEndpointModel>> GetEndpointsAsync(
            AudioEndpointDirection direction,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AudioEndpointModel>>([]);
    }
}

