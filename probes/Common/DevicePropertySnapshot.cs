using Windows.Devices.Enumeration;

namespace BTHaven.Probes.Common;

public static class DevicePropertySnapshot
{
    public static IReadOnlyDictionary<string, string?> Read(DeviceInformation device)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in device.Properties)
        {
            result[property.Key] = property.Value?.ToString();
        }

        return result;
    }

    public static string? Get(IReadOnlyDictionary<string, string?> properties, string name)
    {
        return properties.TryGetValue(name, out var value) ? value : null;
    }

    public static bool? GetBool(IReadOnlyDictionary<string, string?> properties, string name)
    {
        var value = Get(properties, name);
        return bool.TryParse(value, out var parsed) ? parsed : null;
    }

    public static int? GetInt(IReadOnlyDictionary<string, string?> properties, string name)
    {
        var value = Get(properties, name);
        return int.TryParse(value, out var parsed) ? parsed : null;
    }
}
