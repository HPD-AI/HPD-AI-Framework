using HPD.Base.Policy;

namespace HPD.Base.Realtime;

public sealed record BaseRealtimeChannelJoinRequest
{
    public required string Kind { get; init; }
    public bool Private { get; init; } = true;
    public string? CollectionId { get; init; }
    public string? RecordId { get; init; }
    public BaseOperationKind[]? Operations { get; init; }
    public string[]? EventTypes { get; init; }
    public string? TenantId { get; init; }
    public VisibilityLevel? Visibility { get; init; }
    public bool IncludeSnapshots { get; init; }
    public bool IncludeBefore { get; init; }
    public bool IncludePrincipal { get; init; }
    public bool IncludeExtensions { get; init; }
}

public sealed record BaseRealtimeSubscribeRequest
{
    public required string Channel { get; init; }
    public required BaseRealtimeChannelJoinRequest Config { get; init; }
}

public sealed record BaseRealtimeChannelJoinResult
{
    public required string Channel { get; init; }
    public required string Kind { get; init; }
    public bool Replayable { get; init; }
    public bool Resumable { get; init; }
    public string? StreamId { get; init; }
}

public sealed record BaseRealtimeConnectionDescriptor
{
    public required string ConnectionId { get; init; }
    public required string Transport { get; init; }
    public required DateTimeOffset ConnectedAt { get; init; }
    public int ActiveChannelCount { get; init; }
    public bool Replayable { get; init; }
    public bool Resumable { get; init; }
}

public sealed record BaseRealtimeChannelDescriptor
{
    public required string Channel { get; init; }
    public required string Kind { get; init; }
    public string? CollectionId { get; init; }
    public string? RecordId { get; init; }
    public string? TenantId { get; init; }
    public bool Private { get; init; }
    public bool Replayable { get; init; }
    public bool Resumable { get; init; }
}

public sealed record BaseRealtimeError
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Target { get; init; }
}

public sealed record BaseRealtimeSnapshotOptions
{
    public bool IncludeSnapshots { get; init; }
    public bool IncludeBefore { get; init; }
    public bool IncludePrincipal { get; init; }
    public bool IncludeExtensions { get; init; }
}

public sealed record BaseRealtimeLimits
{
    public int MaxConnections { get; init; } = 1024;
    public int MaxChannelsPerConnection { get; init; } = 16;
    public int StreamCapacity { get; init; } = 1024;
    public int MaxMessageBytes { get; init; } = 64 * 1024;
    public int MaxPayloadBytes { get; init; } = 256 * 1024;
    public int HeartbeatIntervalSeconds { get; init; } = 30;
    public int HeartbeatTimeoutSeconds { get; init; } = 90;
    public int MaxJoinsPerSecond { get; init; } = 8;
    public int MaxEventsPerSecond { get; init; } = 1024;
}
