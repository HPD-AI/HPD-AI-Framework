
namespace HPD.Base;

/// <summary>Defines the ifile bucket registry contract.</summary>
public interface IFileBucketRegistry
{
    /// <summary>Executes the find async operation.</summary>
    ValueTask<FileBucketDescriptor?> FindAsync(FileBucketId bucketId, CancellationToken cancellationToken = default);
    /// <summary>Executes the list async operation.</summary>
    ValueTask<FileBucketDescriptor[]> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>Defines the ifile storage provider contract.</summary>
public interface IFileStorageProvider
{
    /// <summary>Gets the provider ref.</summary>
    FileProviderRef ProviderRef { get; }
    /// <summary>Executes the upload async operation.</summary>
    ValueTask<OperationResult<FileObjectUploadResult>> UploadAsync(FileBucketDescriptor bucket, FileObjectUploadRequest request, FileOperationContext context, CancellationToken cancellationToken = default);
    /// <summary>Executes the open download async operation.</summary>
    ValueTask<OperationResult<FileObjectDownloadResult>> OpenDownloadAsync(FileBucketDescriptor bucket, FileObjectDownloadRequest request, FileOperationContext context, CancellationToken cancellationToken = default);
    /// <summary>Executes the get metadata async operation.</summary>
    ValueTask<OperationResult<FileObjectMetadata>> GetMetadataAsync(FileBucketDescriptor bucket, FileObjectMetadataRequest request, FileOperationContext context, CancellationToken cancellationToken = default);
    /// <summary>Executes the delete async operation.</summary>
    ValueTask<OperationResult> DeleteAsync(FileBucketDescriptor bucket, FileObjectDeleteRequest request, FileOperationContext context, CancellationToken cancellationToken = default);
    /// <summary>Executes the list metadata async operation.</summary>
    ValueTask<OperationResult<FileObjectListResult>> ListMetadataAsync(FileBucketDescriptor bucket, FileObjectListRequest request, FileOperationContext context, CancellationToken cancellationToken = default);
}

/// <summary>Defines the ifile storage provider resolver contract.</summary>
public interface IFileStorageProviderResolver
{
    /// <summary>Executes the resolve async operation.</summary>
    ValueTask<IFileStorageProvider?> ResolveAsync(FileBucketDescriptor bucket, CancellationToken cancellationToken = default);
}
