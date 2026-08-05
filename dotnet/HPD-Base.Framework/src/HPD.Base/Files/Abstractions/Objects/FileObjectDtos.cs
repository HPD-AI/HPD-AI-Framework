
namespace HPD.Base;

/// <summary>Represents a file object ref.</summary>
public sealed record FileObjectRef
{
    /// <summary>Gets or sets the bucket ID.</summary>
    public required FileBucketId BucketId { get; init; }
    /// <summary>Gets or sets the object ID.</summary>
    public required FileObjectId ObjectId { get; init; }
    /// <summary>Gets or sets the revision.</summary>
    public FileObjectRevision? Revision { get; init; }
    /// <summary>Gets or sets the content type.</summary>
    public string? ContentType { get; init; }
    /// <summary>Gets or sets the size bytes.</summary>
    public long? SizeBytes { get; init; }
    /// <summary>Gets or sets the checksum.</summary>
    public FileObjectChecksum? Checksum { get; init; }
    /// <summary>Gets or sets the name.</summary>
    public string? Name { get; init; }
    /// <summary>Gets or sets the metadata.</summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>Represents a file object metadata.</summary>
public sealed record FileObjectMetadata
{
    /// <summary>Gets or sets the bucket ID.</summary>
    public required FileBucketId BucketId { get; init; }
    /// <summary>Gets or sets the object ID.</summary>
    public required FileObjectId ObjectId { get; init; }
    /// <summary>Gets or sets the key.</summary>
    public FileObjectKey? Key { get; init; }
    /// <summary>Gets or sets the name.</summary>
    public string? Name { get; init; }
    /// <summary>Gets or sets the content type.</summary>
    public string? ContentType { get; init; }
    /// <summary>Gets or sets the size bytes.</summary>
    public long? SizeBytes { get; init; }
    /// <summary>Gets or sets the checksum.</summary>
    public FileObjectChecksum? Checksum { get; init; }
    /// <summary>Gets or sets the revision.</summary>
    public FileObjectRevision? Revision { get; init; }
    /// <summary>Gets or sets the created at.</summary>
    public DateTimeOffset? CreatedAt { get; init; }
    /// <summary>Gets or sets the updated at.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }
    /// <summary>Gets or sets the owner subject ID.</summary>
    public string? OwnerSubjectId { get; init; }
    /// <summary>Gets or sets the tenant ID.</summary>
    public string? TenantId { get; init; }
    /// <summary>Gets or sets the public metadata.</summary>
    public Dictionary<string, string>? PublicMetadata { get; init; }
}

/// <summary>Represents a file object upload request.</summary>
public sealed record FileObjectUploadRequest
{
    /// <summary>Gets or sets the bucket ID.</summary>
    public required FileBucketId BucketId { get; init; }
    /// <summary>Gets or sets the key.</summary>
    public FileObjectKey? Key { get; init; }
    /// <summary>Gets or sets the name.</summary>
    public string? Name { get; init; }
    /// <summary>Gets or sets the content type.</summary>
    public string? ContentType { get; init; }
    /// <summary>Gets or sets the size bytes.</summary>
    public long? SizeBytes { get; init; }
    /// <summary>Gets or sets the checksum.</summary>
    public FileObjectChecksum? Checksum { get; init; }
    /// <summary>Gets or sets the overwrite.</summary>
    public bool Overwrite { get; init; }
    /// <summary>Gets or sets the content.</summary>
    public Stream? Content { get; init; }
    /// <summary>Gets or sets the metadata.</summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>Represents a file object upload result.</summary>
public sealed record FileObjectUploadResult
{
    /// <summary>Gets or sets the metadata.</summary>
    public required FileObjectMetadata Metadata { get; init; }
    /// <summary>Gets or sets the created.</summary>
    public bool Created { get; init; } = true;
}

/// <summary>Represents a file object download request.</summary>
public sealed record FileObjectDownloadRequest
{
    /// <summary>Gets or sets the bucket ID.</summary>
    public required FileBucketId BucketId { get; init; }
    /// <summary>Gets or sets the object ID.</summary>
    public required FileObjectId ObjectId { get; init; }
}

/// <summary>Represents a file object download result.</summary>
public sealed record FileObjectDownloadResult : IAsyncDisposable, IDisposable
{
    /// <summary>Gets or sets the metadata.</summary>
    public required FileObjectMetadata Metadata { get; init; }
    /// <summary>Gets or sets the content.</summary>
    public required Stream Content { get; init; }
    /// <summary>Gets or sets the content length.</summary>
    public long? ContentLength { get; init; }
    /// <summary>Gets or sets the content type.</summary>
    public string? ContentType { get; init; }
    /// <summary>Gets or sets the etag.</summary>
    public string? ETag { get; init; }
    /// <summary>Gets or sets the revision.</summary>
    public FileObjectRevision? Revision { get; init; }
    /// <summary>Gets or sets the owns stream.</summary>
    public bool OwnsStream { get; init; } = true;

