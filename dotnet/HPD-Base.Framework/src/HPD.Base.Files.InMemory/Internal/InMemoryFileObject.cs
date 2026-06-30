using HPD.Base.Files.Objects;

namespace HPD.Base.Files.InMemory.Internal;

internal sealed record InMemoryFileObject
{
    public required FileBucketId BucketId { get; init; }
    public required FileObjectId ObjectId { get; init; }
    public required FileObjectKey Key { get; init; }
    public string? Name { get; init; }
    public string? ContentType { get; init; }
    public required byte[] Content { get; init; }
    public FileObjectChecksum? Checksum { get; init; }
    public required FileObjectRevision Revision { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public string? OwnerSubjectId { get; init; }
    public string? TenantId { get; init; }
    public Dictionary<string, string>? PublicMetadata { get; init; }

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
