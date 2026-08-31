using System.Text.Json;
using BTHaven.Windows.Diagnostics;

namespace BTHaven.IntegrationTests;

public sealed class TraceDiagnosticLoggerTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void Persists_all_levels_and_exception_details_as_structured_jsonl()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bthaven-logger-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var logger = new TraceDiagnosticLogger(directory);
            logger.Trace("Test.Trace", new Dictionary<string, object?> { ["value"] = 1 });
            logger.Debug("Test.Debug");
            logger.Info("Test.Info", new Dictionary<string, object?> { ["deviceId"] = "secret-device-id" });
            logger.Warning("Test.Warning");
            logger.Error("Test.Error", new InvalidOperationException("boom"));
            logger.Critical("Test.Critical", new InvalidOperationException("critical"));

            var lines = logger.ReadRecent(100);

            Assert.True(lines.Count >= 7);
            foreach (var line in lines)
            {
                using var document = JsonDocument.Parse(line);
                Assert.True(document.RootElement.TryGetProperty("timestampUtc", out _));
                Assert.True(document.RootElement.TryGetProperty("event", out _));
                Assert.True(document.RootElement.TryGetProperty("level", out _));
                Assert.True(document.RootElement.TryGetProperty("data", out _));
            }

            Assert.Contains(lines, line => line.Contains("Test.Trace", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.Contains("Test.Critical", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.Contains("InvalidOperationException", StringComparison.Ordinal));

            var redacted = logger.ReadRecent(100, redactSensitive: true);
            Assert.DoesNotContain(redacted, line => line.Contains("secret-device-id", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
