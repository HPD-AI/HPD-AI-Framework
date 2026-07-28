namespace HPD.Base.Files.Tests.Runtime;

public sealed class FileObjectServiceFailClosedTests
{
    [Fact]
    public async Task UploadFailsClosedWhenPolicyIsMissing()
    {
        using var provider = Services().BuildServiceProvider();
        var service = provider.GetRequiredService<IFileObjectService>();

        var result = await service.UploadAsync(new FileObjectUploadRequest
        {
            BucketId = new FileBucketId("assets"),
            Key = new FileObjectKey("safe/file.txt"),
            ContentType = "text/plain",
            SizeBytes = 12,
            Content = Stream.Null
        }, new FileOperationContext());

        result.Status.Should().Be(OperationStatus.PolicyDenied);
        result.Error?.Code.Should().Be(FileDiagnosticIds.PolicyUnavailable);
    }

    [Fact]
    public async Task UnknownBucketReturnsNotFound()
    {
        using var provider = Services().BuildServiceProvider();
        var service = provider.GetRequiredService<IFileObjectService>();

        var result = await service.GetMetadataAsync(new FileObjectMetadataRequest
        {
            BucketId = new FileBucketId("missing"),
            ObjectId = new FileObjectId("obj_1")
        }, new FileOperationContext());

        result.Status.Should().Be(OperationStatus.NotFound);
    }

    [Fact]
    public async Task NoProviderFailsClosedAfterPolicyAllows()
    {
        var services = Services();
        services.AddSingleton<IFilePolicyOrchestrator, AllowFilePolicy>();
        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IFileObjectService>();

        var result = await service.GetMetadataAsync(new FileObjectMetadataRequest
        {
            BucketId = new FileBucketId("assets"),
            ObjectId = new FileObjectId("obj_1")
        }, new FileOperationContext());

        result.Status.Should().Be(OperationStatus.CapabilityUnavailable);
        result.Error?.Code.Should().Be(FileDiagnosticIds.NoProvider);
    }

    [Fact]
    public async Task DisabledBucketRejectsOperations()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBaseRuntime();
        services.AddHPDBaseFiles(options =>
        {
            options.Buckets.Add(new FileBucketDescriptor
            {
                BucketId = new FileBucketId("disabled"),
                Enabled = false,
                ProviderRef = new FileProviderRef("none")
            });
        });
        services.AddSingleton<IFilePolicyOrchestrator, AllowFilePolicy>();
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IFileObjectService>().GetMetadataAsync(new FileObjectMetadataRequest
        {
            BucketId = new FileBucketId("disabled"),
            ObjectId = new FileObjectId("obj_1")
        }, new FileOperationContext());

