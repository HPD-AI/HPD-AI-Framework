using System.Collections.Concurrent;
using System.Diagnostics;
using HPD.Base.Observability;
using HPD.Base.Tests.Observability;
using HPD.Base.Runtime.Results;

namespace HPD.Base.Files.Tests.Observability;

public sealed class FileServiceTelemetryTests
{
    [Fact]
    public async Task FileServiceSpansAndMetricsDoNotLeakFileIdentityOrContentMarkers()
    {
        using var activities = new ActivityCollector(HPDBaseActivitySourceNames.Files);
        using var metrics = new MeterCollector(HPDBaseMeterNames.Files);
        var services = Services();
        services.AddSingleton<IFilePolicyOrchestrator, AllowFilePolicy>();
        services.AddSingleton<IFileStorageProvider, TelemetryProvider>();
        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IFileObjectService>();
        var context = new FileOperationContext
        {
            SubjectId = "subject-secret",
            TenantId = "tenant-secret",
            CorrelationId = "corr-secret"
        };

        var upload = await service.UploadAsync(new FileObjectUploadRequest
        {
            BucketId = new FileBucketId("bucket-secret"),
            Key = new FileObjectKey("object-key-secret"),
            Name = "file-name-secret.txt",
            ContentType = "text/plain",
            SizeBytes = 14,
            Checksum = new FileObjectChecksum("checksum-secret"),
            Overwrite = true,
            Content = new MemoryStream("content-secret"u8.ToArray())
        }, context);
        await service.OpenDownloadAsync(new FileObjectDownloadRequest { BucketId = new FileBucketId("bucket-secret"), ObjectId = new FileObjectId("object-id-secret") }, context);
        await service.GetMetadataAsync(new FileObjectMetadataRequest { BucketId = new FileBucketId("bucket-secret"), ObjectId = new FileObjectId("object-id-secret") }, context);
        await service.ListMetadataAsync(new FileObjectListRequest { BucketId = new FileBucketId("bucket-secret"), Prefix = new FileObjectKey("object-key-secret") }, context);
        await service.DeleteAsync(new FileObjectDeleteRequest { BucketId = new FileBucketId("bucket-secret"), ObjectId = new FileObjectId("object-id-secret") }, context);
        await service.UploadAsync(new FileObjectUploadRequest
        {
            BucketId = new FileBucketId("bucket-secret"),
            Key = new FileObjectKey("bad key secret"),
            ContentType = "text/plain",
            SizeBytes = 14,
            Content = new MemoryStream("content-secret"u8.ToArray())
        }, context);

        upload.Status.Should().Be(OperationStatus.Created);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.FilesObjectUpload);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.FilesObjectDownloadOpen);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.FilesObjectMetadataGet);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.FilesObjectList);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.FilesObjectDelete);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.FilesOperations);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.FilesOperationDuration);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.FilesUploadBytes);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.FilesDownloadBytes);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.FilesPolicyEvaluations);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.FilesValidationFailures);

        var forbidden = new[]
        {
            "bucket-secret",
            "object-id-secret",
            "object-key-secret",
            "file-name-secret",
            "checksum-secret",
            "subject-secret",
            "tenant-secret",
            "corr-secret",
            "content-secret",
            "bad key secret",
            "etag-secret"
        };
        activities.Stopped.Should().NotContain(activity => TagValues(activity).Any(value => forbidden.Any(marker => value.Contains(marker, StringComparison.Ordinal))));
        Assert.Contains(activities.Stopped, activity => activity.TagObjects.Any(tag =>
            tag.Key == HPDBaseTelemetryTags.CorrelationIdPresent &&
            tag.Value is true));
    }

    [Fact]
    public async Task FileOperationsWorkWithoutConfiguredTelemetryListeners()
    {
        var services = Services();
        services.AddSingleton<IFilePolicyOrchestrator, AllowFilePolicy>();
        services.AddSingleton<IFileStorageProvider, TelemetryProvider>();
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IFileObjectService>().UploadAsync(new FileObjectUploadRequest
        {
            BucketId = new FileBucketId("bucket-secret"),
            Key = new FileObjectKey("no-listener.txt"),
            ContentType = "text/plain",
            SizeBytes = 11,
            Content = new MemoryStream("no-listener"u8.ToArray())
        }, new FileOperationContext());

        result.Status.Should().Be(OperationStatus.Created);
        result.Value.Should().NotBeNull();
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddHPDBaseRuntime();
        services.AddHPDBaseFiles(options =>
        {
            options.Buckets.Add(new FileBucketDescriptor
            {
                BucketId = new FileBucketId("bucket-secret"),
                DisplayName = "Assets",
                ProviderRef = new FileProviderRef("telemetry"),
                AllowedContentTypes = ["text/plain"],
                MaxObjectBytes = 1024,
                AllowOverwrite = true
            });
        });
        return services;
    }

    private static string[] TagValues(Activity activity) =>
        activity.TagObjects.Select(tag => Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).ToArray();

    private sealed class AllowFilePolicy : IFilePolicyOrchestrator
    {
        public ValueTask<OperationResult<FilePolicyEvaluation>> EvaluateAsync(FilePolicyRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new OperationResult<FilePolicyEvaluation>
            {
                Status = OperationStatus.Ok,
                Value = new FilePolicyEvaluation { Allowed = true }
            });
    }

    private sealed class TelemetryProvider : IFileStorageProvider
    {
        public FileProviderRef ProviderRef => new("telemetry");

        public ValueTask<OperationResult<FileObjectUploadResult>> UploadAsync(FileBucketDescriptor bucket, FileObjectUploadRequest request, FileOperationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(OperationResults.Created(new FileObjectUploadResult
            {
                Metadata = Metadata(bucket.BucketId),
                Created = true
            }));

        public ValueTask<OperationResult<FileObjectDownloadResult>> OpenDownloadAsync(FileBucketDescriptor bucket, FileObjectDownloadRequest request, FileOperationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(OperationResults.Ok(new FileObjectDownloadResult
            {
                Metadata = Metadata(bucket.BucketId),
                Content = new MemoryStream("content-secret"u8.ToArray()),
                ContentLength = 14,
                ContentType = "text/plain",
                ETag = "etag-secret",
                OwnsStream = true
            }));

        public ValueTask<OperationResult<FileObjectMetadata>> GetMetadataAsync(FileBucketDescriptor bucket, FileObjectMetadataRequest request, FileOperationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(OperationResults.Ok(Metadata(bucket.BucketId)));

        public ValueTask<OperationResult> DeleteAsync(FileBucketDescriptor bucket, FileObjectDeleteRequest request, FileOperationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(OperationResults.NoContent());

        public ValueTask<OperationResult<FileObjectListResult>> ListMetadataAsync(FileBucketDescriptor bucket, FileObjectListRequest request, FileOperationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(OperationResults.Ok(new FileObjectListResult { Items = [Metadata(bucket.BucketId)] }));

        private static FileObjectMetadata Metadata(FileBucketId bucketId) => new()
        {
            BucketId = bucketId,
            ObjectId = new FileObjectId("object-id-secret"),
            Key = new FileObjectKey("object-key-secret"),
            Name = "file-name-secret.txt",
            ContentType = "text/plain",
            SizeBytes = 14,
            Checksum = new FileObjectChecksum("checksum-secret"),
            OwnerSubjectId = "subject-secret",
            TenantId = "tenant-secret"
        };
    }

}
