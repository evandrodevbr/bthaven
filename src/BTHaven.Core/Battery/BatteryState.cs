namespace BTHaven.Core.Battery;

public enum BatteryConfidence
{
    Unknown,
    Low,
    Medium,
    High,
}

public sealed record BatteryState
{
    public int? Percentage { get; init; }
    public bool? IsCharging { get; init; }
    public required string Source { get; init; }
    public DateTimeOffset LastUpdated { get; init; }
    public BatteryConfidence Confidence { get; init; }

    public static BatteryState Unavailable(string source = "unavailable") => new()
    {
        Percentage = null,
        IsCharging = null,
        Source = source,
        LastUpdated = DateTimeOffset.UtcNow,
        Confidence = BatteryConfidence.Unknown,
    };
}
