namespace HPD.Base.Files.ProviderConformance;

public interface IFileStorageProviderConformanceFixture
{
    FileProviderRef ProviderRef { get; }
    FileBucketDescriptor Bucket { get; }
    ValueTask<IFileStorageProvider> CreateProviderAsync(CancellationToken cancellationToken = default);
    ValueTask ResetAsync(CancellationToken cancellationToken = default);
}
