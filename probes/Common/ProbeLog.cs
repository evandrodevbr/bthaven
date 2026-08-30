using System.Text.Json;
using System.Text.Json.Serialization;

namespace BTHaven.Probes.Common;

public static class ProbeLog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Header(string probeName)
    {
        Event("Probe.Started", new
        {
            probe = probeName,
            process = Environment.ProcessPath,
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            commandLine = Environment.CommandLine,
        });
    }

    public static void Event(string eventName, object? data = null)
    {
        var payload = new
        {
            timestampUtc = DateTimeOffset.UtcNow.ToString("O"),
            @event = eventName,
            data,
        };

        Console.WriteLine(JsonSerializer.Serialize(payload, Options));
    }

    public static void Error(string operation, Exception exception, object? data = null)
    {
        Event("Probe.Error", new
        {
            operation,
            exceptionType = exception.GetType().FullName,
            message = exception.Message,
            hResult = $"0x{exception.HResult:X8}",
            data,
        });
    }
}
