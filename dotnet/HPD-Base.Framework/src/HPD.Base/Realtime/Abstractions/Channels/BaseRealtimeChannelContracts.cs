
using System.Text.Json;

namespace HPD.Base;

/// <summary>Defines one closed realtime version 2 channel request.</summary>
[System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseRealtimeLiveFeedRequest), "live")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseRealtimeDurableFeedRequest), "durable")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseRealtimeResumeFeedRequest), "resume")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseRealtimeLiveQueryJoinRequest), "liveQuery")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseRealtimeSubjectLifecycleHintRequest), "subjectLifecycleHints")]
public abstract record BaseRealtimeChannelRequest;

/// <summary>Requests non-authoritative wake-up hints for one installed durable lifecycle consumer.</summary>
public sealed record BaseRealtimeSubjectLifecycleHintRequest : BaseRealtimeChannelRequest
{
    /// <summary>Gets the installed consumer ID.</summary>
    public required string ConsumerId { get; init; }
    /// <summary>Gets the installed consumer version.</summary>
    public required int ConsumerVersion { get; init; }
    /// <summary>Gets the exact project scope for project-bound contracts.</summary>
    public string? ProjectId { get; init; }
}

/// <summary>Defines the bounded filter shared by record-feed requests.</summary>
public sealed record BaseRealtimeRecordFeedFilter
{
    /// <summary>Gets an optional record identity.</summary>
    public string? RecordId { get; init; }
    /// <summary>Gets optional mutation-operation filters.</summary>
    public BaseOperationKind[]? Operations { get; init; }
    /// <summary>Gets optional event-type filters.</summary>
    public string[]? EventTypes { get; init; }
    /// <summary>Gets an optional authorized tenant filter.</summary>
    public string? TenantId { get; init; }
    /// <summary>Gets whether a resulting snapshot is requested.</summary>
    public bool IncludeSnapshots { get; init; }
    /// <summary>Gets whether an authorized prior snapshot is requested.</summary>
    public bool IncludeBefore { get; init; }
}

/// <summary>Requests live at-most-once record delivery.</summary>
public sealed record BaseRealtimeLiveFeedRequest : BaseRealtimeChannelRequest
{
    /// <summary>Gets the collection identifier.</summary>
    public required string Collection { get; init; }
    /// <summary>Gets the bounded feed filter.</summary>
    public required BaseRealtimeRecordFeedFilter Filter { get; init; }
}

/// <summary>Requests durable at-least-once record delivery from the current head.</summary>
public sealed record BaseRealtimeDurableFeedRequest : BaseRealtimeChannelRequest
{
    /// <summary>Gets the collection identifier.</summary>
    public required string Collection { get; init; }
    /// <summary>Gets the bounded feed filter.</summary>
    public required BaseRealtimeRecordFeedFilter Filter { get; init; }
}

/// <summary>Resumes durable record delivery from an opaque cursor.</summary>
public sealed record BaseRealtimeResumeFeedRequest : BaseRealtimeChannelRequest
{
    /// <summary>Gets the collection identifier.</summary>
    public required string Collection { get; init; }
    /// <summary>Gets the opaque resume cursor.</summary>
    public required string Cursor { get; init; }
    /// <summary>Gets the bounded feed filter.</summary>
    public required BaseRealtimeRecordFeedFilter Filter { get; init; }
}

/// <summary>Requests one generated dependency-driven live query.</summary>
public sealed record BaseRealtimeLiveQueryJoinRequest : BaseRealtimeChannelRequest
{
    /// <summary>Gets the generated closed operation.</summary>
    public required BaseRealtimeLiveQueryOperation Operation { get; init; }
    /// <summary>Gets the server-declared result DTO identifier.</summary>
    public required string ResultTypeId { get; init; }
}

/// <summary>Defines one closed live-query operation.</summary>
[System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseRealtimeCollectionQueryOperation), "collectionQuery")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseRealtimeRegisteredReadOperation), "registeredRead")]
public abstract record BaseRealtimeLiveQueryOperation;

/// <summary>Executes one bounded collection query as a live replacement.</summary>
public sealed record BaseRealtimeCollectionQueryOperation : BaseRealtimeLiveQueryOperation
{
    /// <summary>Gets the stable collection ID.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the closed query.</summary>
    public required RecordQuery Query { get; init; }
    /// <summary>Gets the required complete replacement bound.</summary>
    public required int Take { get; init; }
}

