
namespace HPD.Base;

internal sealed record InMemoryFileObject
{
    /// <summary>Gets or sets the bucket ID.</summary>
    public required FileBucketId BucketId { get; init; }
    /// <summary>Gets or sets the object ID.</summary>
    public required FileObjectId ObjectId { get; init; }
    /// <summary>Gets or sets the key.</summary>
    public required FileObjectKey Key { get; init; }
    /// <summary>Gets or sets the name.</summary>
    public string? Name { get; init; }
    /// <summary>Gets or sets the content type.</summary>
    public string? ContentType { get; init; }
    /// <summary>Gets or sets the content.</summary>
    public required byte[] Content { get; init; }
    /// <summary>Gets or sets the checksum.</summary>
    public FileObjectChecksum? Checksum { get; init; }
    /// <summary>Gets or sets the revision.</summary>
    public required FileObjectRevision Revision { get; init; }
    /// <summary>Gets or sets the created at.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
    /// <summary>Gets or sets the updated at.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
    /// <summary>Gets or sets the owner subject ID.</summary>
    public string? OwnerSubjectId { get; init; }
    /// <summary>Gets or sets the tenant ID.</summary>
    public string? TenantId { get; init; }
    /// <summary>Gets or sets the public metadata.</summary>
    public Dictionary<string, string>? PublicMetadata { get; init; }

    /// <summary>Executes the to metadata operation.</summary>
    public FileObjectMetadata ToMetadata() => new()
    {
        BucketId = BucketId,
        ObjectId = ObjectId,
        Key = Key,
        Name = Name,
        ContentType = ContentType,
        SizeBytes = Content.LongLength,
        Checksum = Checksum,
        Revision = Revision,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
        OwnerSubjectId = OwnerSubjectId,
        TenantId = TenantId,
        PublicMetadata = PublicMetadata is null ? null : new Dictionary<string, string>(PublicMetadata, StringComparer.Ordinal)
    };
}
