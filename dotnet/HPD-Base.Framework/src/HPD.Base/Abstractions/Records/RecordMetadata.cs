
namespace HPD.Base;

/// <summary>Represents a record metadata.</summary>
public sealed record RecordMetadata
{
    /// <summary>Gets or sets the created at.</summary>
    public DateTimeOffset? CreatedAt { get; init; }
    /// <summary>Gets or sets the updated at.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }
    /// <summary>Gets or sets the revision.</summary>
    public RevisionToken? Revision { get; init; }
    /// <summary>Gets or sets the etag.</summary>
    public string? ETag { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public string? StoreId { get; init; }
    /// <summary>Gets or sets the tags.</summary>
    public Dictionary<string, string>? Tags { get; init; }
}

/// <summary>Represents a record policy metadata.</summary>
public sealed record RecordPolicyMetadata
{
    /// <summary>Gets or sets the redacted.</summary>
    public bool Redacted { get; init; }
    /// <summary>Gets or sets the omitted fields.</summary>
    public string[]? OmittedFields { get; init; }
    /// <summary>Gets or sets the read only fields.</summary>
    public string[]? ReadOnlyFields { get; init; }
    /// <summary>Gets or sets the reason code.</summary>
    public string? ReasonCode { get; init; }
}
