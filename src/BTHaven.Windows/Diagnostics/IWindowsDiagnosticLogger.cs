namespace BTHaven.Windows.Diagnostics;

public interface IWindowsDiagnosticLogger
{
    void Info(string eventName, IReadOnlyDictionary<string, object?> fields);
    void Error(string eventName, Exception exception, IReadOnlyDictionary<string, object?>? fields = null);
}
