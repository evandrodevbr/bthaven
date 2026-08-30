using BTHaven.Core.Bluetooth;

namespace BTHaven.Core.Tests;

public sealed class ReconnectBackoffTests
{
    [Fact]
    public void Follows_the_documented_schedule_and_caps_at_one_minute()
    {
        var backoff = new ReconnectBackoff();

        var delays = Enumerable.Range(0, 8).Select(_ => backoff.NextDelay()).ToArray();

        Assert.Equal(
            new[] { 1, 2, 5, 10, 30, 60, 60, 60 },
            delays.Select(delay => (int)delay.TotalSeconds));
    }

    [Fact]
    public void Reset_starts_the_schedule_again()
    {
        var backoff = new ReconnectBackoff();
        _ = backoff.NextDelay();
        _ = backoff.NextDelay();

        backoff.Reset();

        Assert.Equal(TimeSpan.FromSeconds(1), backoff.NextDelay());
    }
}
