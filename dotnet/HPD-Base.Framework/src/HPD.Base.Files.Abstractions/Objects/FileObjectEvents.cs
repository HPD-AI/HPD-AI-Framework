namespace HPD.Base.Files.Objects;

public sealed record FileObjectEventPayload
{
    public required FileBucketId BucketId { get; init; }
    public required FileObjectId ObjectId { get; init; }
    public required string Operation { get; init; }
    public string? SubjectId { get; init; }
    public string? TenantId { get; init; }
    public string? CorrelationId { get; init; }
    public FileObjectRevision? Revision { get; init; }
    public Dictionary<string, string>? PublicMetadata { get; init; }
}
