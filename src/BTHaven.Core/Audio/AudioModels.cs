namespace BTHaven.Core.Audio;

public enum AudioEndpointDirection
{
    Render,
    Capture,
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
