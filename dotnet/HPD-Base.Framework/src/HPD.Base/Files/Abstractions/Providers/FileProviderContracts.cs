
namespace HPD.Base;

public interface IFileBucketRegistry
{
    ValueTask<FileBucketDescriptor?> FindAsync(FileBucketId bucketId, CancellationToken cancellationToken = default);
    ValueTask<FileBucketDescriptor[]> ListAsync(CancellationToken cancellationToken = default);
}

public interface IFileStorageProvider
{
    FileProviderRef ProviderRef { get; }
    ValueTask<OperationResult<FileObjectUploadResult>> UploadAsync(FileBucketDescriptor bucket, FileObjectUploadRequest request, FileOperationContext context, CancellationToken cancellationToken = default);
    ValueTask<OperationResult<FileObjectDownloadResult>> OpenDownloadAsync(FileBucketDescriptor bucket, FileObjectDownloadRequest request, FileOperationContext context, CancellationToken cancellationToken = default);
    ValueTask<OperationResult<FileObjectMetadata>> GetMetadataAsync(FileBucketDescriptor bucket, FileObjectMetadataRequest request, FileOperationContext context, CancellationToken cancellationToken = default);
    ValueTask<OperationResult> DeleteAsync(FileBucketDescriptor bucket, FileObjectDeleteRequest request, FileOperationContext context, CancellationToken cancellationToken = default);
    ValueTask<OperationResult<FileObjectListResult>> ListMetadataAsync(FileBucketDescriptor bucket, FileObjectListRequest request, FileOperationContext context, CancellationToken cancellationToken = default);
}

public interface IFileStorageProviderResolver
{
    ValueTask<IFileStorageProvider?> ResolveAsync(FileBucketDescriptor bucket, CancellationToken cancellationToken = default);
}
