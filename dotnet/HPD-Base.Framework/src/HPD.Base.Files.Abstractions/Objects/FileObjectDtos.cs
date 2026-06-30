using HPD.Base.Results;

namespace HPD.Base.Files.Objects;

public sealed record FileObjectRef
{
    public required FileBucketId BucketId { get; init; }
    public required FileObjectId ObjectId { get; init; }
    public FileObjectRevision? Revision { get; init; }
    public string? ContentType { get; init; }
    public long? SizeBytes { get; init; }
    public FileObjectChecksum? Checksum { get; init; }
    public string? Name { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
}

public sealed record FileObjectMetadata
{
    public required FileBucketId BucketId { get; init; }
    public required FileObjectId ObjectId { get; init; }
    public FileObjectKey? Key { get; init; }
    public string? Name { get; init; }
    public string? ContentType { get; init; }
    public long? SizeBytes { get; init; }
    public FileObjectChecksum? Checksum { get; init; }
    public FileObjectRevision? Revision { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? OwnerSubjectId { get; init; }
    public string? TenantId { get; init; }
    public Dictionary<string, string>? PublicMetadata { get; init; }
}

public sealed record FileObjectUploadRequest
{
    public required FileBucketId BucketId { get; init; }
    public FileObjectKey? Key { get; init; }
    public string? Name { get; init; }
    public string? ContentType { get; init; }
    public long? SizeBytes { get; init; }
    public FileObjectChecksum? Checksum { get; init; }
    public bool Overwrite { get; init; }
    public Stream? Content { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
}

public sealed record FileObjectUploadResult
{
    public required FileObjectMetadata Metadata { get; init; }
    public bool Created { get; init; } = true;
}

public sealed record FileObjectDownloadRequest
{
    public required FileBucketId BucketId { get; init; }
    public required FileObjectId ObjectId { get; init; }
}

public sealed record FileObjectDownloadResult : IAsyncDisposable, IDisposable
{
    public required FileObjectMetadata Metadata { get; init; }
    public required Stream Content { get; init; }
    public long? ContentLength { get; init; }
    public string? ContentType { get; init; }
    public string? ETag { get; init; }
    public FileObjectRevision? Revision { get; init; }
    public bool OwnsStream { get; init; } = true;

    public void Dispose()
    {
        if (OwnsStream)
            Content.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (OwnsStream)
            await Content.DisposeAsync();
    }
}

public sealed record FileObjectMetadataRequest
{
    public required FileBucketId BucketId { get; init; }
    public required FileObjectId ObjectId { get; init; }
}

public sealed record FileObjectListRequest
{
    public required FileBucketId BucketId { get; init; }
    public FileObjectKey? Prefix { get; init; }
    public int? Limit { get; init; }
    public string? Cursor { get; init; }
}

public sealed record FileObjectListResult
{
    public required FileObjectMetadata[] Items { get; init; }
    public string? NextCursor { get; init; }
}

public sealed record FileObjectDeleteRequest
{
    public required FileBucketId BucketId { get; init; }
    public required FileObjectId ObjectId { get; init; }
}

public interface IFileObjectService
{
    ValueTask<OperationResult<FileObjectUploadResult>> UploadAsync(FileObjectUploadRequest request, FileOperationContext context, CancellationToken cancellationToken = default);
    ValueTask<OperationResult<FileObjectDownloadResult>> OpenDownloadAsync(FileObjectDownloadRequest request, FileOperationContext context, CancellationToken cancellationToken = default);
    ValueTask<OperationResult<FileObjectMetadata>> GetMetadataAsync(FileObjectMetadataRequest request, FileOperationContext context, CancellationToken cancellationToken = default);
    ValueTask<OperationResult> DeleteAsync(FileObjectDeleteRequest request, FileOperationContext context, CancellationToken cancellationToken = default);
    ValueTask<OperationResult<FileObjectListResult>> ListMetadataAsync(FileObjectListRequest request, FileOperationContext context, CancellationToken cancellationToken = default);
}

public sealed record FileOperationContext
{
    public string? SubjectId { get; init; }
    public string? TenantId { get; init; }
    public string? CorrelationId { get; init; }
    public bool IsAdmin { get; init; }
}
