using BTHaven.Core.Devices;

namespace BTHaven.Core.Tests;

public sealed class BluetoothDeviceSelectionPolicyTests
{
    [Theory]
    [MemberData(nameof(Cases))]
    public void Resolve_follows_policy_A(
        string[] visibleIds,
        string? currentId,
        string? preferredId,
        string? expectedId)
    {
        var visible = visibleIds.Select(id => Device(id)).ToArray();

        var actual = BluetoothDeviceSelectionPolicy.Resolve(visible, currentId, preferredId);

        Assert.Equal(expectedId, actual, ignoreCase: true);
    }

    [Fact]
    public void Connected_item_does_not_replace_visible_disconnected_current_selection()
    {
        var visible = new[]
        {
            Device("connected-other", isConnected: true),
            Device("chosen-disconnected", isConnected: false),
        };

        var actual = BluetoothDeviceSelectionPolicy.Resolve(
            visible,
            currentDeviceId: "CHOSEN-DISCONNECTED",
            preferredDeviceId: "connected-other");

        Assert.Equal("chosen-disconnected", actual);
    }

    public static TheoryData<string[], string?, string?, string?> Cases => new()
    {
        { ["connected-other", "chosen-disconnected"], "chosen-disconnected", "chosen-disconnected", "chosen-disconnected" },
        { ["first", "preferred"], null, "preferred", "preferred" },
        { ["first", "second"], "hidden", "missing", "first" },
        { [], "old", "old", null },
        { ["CaseSensitive"], "casesensitive", null, "CaseSensitive" },
    };

    private static BluetoothDeviceModel Device(string id, bool isConnected = false) => new()
    {
        Id = id,
        Name = id,
        IsConnected = isConnected,
    };
}
