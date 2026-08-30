namespace BTHaven.Probes.Common;

public sealed class ProbeArguments
{
    private readonly Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase);

    private ProbeArguments() { }

    public static ProbeArguments Parse(string[] args)
    {
        var result = new ProbeArguments();
        for (var index = 0; index < args.Length; index++)
        {
            var token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var value = "true";
            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++index];
            }

            result.values[token] = value;
        }

        return result;
    }

    public bool Has(string name) => values.ContainsKey(name);

    public string? Get(string name) => values.TryGetValue(name, out var value) ? value : null;

    public int GetInt(string name, int defaultValue)
    {
        return int.TryParse(Get(name), out var value) ? value : defaultValue;
    }
}
