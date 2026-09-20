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

    [Fact]
    [Trait("Category", "Integration")]
    public void Redacted_exception_logs_hide_device_identity_but_keep_type_and_hresult()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bthaven-logger-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        const string deviceId = "device-sensitive-123";
        const string deviceName = "Living Room Headset";
        try
        {
            var logger = new TraceDiagnosticLogger(directory);
            var exception = new InvalidOperationException($"deviceId={deviceId}; name={deviceName}");

            logger.Error("Test.SensitiveError", exception);

            var redacted = logger.ReadRecent(100, redactSensitive: true);
            var line = Assert.Single(redacted, item => item.Contains("Test.SensitiveError", StringComparison.Ordinal));

            Assert.DoesNotContain(deviceId, line, StringComparison.Ordinal);
            Assert.DoesNotContain(deviceName, line, StringComparison.Ordinal);
            Assert.Contains("[REDACTED]", line, StringComparison.Ordinal);
            Assert.Contains("InvalidOperationException", line, StringComparison.Ordinal);
            Assert.Contains("hResult", line, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Redacted_reads_use_fresh_pseudonyms_without_changing_local_logs_or_service_metadata()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bthaven-logger-tests", Guid.NewGuid().ToString("N"));
        const string deviceId = "private-device-id";
        const string serviceUuid = "0000180f-0000-1000-8000-00805f9b34fb";
        try
        {
            var logger = new TraceDiagnosticLogger(directory);
            logger.Info("Test.Pseudonyms", new Dictionary<string, object?>
            {
                ["deviceId"] = deviceId,
                ["deviceUuid"] = deviceId,
                ["nested"] = new[] { new Dictionary<string, object?> { ["endpointId"] = deviceId } },
                ["serviceUuid"] = serviceUuid,
                ["serviceId"] = serviceUuid,
                ["hResult"] = "0x80070005",
                ["status"] = "Success",
            });
            var raw = Assert.Single(logger.ReadRecent(), line => line.Contains("Test.Pseudonyms", StringComparison.Ordinal));
            using var rawDocument = JsonDocument.Parse(raw);
            var pseudonyms = new List<string>();
            for (var index = 0; index < 2; index++)
            {
                var line = Assert.Single(logger.ReadRecent(redactSensitive: true), item => item.Contains("Test.Pseudonyms", StringComparison.Ordinal));
                Assert.DoesNotContain(deviceId, line, StringComparison.Ordinal);
                using var document = JsonDocument.Parse(line);
                var data = document.RootElement.GetProperty("data");
                var pseudonym = data.GetProperty("deviceId").GetString()!;
                Assert.Equal(pseudonym, data.GetProperty("nested")[0].GetProperty("endpointId").GetString());
                Assert.Equal(pseudonym, data.GetProperty("deviceUuid").GetString());
                Assert.Equal(serviceUuid, data.GetProperty("serviceUuid").GetString());
                Assert.Equal(serviceUuid, data.GetProperty("serviceId").GetString());
                Assert.Equal("0x80070005", data.GetProperty("hResult").GetString());
                Assert.Equal("Success", data.GetProperty("status").GetString());
                foreach (var field in new[] { "timestampUtc", "sequence", "processId", "threadId", "level", "event" })
                {
                    Assert.Equal(rawDocument.RootElement.GetProperty(field).ToString(), document.RootElement.GetProperty(field).ToString());
                }
                pseudonyms.Add(pseudonym);
            }
            Assert.NotEqual(pseudonyms[0], pseudonyms[1]);
            Assert.Equal(raw, Assert.Single(logger.ReadRecent(), line => line.Contains("Test.Pseudonyms", StringComparison.Ordinal)));
            Assert.Contains(deviceId, raw, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Redacted_reads_do_not_pass_through_nonobject_or_malformed_log_records()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bthaven-logger-tests", Guid.NewGuid().ToString("N"));
        const string identity = "private-corrupted-record";
        try
        {
            var logger = new TraceDiagnosticLogger(directory);
            File.AppendAllLines(logger.CurrentLogPath!, [identity, $"\"{identity}\"", $"[\"{identity}\"]"]);

            var lines = logger.ReadRecent(redactSensitive: true);

            Assert.DoesNotContain(lines, line => line.Contains(identity, StringComparison.Ordinal));
            Assert.Contains(lines, line => line.Contains("Logging.SessionStarted", StringComparison.Ordinal));
            Assert.Contains(logger.ReadRecent(), line => line.Contains(identity, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

}
