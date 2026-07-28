using System.Text.Json;

namespace HPD.Base.Realtime;

public sealed record BaseRealtimeClientMessage
{
    public required string Type { get; init; }
    public string? Ref { get; init; }
    public string? Channel { get; init; }
    public BaseRealtimeChannelJoinRequest? Config { get; init; }
    public Dictionary<string, JsonElement>? Payload { get; init; }
}

public sealed record BaseRealtimeServerMessage
{
    public required string Type { get; init; }
    public string? Ref { get; init; }
    public string? Channel { get; init; }
    public BaseRealtimeChannelJoinResult? Join { get; init; }
    public BaseRealtimeEvent? Event { get; init; }
    public BaseRealtimeError? Error { get; init; }
    public Dictionary<string, JsonElement>? Payload { get; init; }
}
