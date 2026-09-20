namespace BTHaven.Windows.Diagnostics;

/// <summary>Lives only for one export/read; neither input identities nor the map are serialized.</summary>
internal sealed class DiagnosticPseudonymMap
{
    private readonly string exportToken = Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, string> identifiers = new(StringComparer.Ordinal);

    internal string? Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!identifiers.TryGetValue(value, out var pseudonym))
        {
            pseudonym = $"redacted:{exportToken}:{identifiers.Count + 1}";
            identifiers.Add(value, pseudonym);
        }

        return pseudonym;
    }
}
