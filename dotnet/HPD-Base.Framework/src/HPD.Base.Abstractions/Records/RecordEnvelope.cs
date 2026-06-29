using HPD.Base;

namespace HPD.Base.Records;

public sealed record RecordEnvelope
{
    public required string CollectionId { get; init; }
    public required RecordId Id { get; init; }
    public required RecordPayload Payload { get; init; }
    public required RecordMetadata Metadata { get; init; }
    public RecordPolicyMetadata? Policy { get; init; }
    public Dictionary<string, RecordIncludeValue>? Includes { get; init; }
}

public sealed record RecordEnvelope<TPayload>
{
    public required string CollectionId { get; init; }
    public required RecordId Id { get; init; }
    public required TPayload Payload { get; init; }
    public required RecordMetadata Metadata { get; init; }
    public RecordPolicyMetadata? Policy { get; init; }
    public Dictionary<string, RecordIncludeValue>? Includes { get; init; }
}

public sealed record RecordIncludeValue
{
    public required string Path { get; init; }
    public required RecordIncludeKind Kind { get; init; }
    public RecordEnvelope? Record { get; init; }
    public RecordEnvelope[]? Records { get; init; }
    public bool Truncated { get; init; }
    public string? ReasonCode { get; init; }
}

public enum RecordIncludeKind
{
    None,
    One,
    Many
}
