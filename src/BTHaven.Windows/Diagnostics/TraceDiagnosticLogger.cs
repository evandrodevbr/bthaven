using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BTHaven.Windows.Diagnostics;

public sealed class TraceDiagnosticLogger : IWindowsDiagnosticLogger
{
    public static TraceDiagnosticLogger Instance { get; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private TraceDiagnosticLogger() { }

    public void Info(string eventName, IReadOnlyDictionary<string, object?> fields)
    {
        Write(eventName, fields, null);
    }

    public void Error(string eventName, Exception exception, IReadOnlyDictionary<string, object?>? fields = null)
    {
        var errorFields = new Dictionary<string, object?>(fields ?? new Dictionary<string, object?>())
        {
            ["exceptionType"] = exception.GetType().FullName,
            ["message"] = exception.Message,
            ["hResult"] = $"0x{exception.HResult:X8}",
        };
        Write(eventName, errorFields, "error");
    }

    private static void Write(string eventName, IReadOnlyDictionary<string, object?> fields, string? level)
    {
        var payload = new
        {
            timestampUtc = DateTimeOffset.UtcNow.ToString("O"),
            @event = eventName,
            level,
            data = fields,
        };
        Trace.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
