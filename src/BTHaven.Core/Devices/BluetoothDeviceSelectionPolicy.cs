namespace BTHaven.Core.Devices;

public static class BluetoothDeviceSelectionPolicy
{
    public static string? Resolve(
        IReadOnlyList<BluetoothDeviceModel> visibleDevices,
        string? currentDeviceId,
        string? preferredDeviceId)
    {
        ArgumentNullException.ThrowIfNull(visibleDevices);

        static bool Same(string left, string? right) =>
            right is not null && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        return visibleDevices.FirstOrDefault(device => Same(device.Id, currentDeviceId))?.Id
            ?? visibleDevices.FirstOrDefault(device => Same(device.Id, preferredDeviceId))?.Id
            ?? visibleDevices.FirstOrDefault()?.Id;
    }
}
