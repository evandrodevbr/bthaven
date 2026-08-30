namespace BTHaven.Core.Calls;

public enum CallState
{
    Disconnected,
    Connecting,
    Connected,
    Idle,
    IncomingCall,
    OutgoingCall,
    CallActive,
    CallHeld,
    AudioConnecting,
    AudioActive,
    Disconnecting,
    Error,
}

public sealed record CallSessionSnapshot
{
    public CallState State { get; init; }
    public string? CallerId { get; init; }
    public string? CallerName { get; init; }
    public bool? InBandRinging { get; init; }
    public bool IsMuted { get; init; }
    public string? OutputEndpointId { get; init; }
    public string? InputEndpointId { get; init; }
}
