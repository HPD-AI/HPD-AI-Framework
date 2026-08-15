
namespace HPD.Base;

/// <summary>Describes one policy-visible live record mutation.</summary>
public sealed record BaseRealtimeEvent
{
    /// <summary>Gets a sanitized exported-subject authority publication for an applicable channel.</summary>
    public BaseSubjectAuthorityPublicationFact? SubjectAuthorityPublication { get; init; }
    /// <summary>Gets the event identity assigned by the mutation publisher.</summary>
    public required string EventId { get; init; }

    /// <summary>Gets the stable record-mutation event type.</summary>
    public required string Type { get; init; }

    /// <summary>Gets the schema version of the projected event contract.</summary>
    public required string SchemaVersion { get; init; }

    /// <summary>Gets when the mutation occurred.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>Gets the affected record identity.</summary>
    public required BaseRealtimeRecordResource Resource { get; init; }

    /// <summary>Gets the mutation operation.</summary>
    public required BaseOperationKind Operation { get; init; }

    /// <summary>Gets the independently redacted prior snapshot when explicitly authorized.</summary>
    public BaseRealtimeRecordSnapshot? Before { get; init; }

    /// <summary>Gets the independently redacted resulting snapshot when requested and authorized.</summary>
    public BaseRealtimeRecordSnapshot? After { get; init; }

    /// <summary>Gets the opaque continuation cursor for a durable channel event.</summary>
    public string? Cursor { get; init; }

    /// <summary>Gets policy-visible opaque dependency invalidations when the capability is enabled.</summary>
    public BaseDependencyInvalidation? Invalidation { get; init; }
}

/// <summary>Identifies the record affected by a realtime mutation event.</summary>
public sealed record BaseRealtimeRecordResource
{
    /// <summary>Gets the collection that owns the record.</summary>
    public required string CollectionId { get; init; }

    /// <summary>Gets the affected record identity.</summary>
    public required RecordId RecordId { get; init; }
}

/// <summary>Contains a subscriber-specific redacted record payload.</summary>
public sealed record BaseRealtimeRecordSnapshot
{
    /// <summary>Gets the redacted record payload.</summary>
    public required RecordPayload Payload { get; init; }
}
