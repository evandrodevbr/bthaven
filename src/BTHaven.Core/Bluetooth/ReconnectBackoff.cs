namespace BTHaven.Core.Bluetooth;

public sealed class ReconnectBackoff
{
    private static readonly TimeSpan[] DefaultDelays =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
    };

    private readonly IReadOnlyList<TimeSpan> delays;
    private int attempt;

    public ReconnectBackoff(IEnumerable<TimeSpan>? delays = null)
    {
        this.delays = (delays ?? DefaultDelays).ToArray();
        if (this.delays.Count == 0 || this.delays.Any(delay => delay < TimeSpan.Zero))
        {
            throw new ArgumentException("Reconnect backoff must contain non-negative delays.", nameof(delays));
        }
    }

    public TimeSpan NextDelay()
    {
        var index = Math.Min(attempt, delays.Count - 1);
        attempt++;
        return delays[index];
    }

    public void Reset() => attempt = 0;
}