        result.Status.Should().Be(OperationStatus.CapabilityUnavailable);
        result.Error?.Code.Should().Be(FileDiagnosticIds.BucketDisabled);
    }

    [Theory]
    [InlineData(2048, "text/plain", "sha256:abc", false, OperationStatus.ValidationFailed, FileDiagnosticIds.SizeExceeded)]
    [InlineData(12, "image/png", "sha256:abc", false, OperationStatus.ValidationFailed, FileDiagnosticIds.ContentTypeRejected)]
    [InlineData(12, "text/plain", "bad checksum", false, OperationStatus.ValidationFailed, FileDiagnosticIds.ChecksumRejected)]
    [InlineData(12, "text/plain", "sha256:abc", true, OperationStatus.Conflict, "files.object.overwriteRejected")]
    public async Task UploadValidatesBucketConstraints(long sizeBytes, string contentType, string checksum, bool overwrite, OperationStatus status, string code)
    {
        var services = Services(requireChecksum: true);
        services.AddSingleton<IFilePolicyOrchestrator, AllowFilePolicy>();
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IFileObjectService>().UploadAsync(new FileObjectUploadRequest
        {
            BucketId = new FileBucketId("assets"),
            Key = new FileObjectKey("safe/file.txt"),
            ContentType = contentType,
            SizeBytes = sizeBytes,
            Checksum = new FileObjectChecksum(checksum),
            Overwrite = overwrite,
            Content = Stream.Null
        }, new FileOperationContext());

        result.Status.Should().Be(status);
        result.Error?.Code.Should().Be(code);
    }

    [Fact]
    public async Task UploadRejectsUnknownSizeWhenLimitIsConfigured()
    {
        var services = Services();
        services.AddSingleton<IFilePolicyOrchestrator, AllowFilePolicy>();
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IFileObjectService>().UploadAsync(new FileObjectUploadRequest
        {
            BucketId = new FileBucketId("assets"),
            Key = new FileObjectKey("safe/file.txt"),
            ContentType = "text/plain",
            Content = Stream.Null
        }, new FileOperationContext());

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Error?.Code.Should().Be(FileDiagnosticIds.SizeExceeded);
    }

    [Fact]
    public async Task ProviderMetadataIsRedactedForPublicRuntimeResults()
    {
        var services = Services();
        services.AddSingleton<IFilePolicyOrchestrator, AllowFilePolicy>();
        services.AddSingleton<IFileStorageProvider, MetadataProvider>();
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IFileObjectService>().GetMetadataAsync(new FileObjectMetadataRequest
        {
            BucketId = new FileBucketId("assets"),
            ObjectId = new FileObjectId("obj_1")
        }, new FileOperationContext());

        result.Status.Should().Be(OperationStatus.Ok);
        result.Value!.Key.Should().BeNull();
        result.Value.Name.Should().BeNull();
        result.Value.Checksum.Should().BeNull();
        result.Value.OwnerSubjectId.Should().BeNull();
        result.Value.TenantId.Should().BeNull();
    }

    private static ServiceCollection Services(bool requireChecksum = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBaseRuntime();
        services.AddHPDBaseFiles(options =>
        {
            options.Buckets.Add(new FileBucketDescriptor
            {
                BucketId = new FileBucketId("assets"),
                DisplayName = "Assets",
                ProviderRef = new FileProviderRef("none"),
                AllowedContentTypes = ["text/plain"],
                MaxObjectBytes = 1024,
                RequireChecksum = requireChecksum
            });
        });
        return services;
    }

    private sealed class AllowFilePolicy : IFilePolicyOrchestrator
    {
        public ValueTask<OperationResult<FilePolicyEvaluation>> EvaluateAsync(FilePolicyRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new OperationResult<FilePolicyEvaluation>
            {
                Status = OperationStatus.Ok,
                Value = new FilePolicyEvaluation { Allowed = true }
            });
    }

    private sealed class MetadataProvider : IFileStorageProvider
    {
        public FileProviderRef ProviderRef => new("none");

        public ValueTask<OperationResult<FileObjectUploadResult>> UploadAsync(FileBucketDescriptor bucket, FileObjectUploadRequest request, FileOperationContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<OperationResult<FileObjectDownloadResult>> OpenDownloadAsync(FileBucketDescriptor bucket, FileObjectDownloadRequest request, FileOperationContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<OperationResult<FileObjectMetadata>> GetMetadataAsync(FileBucketDescriptor bucket, FileObjectMetadataRequest request, FileOperationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new OperationResult<FileObjectMetadata>
            {
                Status = OperationStatus.Ok,
                Value = new FileObjectMetadata
                {
                    BucketId = bucket.BucketId,
                    ObjectId = request.ObjectId,
                    Key = new FileObjectKey("private/key.txt"),
                    Name = "secret.txt",
                    Checksum = new FileObjectChecksum("sha256:secret"),
                    OwnerSubjectId = "owner",
                    TenantId = "tenant"
                }
            });

        public ValueTask<OperationResult> DeleteAsync(FileBucketDescriptor bucket, FileObjectDeleteRequest request, FileOperationContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<OperationResult<FileObjectListResult>> ListMetadataAsync(FileBucketDescriptor bucket, FileObjectListRequest request, FileOperationContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
