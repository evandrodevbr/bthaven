using System.Collections;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BTHaven.Windows.Diagnostics;

/// <summary>
/// Durable local JSONL diagnostics. Events are flushed before the call returns so a crash or
/// Bluetooth service restart leaves an actionable trail. Raw audio is never logged.
/// </summary>
public sealed class TraceDiagnosticLogger : IWindowsDiagnosticLogger
{
    private const string SchemaVersion = "3";
    private const long MaxFileBytes = 10 * 1024 * 1024;
    private const int MaxRetainedFiles = 40;
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly Regex SecretAssignment = new(
        @"(?i)(password|passwd|token|secret|authorization|cookie|private[_ -]?key|connection[_ -]?string)\s*[:=]\s*[^\s,;]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static TraceDiagnosticLogger Instance { get; } = new();

    private readonly object sync = new();
    private readonly string logDirectory;
    private readonly string sessionId = Guid.NewGuid().ToString("N");
    private long sequence;

    public TraceDiagnosticLogger(string? logDirectory = null)
    {
        this.logDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BTHaven",
            "Logs");

        try
        {
            Directory.CreateDirectory(this.logDirectory);
            Write("Logging.SessionStarted", new Dictionary<string, object?>
            {
                ["sessionId"] = sessionId,
                ["processId"] = Environment.ProcessId,
                ["process"] = Environment.ProcessPath,
                ["os"] = Environment.OSVersion.VersionString,
                ["framework"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                ["architecture"] = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            }, "info");
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.WriteLine($"BTHaven diagnostic logger initialization failed: {exception}");
        }
    }

    public string LogDirectory => logDirectory;

    public string? CurrentLogPath
    {
        get
        {
            try
            {
                return GetWritablePath(DateTime.UtcNow);
            }
            catch
            {
                return null;
            }
        }
    }

    public void Trace(string eventName, IReadOnlyDictionary<string, object?>? fields = null)
    {
        Write(eventName, fields, "trace");
    }

    public void Debug(string eventName, IReadOnlyDictionary<string, object?>? fields = null)
    {
        Write(eventName, fields, "debug");
    }

    public void Info(string eventName, IReadOnlyDictionary<string, object?>? fields = null)
    {
        Write(eventName, fields, "info");
    }

    public void Warning(string eventName, IReadOnlyDictionary<string, object?>? fields = null)
    {
        Write(eventName, fields, "warning");
    }

    public void Error(string eventName, Exception exception, IReadOnlyDictionary<string, object?>? fields = null)
    {
        WriteException(eventName, exception, fields, "error");
    }

    public void Critical(string eventName, Exception exception, IReadOnlyDictionary<string, object?>? fields = null)
    {
        WriteException(eventName, exception, fields, "critical");
    }

    public IReadOnlyList<string> ReadRecent(int maxLines = 2000, bool redactSensitive = false)
    {
        if (maxLines <= 0)
        {
            return [];
        }

        var lines = new Queue<string>(Math.Min(maxLines, 10_000));
        try
        {
            var files = Directory.EnumerateFiles(logDirectory, "bthaven-*.jsonl")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var path in files)
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096,
                    options: FileOptions.SequentialScan);
                using var reader = new StreamReader(stream, Utf8NoBom, detectEncodingFromByteOrderMarks: true);
                while (reader.ReadLine() is { } line)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    lines.Enqueue(redactSensitive ? RedactJsonLine(line) : line);
                    while (lines.Count > maxLines)
                    {
                        lines.Dequeue();
                    }
                }
            }
        }
        catch (Exception exception)
        {
            var fallback = JsonSerializer.Serialize(new
            {
                schemaVersion = SchemaVersion,
                timestampUtc = DateTimeOffset.UtcNow.ToString("O"),
                @event = "Logging.ReadRecentFailed",
                level = "error",
                data = new
                {
                    exceptionType = exception.GetType().FullName,
                    hResult = $"0x{exception.HResult:X8}",
                },
            }, JsonOptions);
            lines.Enqueue(fallback);
        }

        return lines.ToArray();
    }

    public void Flush()
    {
        // Events are opened, written, flushed and closed synchronously. This method documents
        // that durability contract for lifecycle callers.
    }

    private void WriteException(
        string eventName,
        Exception exception,
        IReadOnlyDictionary<string, object?>? fields,
        string level)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var errorFields = new Dictionary<string, object?>(fields ?? new Dictionary<string, object?>())
        {
            ["exceptionType"] = exception.GetType().FullName,
            ["message"] = exception.Message,
            ["hResult"] = $"0x{exception.HResult:X8}",
            ["source"] = exception.Source,
            ["targetSite"] = exception.TargetSite?.ToString(),
            ["stackTrace"] = exception.ToString(),
        };
        Write(eventName, errorFields, level);
    }

    private void Write(
        string eventName,
        IReadOnlyDictionary<string, object?>? fields,
        string level)
    {
        var safeEventName = string.IsNullOrWhiteSpace(eventName) ? "UnnamedEvent" : eventName;
        var normalizedFields = NormalizeFields(fields);
        var payload = new Dictionary<string, object?>
        {
            ["schemaVersion"] = SchemaVersion,
            ["timestampUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["sequence"] = Interlocked.Increment(ref sequence),
            ["sessionId"] = sessionId,
            ["processId"] = Environment.ProcessId,
            ["threadId"] = Environment.CurrentManagedThreadId,
            ["event"] = safeEventName,
            ["level"] = level,
            ["data"] = normalizedFields,
        };

        string line;
        try
        {
            line = JsonSerializer.Serialize(payload, JsonOptions);
        }
        catch (Exception exception)
        {
            line = JsonSerializer.Serialize(new
            {
                schemaVersion = SchemaVersion,
                timestampUtc = DateTimeOffset.UtcNow.ToString("O"),
                sequence = Interlocked.Increment(ref sequence),
                sessionId,
                processId = Environment.ProcessId,
                threadId = Environment.CurrentManagedThreadId,
                @event = "Logging.SerializationFailed",
                level = "error",
                data = new
                {
                    originalEvent = safeEventName,
                    exceptionType = exception.GetType().FullName,
                    hResult = $"0x{exception.HResult:X8}",
                },
            }, JsonOptions);
        }

        try
        {
            lock (sync)
            {
                Directory.CreateDirectory(logDirectory);
                var path = GetWritablePath(DateTime.UtcNow);
                using var stream = new FileStream(
                    path,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096,
                    options: FileOptions.WriteThrough);
                using var writer = new StreamWriter(stream, Utf8NoBom);
                writer.WriteLine(line);
                writer.Flush();
                stream.Flush(flushToDisk: true);
                PruneOldFiles();
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.WriteLine($"BTHaven diagnostic logger write failed: {exception}");
        }

        System.Diagnostics.Trace.WriteLine(line);
    }

    private string GetWritablePath(DateTime utcNow)
    {
        Directory.CreateDirectory(logDirectory);
        var prefix = Path.Combine(logDirectory, $"bthaven-{utcNow:yyyyMMdd}");
        var path = $"{prefix}.jsonl";
        if (!File.Exists(path) || new FileInfo(path).Length < MaxFileBytes)
        {
            return path;
        }

        for (var index = 1; index < 100; index++)
        {
            path = $"{prefix}-{index:00}.jsonl";
            if (!File.Exists(path) || new FileInfo(path).Length < MaxFileBytes)
            {
                return path;
            }
        }

        return $"{prefix}-{DateTime.UtcNow:HHmmssfff}.jsonl";
    }

    private void PruneOldFiles()
    {
        try
        {
            var oldFiles = Directory.EnumerateFiles(logDirectory, "bthaven-*.jsonl")
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                .Skip(MaxRetainedFiles)
                .ToArray();
            foreach (var oldFile in oldFiles)
            {
                File.Delete(oldFile);
            }
        }
        catch
        {
            // Logging must never fail the operation it is observing.
        }
    }

    private static Dictionary<string, object?> NormalizeFields(IReadOnlyDictionary<string, object?>? fields)
    {
        var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (fields is null)
        {
            return normalized;
        }

        foreach (var pair in fields)
        {
            normalized[pair.Key] = NormalizeValue(pair.Key, pair.Value);
        }

        return normalized;
    }

    private static object? NormalizeValue(string key, object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (IsSecretKey(key))
        {
            return "[REDACTED]";
        }

        if (value is byte[] bytes)
        {
            return new { type = "binary", length = bytes.Length, content = "[NOT_LOGGED]" };
        }

        if (value is ReadOnlyMemory<byte> memory)
        {
            return new { type = "binary", length = memory.Length, content = "[NOT_LOGGED]" };
        }

        if (value is Exception exception)
        {
            return new
            {
                exceptionType = exception.GetType().FullName,
                message = RedactText(exception.Message),
                hResult = $"0x{exception.HResult:X8}",
                stackTrace = exception.ToString(),
            };
        }

        if (value is string text)
        {
            return RedactText(text);
        }

        if (value is IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in dictionary)
            {
                var entryKey = entry.Key?.ToString() ?? "unknown";
                result[entryKey] = NormalizeValue(entryKey, entry.Value);
            }

            return result;
        }

        if (value is IEnumerable enumerable)
        {
            var result = new List<object?>();
            foreach (var item in enumerable)
            {
                result.Add(NormalizeValue(key, item));
            }

            return result;
        }

        return value.GetType().IsEnum ? value.ToString() : value;
    }

    private static string RedactText(string text)
    {
        return SecretAssignment.Replace(text, "$1=[REDACTED]");
    }

    private static bool IsSecretKey(string key)
    {
        var compact = key.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        return compact.Contains("password", StringComparison.Ordinal)
            || compact.Contains("passwd", StringComparison.Ordinal)
            || compact.Contains("token", StringComparison.Ordinal)
            || compact.Contains("secret", StringComparison.Ordinal)
            || compact.Contains("authorization", StringComparison.Ordinal)
            || compact.Contains("cookie", StringComparison.Ordinal)
            || compact.Contains("privatekey", StringComparison.Ordinal)
            || compact.Contains("connectionstring", StringComparison.Ordinal)
            || compact.Equals("apikey", StringComparison.Ordinal);
    }

    private static string RedactJsonLine(string line)
    {
        try
        {
            var node = JsonNode.Parse(line);
            if (node is null)
            {
                return "[REDACTED_INVALID_LOG_LINE]";
            }

            RedactExportNode(node);
            return node.ToJsonString(JsonOptions);
        }
        catch
        {
            return "[REDACTED_INVALID_LOG_LINE]";
        }
    }

    private static void RedactExportNode(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                if (IsExportSensitiveKey(property.Key))
                {
                    jsonObject[property.Key] = property.Value is JsonValue value && value.TryGetValue<string>(out var text)
                        ? RedactIdentifier(text)
                        : "[REDACTED]";
                    continue;
                }

                if (property.Value is not null)
                {
                    RedactExportNode(property.Value);
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                if (item is not null)
                {
                    RedactExportNode(item);
                }
            }
        }
    }

    private static bool IsExportSensitiveKey(string key)
    {
        if (IsSecretKey(key))
        {
            return true;
        }

        var compact = key.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        return compact is "id" or "deviceid" or "containerid" or "address" or "name"
            or "friendlyname" or "manufacturer" or "model" or "path" or "process"
            or "commandline" or "username" or "user";
    }

    private static string RedactIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "[REDACTED]";
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"redacted:{Convert.ToHexString(bytes)[..16].ToLowerInvariant()}";
    }
}