    /// <summary>Executes the dispose operation.</summary>
    public void Dispose()
    {
        if (OwnsStream)
            Content.Dispose();
    }

    /// <summary>Executes the dispose async operation.</summary>
    public async ValueTask DisposeAsync()
    {
        if (OwnsStream)
            await Content.DisposeAsync();
    }
}

/// <summary>Represents a file object metadata request.</summary>
public sealed record FileObjectMetadataRequest
{
    /// <summary>Gets or sets the bucket ID.</summary>
    public required FileBucketId BucketId { get; init; }
    /// <summary>Gets or sets the object ID.</summary>
    public required FileObjectId ObjectId { get; init; }
}

/// <summary>Represents a file object list request.</summary>
public sealed record FileObjectListRequest
{
    /// <summary>Gets or sets the bucket ID.</summary>
    public required FileBucketId BucketId { get; init; }
    /// <summary>Gets or sets the prefix.</summary>
    public FileObjectKey? Prefix { get; init; }
    /// <summary>Gets or sets the limit.</summary>
    public int? Limit { get; init; }
    /// <summary>Gets or sets the cursor.</summary>
    public string? Cursor { get; init; }
}

/// <summary>Represents a file object list result.</summary>
public sealed record FileObjectListResult
{
    /// <summary>Gets or sets the items.</summary>
    public required FileObjectMetadata[] Items { get; init; }
    /// <summary>Gets or sets the next cursor.</summary>
    public string? NextCursor { get; init; }
}

/// <summary>Represents a file object delete request.</summary>
public sealed record FileObjectDeleteRequest
{
    /// <summary>Gets or sets the bucket ID.</summary>
    public required FileBucketId BucketId { get; init; }
    /// <summary>Gets or sets the object ID.</summary>
    public required FileObjectId ObjectId { get; init; }
}

/// <summary>Defines the ifile object service contract.</summary>
public interface IFileObjectService
{
    /// <summary>Executes the upload async operation.</summary>
    ValueTask<OperationResult<FileObjectUploadResult>> UploadAsync(FileObjectUploadRequest request, FileOperationContext context, CancellationToken cancellationToken = default);
    /// <summary>Executes the open download async operation.</summary>
    ValueTask<OperationResult<FileObjectDownloadResult>> OpenDownloadAsync(FileObjectDownloadRequest request, FileOperationContext context, CancellationToken cancellationToken = default);
    /// <summary>Executes the get metadata async operation.</summary>
    ValueTask<OperationResult<FileObjectMetadata>> GetMetadataAsync(FileObjectMetadataRequest request, FileOperationContext context, CancellationToken cancellationToken = default);
    /// <summary>Executes the delete async operation.</summary>
    ValueTask<OperationResult> DeleteAsync(FileObjectDeleteRequest request, FileOperationContext context, CancellationToken cancellationToken = default);
    /// <summary>Executes the list metadata async operation.</summary>
    ValueTask<OperationResult<FileObjectListResult>> ListMetadataAsync(FileObjectListRequest request, FileOperationContext context, CancellationToken cancellationToken = default);
}

/// <summary>Represents a file operation context.</summary>
public sealed record FileOperationContext
{
    /// <summary>Gets or sets the subject ID.</summary>
    public string? SubjectId { get; init; }
    /// <summary>Gets or sets the tenant ID.</summary>
    public string? TenantId { get; init; }
    /// <summary>Gets or sets the correlation ID.</summary>
    public string? CorrelationId { get; init; }
    /// <summary>Gets or sets the is admin.</summary>
    public bool IsAdmin { get; init; }
}
