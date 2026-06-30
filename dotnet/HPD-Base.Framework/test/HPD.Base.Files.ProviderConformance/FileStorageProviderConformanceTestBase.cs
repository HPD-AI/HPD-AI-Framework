namespace HPD.Base.Files.ProviderConformance;

public abstract class FileStorageProviderConformanceTestBase<TFixture> : IAsyncLifetime
    where TFixture : IFileStorageProviderConformanceFixture, new()
{
    protected TFixture Fixture { get; } = new();
    protected FileBucketDescriptor Bucket => Fixture.Bucket;

    public async Task InitializeAsync() => await Fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    protected ValueTask<IFileStorageProvider> CreateProviderAsync() => Fixture.CreateProviderAsync();

    protected static FileOperationContext UserContext(string subject = "user-1") => new()
    {
        SubjectId = subject,
        TenantId = "tenant-1",
        CorrelationId = "corr-1"
    };

    protected static FileObjectUploadRequest Upload(
        FileBucketId bucketId,
        string key,
        string content,
        string contentType = "text/plain",
        bool overwrite = false) => new()
    {
        BucketId = bucketId,
        Key = new FileObjectKey(key),
        Name = Path.GetFileName(key),
        ContentType = contentType,
        SizeBytes = Encoding.UTF8.GetByteCount(content),
        Content = new MemoryStream(Encoding.UTF8.GetBytes(content)),
        Overwrite = overwrite,
        Metadata = new Dictionary<string, string> { ["purpose"] = "conformance" }
    };

    protected static async Task<string> ReadAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}
