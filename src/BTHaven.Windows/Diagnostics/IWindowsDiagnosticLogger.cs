namespace BTHaven.Windows.Diagnostics;

public interface IWindowsDiagnosticLogger
{
    void Trace(string eventName, IReadOnlyDictionary<string, object?>? fields = null);
    void Debug(string eventName, IReadOnlyDictionary<string, object?>? fields = null);
    void Info(string eventName, IReadOnlyDictionary<string, object?>? fields = null);
    void Warning(string eventName, IReadOnlyDictionary<string, object?>? fields = null);
    void Error(string eventName, Exception exception, IReadOnlyDictionary<string, object?>? fields = null);
    void Critical(string eventName, Exception exception, IReadOnlyDictionary<string, object?>? fields = null);
    IReadOnlyList<string> ReadRecent(int maxLines = 2000, bool redactSensitive = false);
    void Flush();
}
