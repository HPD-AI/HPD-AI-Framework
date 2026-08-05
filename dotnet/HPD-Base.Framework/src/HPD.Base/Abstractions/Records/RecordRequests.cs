
namespace HPD.Base;

/// <summary>Represents a record create request.</summary>
public sealed record RecordCreateRequest
{
    /// <summary>Gets or sets the payload.</summary>
    public required RecordPayload Payload { get; init; }
    /// <summary>Gets or sets the requested ID.</summary>
    public RecordId? RequestedId { get; init; }
}

/// <summary>
/// Portable phase-one patch request. FieldMap payloads merge supplied top-level fields.
/// </summary>
public sealed record RecordPatchRequest
{
    /// <summary>Gets or sets the patch.</summary>
    public required RecordPayload Patch { get; init; }
    /// <summary>Gets or sets the expected revision.</summary>
    public RevisionToken? ExpectedRevision { get; init; }
}

/// <summary>
/// Full replacement request for a record payload.
/// </summary>
public sealed record RecordReplaceRequest
{
    /// <summary>Gets or sets the payload.</summary>
    public required RecordPayload Payload { get; init; }
    /// <summary>Gets or sets the expected revision.</summary>
    public RevisionToken? ExpectedRevision { get; init; }
}

/// <summary>Represents a record delete request.</summary>
public sealed record RecordDeleteRequest
{
    /// <summary>Gets or sets the expected revision.</summary>
    public RevisionToken? ExpectedRevision { get; init; }
    /// <summary>Gets or sets the return previous.</summary>
    public bool ReturnPrevious { get; init; }
}
