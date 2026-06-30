namespace HPD.Base.Files.InMemory.Tests.Conformance;

public sealed class InMemoryFileProviderConformanceFixture : IFileStorageProviderConformanceFixture
{
    private readonly InMemoryFileStorageProvider _provider = new(Options.Create(new HPDBaseFilesInMemoryOptions
    {
        ProviderRef = new FileProviderRef("inmemory")
    }));

    public FileProviderRef ProviderRef => new("inmemory");

    public FileBucketDescriptor Bucket { get; } = new()
    {
        BucketId = new FileBucketId("conformance"),
        ProviderRef = new FileProviderRef("inmemory"),
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
