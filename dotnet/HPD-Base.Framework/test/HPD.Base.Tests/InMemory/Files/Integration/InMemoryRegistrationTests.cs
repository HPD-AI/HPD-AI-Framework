namespace HPD.Base.Tests.InMemory.Files.Integration;

public sealed class InMemoryRegistrationTests
{
    [Fact]
    public void RegistersProviderWithConfiguredRef()
    {
        var services = new ServiceCollection();
        services.AddHPDBaseFilesInMemoryProvider(options => options.ProviderRef = new FileProviderRef("custom"));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IFileStorageProvider>().ProviderRef.Should().Be(new FileProviderRef("custom"));
    }

    [Fact]
    public async Task ProviderRejectsUploadsAboveBufferedLimitWithoutUnboundedRead()
    {
        var provider = CreateProvider(maxBufferedBytes: 4);

        var result = await provider.UploadAsync(Bucket(), new FileObjectUploadRequest
        {
            BucketId = new FileBucketId("assets"),
            Key = new FileObjectKey("docs/too-large.txt"),
            Content = new MemoryStream("hello"u8.ToArray())
        }, new FileOperationContext());

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Error?.Code.Should().Be("hpd.base.files.sizeExceeded");
    }

    [Fact]
    public async Task ProviderRejectsSha256ChecksumMismatch()
    {
        var provider = CreateProvider();

        var result = await provider.UploadAsync(Bucket(), new FileObjectUploadRequest
        {
            BucketId = new FileBucketId("assets"),
            Key = new FileObjectKey("docs/checksum.txt"),
            Checksum = new FileObjectChecksum("sha256:" + new string('0', 64)),
            Content = new MemoryStream("hello"u8.ToArray())
        }, new FileOperationContext());

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Error?.Code.Should().Be("hpd.base.files.checksumRejected");
    }

    private static InMemoryFileStorageProvider CreateProvider(long maxBufferedBytes = 104_857_600) =>
        new(Options.Create(new HPDBaseInMemoryFileStoreOptions { MaxBufferedObjectBytes = maxBufferedBytes }));

    private static FileBucketDescriptor Bucket() => new()
    {
        BucketId = new FileBucketId("assets"),
        ProviderRef = new FileProviderRef("inmemory")
    };
}
