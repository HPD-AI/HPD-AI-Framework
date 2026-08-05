using System.Text.Json;

namespace HPD.Base;

/// <summary>Represents a base realtime client message.</summary>
public sealed record BaseRealtimeClientMessage
{
    /// <summary>Gets or sets the type.</summary>
    public required string Type { get; init; }
    /// <summary>Gets or sets the ref.</summary>
    public string? Ref { get; init; }
    /// <summary>Gets or sets the channel.</summary>
    public string? Channel { get; init; }
    /// <summary>Gets or sets the config.</summary>
    public BaseRealtimeChannelJoinRequest? Config { get; init; }
    /// <summary>Gets or sets the payload.</summary>
    public Dictionary<string, JsonElement>? Payload { get; init; }
}

/// <summary>Represents a base realtime server message.</summary>
public sealed record BaseRealtimeServerMessage
{
    /// <summary>Gets or sets the type.</summary>
    public required string Type { get; init; }
    /// <summary>Gets or sets the ref.</summary>
    public string? Ref { get; init; }
    /// <summary>Gets or sets the channel.</summary>
    public string? Channel { get; init; }
    /// <summary>Gets or sets the join.</summary>
    public BaseRealtimeChannelJoinResult? Join { get; init; }
    /// <summary>Gets or sets the event.</summary>
    public BaseRealtimeEvent? Event { get; init; }
    /// <summary>Gets or sets the error.</summary>
    public BaseRealtimeError? Error { get; init; }
    /// <summary>Gets or sets the payload.</summary>
    public Dictionary<string, JsonElement>? Payload { get; init; }
}
