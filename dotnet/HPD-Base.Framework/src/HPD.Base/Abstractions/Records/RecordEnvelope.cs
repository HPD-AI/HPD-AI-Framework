namespace HPD.Base;
/// <summary>Represents record Envelope.</summary>
public sealed record RecordEnvelope
{
    /// <summary>Gets or sets collection Id.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets or sets id.</summary>
    public required RecordId Id { get; init; }
    /// <summary>Gets or sets payload.</summary>
    public required RecordPayload Payload { get; init; }
    /// <summary>Gets or sets metadata.</summary>
    public required RecordMetadata Metadata { get; init; }
    /// <summary>Gets or sets policy.</summary>
    public RecordPolicyMetadata? Policy { get; init; }
    /// <summary>Gets or sets includes.</summary>
    public RecordIncludeResult[]? Includes { get; init; }
}

/// <summary>Represents record Envelope.</summary>
public sealed record RecordEnvelope<TPayload>
{
    /// <summary>Gets or sets collection Id.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets or sets id.</summary>
    public required RecordId Id { get; init; }
    /// <summary>Gets or sets payload.</summary>
    public required TPayload Payload { get; init; }
    /// <summary>Gets or sets metadata.</summary>
    public required RecordMetadata Metadata { get; init; }
    /// <summary>Gets or sets policy.</summary>
    public RecordPolicyMetadata? Policy { get; init; }
    /// <summary>Gets or sets includes.</summary>
    public RecordIncludeResult[]? Includes { get; init; }
}

/// <summary>Represents record Include Result.</summary>
public sealed record RecordIncludeResult
{
    /// <summary>Gets or sets navigation Id.</summary>
    public required string NavigationId { get; init; }
    /// <summary>Gets or sets kind.</summary>
    public required RecordIncludeKind Kind { get; init; }
    /// <summary>Gets or sets record.</summary>
    public RecordEnvelope? Record { get; init; }
    /// <summary>Gets or sets records.</summary>
    public RecordEnvelope[]? Records { get; init; }
    /// <summary>Gets or sets includes.</summary>
    public RecordIncludeResult[]? Includes { get; init; }
}

/// <summary>Defines record Include Kind.</summary>
public enum RecordIncludeKind
{
    /// <summary>Identifies none.</summary>
None,
    /// <summary>Identifies one.</summary>
One,
    /// <summary>Identifies many.</summary>
Many
}
