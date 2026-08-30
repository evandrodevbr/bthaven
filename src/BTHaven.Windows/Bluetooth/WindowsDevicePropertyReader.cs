using System.Collections;
namespace BTHaven.Windows.Bluetooth;

internal static class WindowsDevicePropertyReader
{
    public static bool? Bool(IReadOnlyDictionary<string, object> properties, string key)
    {
        var value = Find(properties, key);
        if (value is bool boolean)
        {
            return boolean;
        }

        return bool.TryParse(value?.ToString(), out var parsed) ? parsed : null;
    }

    public static int? Int32(IReadOnlyDictionary<string, object> properties, string key)
    {
        var value = Find(properties, key);
        if (value is int integer)
        {
            return integer;
        }

        return int.TryParse(value?.ToString(), out var parsed) ? parsed : null;
    }

    public static string? String(IReadOnlyDictionary<string, object> properties, string key)
    {
        return Find(properties, key)?.ToString();
    }

    public static IReadOnlyList<string> Strings(IReadOnlyDictionary<string, object> properties, string key)
    {
        var value = Find(properties, key);
        if (value is null)
        {
            return [];
        }

        if (value is string single)
        {
            return [single];
        }

        if (value is IEnumerable<string> strings)
        {
            return strings.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        }

        if (value is IEnumerable enumerable)
        {
            return enumerable.Cast<object?>()
                .Select(item => item?.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!)
                .ToArray();
        }

        return [value.ToString()!];
    }

    public static bool Contains(IReadOnlyDictionary<string, object> properties, string key)
    {
        return Find(properties, key) is not null;
    }

    private static object? Find(IReadOnlyDictionary<string, object> properties, string key)
    {
        return properties.TryGetValue(key, out var value) ? value : null;
    }
}
