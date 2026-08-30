using System.IO.Compression;
using System.Text;
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
        var exporter = new DiagnosticsExporter(devices, new AudioEndpointManager(NullDiagnosticLogger.Instance));

        var path = await exporter.ExportAsync();
        try
        {
            Assert.True(File.Exists(path));
            using var archive = ZipFile.OpenRead(path);
            Assert.Contains(archive.Entries, entry => entry.FullName == "diagnostics.json");
            Assert.Contains(archive.Entries, entry => entry.FullName == "README.txt");
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("audio", StringComparison.OrdinalIgnoreCase));

            var jsonEntry = archive.GetEntry("diagnostics.json");
            Assert.NotNull(jsonEntry);
            using var reader = new StreamReader(jsonEntry!.Open(), Encoding.UTF8);
            var json = await reader.ReadToEndAsync();
            Assert.Contains("redacted", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("audioBuffer", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
