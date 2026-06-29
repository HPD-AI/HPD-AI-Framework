using HPD.Base;

namespace HPD.Base.Records;

public sealed record RecordCreateRequest
{
    public required RecordPayload Payload { get; init; }
    public RecordId? RequestedId { get; init; }
    public string? IdempotencyKey { get; init; }
}

/// <summary>
/// Portable phase-one patch request. FieldMap payloads merge supplied top-level fields.
/// </summary>
public sealed record RecordPatchRequest
{
    public required RecordPayload Patch { get; init; }
    public RevisionToken? ExpectedRevision { get; init; }
}

/// <summary>
/// Full replacement request for a record payload.
/// </summary>
public sealed record RecordReplaceRequest
{
    public required RecordPayload Payload { get; init; }
    public RevisionToken? ExpectedRevision { get; init; }
}

public sealed record RecordDeleteRequest
{
    public RevisionToken? ExpectedRevision { get; init; }
    public bool ReturnPrevious { get; init; }
}
