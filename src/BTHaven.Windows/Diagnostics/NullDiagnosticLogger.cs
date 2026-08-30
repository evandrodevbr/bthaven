namespace BTHaven.Windows.Diagnostics;

public sealed class NullDiagnosticLogger : IWindowsDiagnosticLogger
{
    public static NullDiagnosticLogger Instance { get; } = new();

    private NullDiagnosticLogger() { }

    public void Info(string eventName, IReadOnlyDictionary<string, object?> fields) { }

    public void Error(string eventName, Exception exception, IReadOnlyDictionary<string, object?>? fields = null) { }
}
