using BTHaven_App;

namespace BTHaven.IntegrationTests;

public sealed class BatteryTextFormatterTests
{
    [Theory]
    [InlineData(true, "57% · carregando")]
    [InlineData(false, "57% · não carregando")]
    public void Percentage_keeps_known_charging_state(bool isCharging, string expected) =>
        Assert.Equal(expected, BatteryTextFormatter.FormatPercentage(57, isCharging));
}
