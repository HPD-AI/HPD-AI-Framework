using HPD.Base;

namespace HPD.Base.Records;

public sealed record RecordMetadata
{
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public RevisionToken? Revision { get; init; }
    public string? ETag { get; init; }
    public string? StoreId { get; init; }
    public Dictionary<string, string>? Tags { get; init; }
}

public sealed record RecordPolicyMetadata
{
    public bool Redacted { get; init; }
    public string[]? OmittedFields { get; init; }
    public string[]? ReadOnlyFields { get; init; }
    public string? ReasonCode { get; init; }
}
