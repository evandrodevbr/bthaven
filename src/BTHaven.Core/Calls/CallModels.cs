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

public sealed record PhoneLineTransportModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? DeviceId { get; init; }
    public string? Transport { get; init; }
    public string? AudioRoutingStatus { get; init; }
    public bool InBandRingingEnabled { get; init; }
    public bool IsRegistered { get; init; }
}

public sealed record PhoneLineTransportActivationResult
{
    public bool Succeeded { get; init; }
    public required string Status { get; init; }
    public string? Message { get; init; }
    public string? AccessStatus { get; init; }
    public bool IsRegistered { get; init; }
    public bool IsConnected { get; init; }
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
