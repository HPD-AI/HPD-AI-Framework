namespace HPD.Base.Tests.Volatile.Files.Conformance;

public sealed class VolatileFileProviderConformanceFixture : IFileStorageProviderConformanceFixture
{
    private readonly VolatileFileStorageProvider _provider = new(Options.Create(new HPDBaseVolatileFileStoreOptions
    {
        ProviderRef = new FileProviderRef("volatile")
    }));

    public FileProviderRef ProviderRef => new("volatile");

    public FileBucketDescriptor Bucket { get; } = new()
    {
        BucketId = new FileBucketId("conformance"),
        ProviderRef = new FileProviderRef("volatile"),
        AllowOverwrite = true
    };

    public ValueTask<IFileStorageProvider> CreateProviderAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IFileStorageProvider>(_provider);

    public ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        _provider.Clear();
        return ValueTask.CompletedTask;
    }
}
