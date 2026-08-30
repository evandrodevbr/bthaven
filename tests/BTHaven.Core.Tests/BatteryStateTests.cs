using BTHaven.Core.Battery;

namespace BTHaven.Core.Tests;

public sealed class BatteryStateTests
{
    [Fact]
    public void Unavailable_state_does_not_invent_a_percentage()
    {
        var state = BatteryState.Unavailable();

        Assert.Null(state.Percentage);
        Assert.Null(state.IsCharging);
        Assert.Equal("unavailable", state.Source);
        Assert.Equal(BatteryConfidence.Unknown, state.Confidence);
    }

    [Fact]
    public void Reported_state_preserves_the_provider_and_value()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-30T18:00:00Z");
        var state = new BatteryState
        {
            Percentage = 76,
            IsCharging = false,
            Source = "windows-device-properties",
            LastUpdated = observedAt,
            Confidence = BatteryConfidence.High,
        };

        Assert.Equal(76, state.Percentage);
        Assert.False(state.IsCharging);
        Assert.Equal("windows-device-properties", state.Source);
        Assert.Equal(observedAt, state.LastUpdated);
    }
}
