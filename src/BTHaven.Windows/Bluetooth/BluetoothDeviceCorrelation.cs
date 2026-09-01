using BTHaven.Core.Calls;
using BTHaven.Core.Devices;
using BTHaven.Windows.Audio;

namespace BTHaven.Windows.Bluetooth;

public enum BluetoothCorrelationMatchKind
{
    None,
    Name,
    Address,
    ContainerId,
    EndpointId,
}

public static class BluetoothDeviceCorrelation
{
    public static RemoteAudioDeviceInfo[] FindMatches(
        BluetoothDeviceModel device,
        IReadOnlyList<RemoteAudioDeviceInfo> candidates)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(candidates);
        return FindBestMatches(candidates, target => Match(device, target));
    }

    public static PhoneLineTransportModel[] FindMatches(
        BluetoothDeviceModel device,
        IReadOnlyList<PhoneLineTransportModel> candidates)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(candidates);
        return FindBestMatches(candidates, target => Match(device, target));
    }

    public static BluetoothCorrelationMatchKind Match(
        BluetoothDeviceModel device,
        RemoteAudioDeviceInfo target)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(target);

        if (HasEndpoint(device, target.Id))
        {
            return BluetoothCorrelationMatchKind.EndpointId;
        }

        if (EqualsNonEmpty(device.ContainerId, target.ContainerId))
        {
            return BluetoothCorrelationMatchKind.ContainerId;
        }

        if (HasAddressMatch(device.Address, target.Address, target.Id))
        {
            return BluetoothCorrelationMatchKind.Address;
        }

        return HasNameMatch(device.Name, target.Name)
            ? BluetoothCorrelationMatchKind.Name
            : BluetoothCorrelationMatchKind.None;
    }

    public static BluetoothCorrelationMatchKind Match(
        BluetoothDeviceModel device,
        PhoneLineTransportModel target)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(target);

        if (HasEndpoint(device, target.Id) || HasEndpoint(device, target.DeviceId))
        {
            return BluetoothCorrelationMatchKind.EndpointId;
        }

        var address = NormalizeAddress(device.Address);
        if (address.Length > 0
            && (ContainsNormalizedAddress(target.Id, address)
                || ContainsNormalizedAddress(target.DeviceId, address)))
        {
            return BluetoothCorrelationMatchKind.Address;
        }

        return HasNameMatch(device.Name, target.Name)
            ? BluetoothCorrelationMatchKind.Name
            : BluetoothCorrelationMatchKind.None;
    }

    private static T[] FindBestMatches<T>(
        IReadOnlyList<T> candidates,
        Func<T, BluetoothCorrelationMatchKind> matcher)
    {
        var ranked = candidates
            .Select(candidate => (Candidate: candidate, Kind: matcher(candidate)))
            .Where(result => result.Kind != BluetoothCorrelationMatchKind.None)
            .ToArray();
        if (ranked.Length == 0)
        {
            return [];
        }

        var bestKind = ranked.Max(result => result.Kind);
        return ranked
            .Where(result => result.Kind == bestKind)
            .Select(result => result.Candidate)
            .ToArray();
    }

    private static bool HasEndpoint(BluetoothDeviceModel device, string? endpointId) =>
        !string.IsNullOrWhiteSpace(endpointId)
        && device.Endpoints.Any(endpoint =>
            string.Equals(endpoint.Id, endpointId, StringComparison.OrdinalIgnoreCase));

    private static bool HasAddressMatch(string? deviceAddress, string? targetAddress, string? targetId)
    {
        var address = NormalizeAddress(deviceAddress);
        return address.Length > 0
            && (string.Equals(address, NormalizeAddress(targetAddress), StringComparison.OrdinalIgnoreCase)
                || ContainsNormalizedAddress(targetId, address));
    }

    private static bool ContainsNormalizedAddress(string? value, string address)
    {
        return NormalizeAddress(value).Contains(address, StringComparison.OrdinalIgnoreCase);
    }

    private static bool EqualsNonEmpty(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool HasNameMatch(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeAddress(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
    }
}
