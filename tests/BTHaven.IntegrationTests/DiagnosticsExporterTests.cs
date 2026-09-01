using System.IO.Compression;
using System.Text;
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

    private sealed class StubDeviceService : IBluetoothDeviceService
    {
        public Task<IReadOnlyList<BluetoothDeviceModel>> GetDevicesAsync(
            BluetoothDeviceFilter filter = BluetoothDeviceFilter.Connected,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BluetoothDeviceModel>>([]);

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

