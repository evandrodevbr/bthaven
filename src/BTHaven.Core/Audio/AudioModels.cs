namespace BTHaven.Core.Audio;

public enum AudioEndpointDirection
{
    Render,
    Capture,
}

public enum RemoteVolumeAvailability
{
    Unknown,
    Available,
    NotExposed,
    Failed,
}

public sealed record RemoteVolumeStatus
{
    public required RemoteVolumeAvailability Availability { get; init; }
    public required string Source { get; init; }
    public float? Level { get; init; }
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? HResult { get; init; }
    public string? Message { get; init; }
    public bool CanControl => Availability == RemoteVolumeAvailability.Available;

    public static RemoteVolumeStatus NotExposed(string source, string message) => new()
    {
        Availability = RemoteVolumeAvailability.NotExposed,
        Source = source,
        Message = message,
    };

    public static void ValidateLevel(float level)
    {
        if (float.IsNaN(level) || float.IsInfinity(level) || level is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Volume level must be between 0 and 1.");
        }
    }
}

public enum MediaAudioSinkState
{
    Disabled,
    Starting,
    Started,
    Opening,
    Opened,
    Failed,
}

public sealed record AudioEndpointModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required AudioEndpointDirection Direction { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; }
    public string? Format { get; init; }
}

public sealed record AudioStreamMetrics
{
    public long CaptureBufferFrames { get; init; }
    public long RenderBufferFrames { get; init; }
    public long Underruns { get; init; }
    public long Overruns { get; init; }
    public long DroppedPackets { get; init; }
    public int SampleRate { get; init; }
    public int Channels { get; init; }
    public TimeSpan EstimatedLatency { get; init; }
}