/// <summary>Executes one generated registered read as a live replacement.</summary>
public sealed record BaseRealtimeRegisteredReadOperation : BaseRealtimeLiveQueryOperation
{
    /// <summary>Gets the stable registered-read ID.</summary>
    public required string ReadId { get; init; }
    /// <summary>Gets the source-generated parameter payload.</summary>
    public required JsonElement Parameters { get; init; }
}

/// <summary>Defines a request to join the live record-mutation channel.</summary>
public sealed record BaseRealtimeChannelJoinRequest
{
    /// <summary>Gets the requested channel kind.</summary>
    public required string Kind { get; init; }

    /// <summary>Gets whether the channel requires an authenticated principal.</summary>
    public bool Private { get; init; } = true;

    /// <summary>Gets an optional collection filter.</summary>
    public string? CollectionId { get; init; }

    /// <summary>Gets an optional record identity filter.</summary>
    public string? RecordId { get; init; }

    /// <summary>Gets optional mutation-operation filters.</summary>
    public BaseOperationKind[]? Operations { get; init; }

    /// <summary>Gets optional event-type filters.</summary>
    public string[]? EventTypes { get; init; }

    /// <summary>Gets an optional authorized tenant filter.</summary>
    public string? TenantId { get; init; }

    /// <summary>Gets whether a redacted resulting snapshot is requested.</summary>
    public bool IncludeSnapshots { get; init; }

    /// <summary>Gets whether an authorized redacted prior snapshot is requested.</summary>
    public bool IncludeBefore { get; init; }

    /// <summary>Gets whether this channel must use the durable mutation journal.</summary>
    public bool Durable { get; init; }

    /// <summary>Gets an opaque durable cursor from a previously delivered event.</summary>
    public string? ResumeCursor { get; init; }
}

/// <summary>Represents a base realtime channel join result.</summary>
public sealed record BaseRealtimeChannelJoinResult
{
    /// <summary>Gets or sets the channel.</summary>
    public required string Channel { get; init; }
    /// <summary>Gets or sets the kind.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets or sets the replayable.</summary>
    public bool Replayable { get; init; }
    /// <summary>Gets or sets the resumable.</summary>
    public bool Resumable { get; init; }
    /// <summary>Gets or sets the stream ID.</summary>
    public string? StreamId { get; init; }
    /// <summary>Gets or sets the cursor.</summary>
    public string? Cursor { get; init; }
}

/// <summary>Represents a base realtime error.</summary>
public sealed record BaseRealtimeError
{
    /// <summary>Gets or sets the code.</summary>
    public required string Code { get; init; }
    /// <summary>Gets or sets the message.</summary>
    public required string Message { get; init; }
    /// <summary>Gets or sets the target.</summary>
    public string? Target { get; init; }
}

/// <summary>Defines enforced limits for one realtime server instance and its connections.</summary>
public sealed record BaseRealtimeLimits
{
    /// <summary>Gets the maximum number of concurrent realtime connections.</summary>
    public int MaxConnections { get; init; } = 1024;

    /// <summary>Gets the maximum number of joined channels on one connection.</summary>
    public int MaxChannelsPerConnection { get; init; } = 128;

    /// <summary>Gets the capacity of each channel's HPD.Events inbox.</summary>
    public int StreamCapacity { get; init; } = 1024;

    /// <summary>Gets the capacity of each channel's outbound event queue.</summary>
    public int OutboundCapacity { get; init; } = 32;

    /// <summary>Gets the maximum complete inbound protocol message size in bytes.</summary>
    public int MaxMessageBytes { get; init; } = 1024 * 1024;

    /// <summary>Gets the maximum serialized outbound protocol payload size in bytes.</summary>
    public int MaxPayloadBytes { get; init; } = 256 * 1024;

    /// <summary>Gets the maximum interval without a complete inbound message.</summary>
    public int ReceiveIdleTimeoutSeconds { get; init; } = 90;

    /// <summary>Gets the maximum time allowed for one WebSocket send.</summary>
    public int SendTimeoutSeconds { get; init; } = 10;

    /// <summary>Gets the maximum channel join attempts allowed per connection per second.</summary>
    public int MaxJoinsPerSecond { get; init; } = 8;

    /// <summary>Gets the maximum journal entries read per durable poll.</summary>
    public int ReplayBatchSize { get; init; } = 256;

    /// <summary>Gets the lifetime of an issued resume cursor in seconds.</summary>
    public int CursorLifetimeSeconds { get; init; } = 7 * 24 * 60 * 60;

    /// <summary>Gets the durable journal polling interval in milliseconds.</summary>
    public int DurablePollIntervalMilliseconds { get; init; } = 250;
}
