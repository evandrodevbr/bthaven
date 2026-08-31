namespace BTHaven.Windows.Diagnostics;

public sealed class NullDiagnosticLogger : IWindowsDiagnosticLogger
{
    public static NullDiagnosticLogger Instance { get; } = new();

    private NullDiagnosticLogger() { }

    public void Trace(string eventName, IReadOnlyDictionary<string, object?>? fields = null) { }
    public void Debug(string eventName, IReadOnlyDictionary<string, object?>? fields = null) { }
    public void Info(string eventName, IReadOnlyDictionary<string, object?>? fields = null) { }
    public void Warning(string eventName, IReadOnlyDictionary<string, object?>? fields = null) { }
    public void Error(string eventName, Exception exception, IReadOnlyDictionary<string, object?>? fields = null) { }
    public void Critical(string eventName, Exception exception, IReadOnlyDictionary<string, object?>? fields = null) { }
    public IReadOnlyList<string> ReadRecent(int maxLines = 2000, bool redactSensitive = false) => [];
    public void Flush() { }
}
