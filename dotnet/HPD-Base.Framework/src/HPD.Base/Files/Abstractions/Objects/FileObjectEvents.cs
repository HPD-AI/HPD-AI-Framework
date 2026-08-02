namespace HPD.Base;

/// <summary>Represents a file object event payload.</summary>
public sealed record FileObjectEventPayload
{
    /// <summary>Gets or sets the bucket ID.</summary>
    public required FileBucketId BucketId { get; init; }
    /// <summary>Gets or sets the object ID.</summary>
    public required FileObjectId ObjectId { get; init; }
    /// <summary>Gets or sets the operation.</summary>
    public required string Operation { get; init; }
    /// <summary>Gets or sets the subject ID.</summary>
    public string? SubjectId { get; init; }
    /// <summary>Gets or sets the tenant ID.</summary>
    public string? TenantId { get; init; }
    /// <summary>Gets or sets the correlation ID.</summary>
    public string? CorrelationId { get; init; }
    /// <summary>Gets or sets the revision.</summary>
    public FileObjectRevision? Revision { get; init; }
    /// <summary>Gets or sets the public metadata.</summary>
    public Dictionary<string, string>? PublicMetadata { get; init; }
}
