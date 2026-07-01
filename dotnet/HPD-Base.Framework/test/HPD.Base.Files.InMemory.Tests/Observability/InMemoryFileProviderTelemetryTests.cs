using System.Collections.Concurrent;
using System.Diagnostics;
using HPD.Base.Observability;
using HPD.Base.Tests.Observability;

namespace HPD.Base.Files.InMemory.Tests.Observability;

public sealed class InMemoryFileProviderTelemetryTests
{
    [Fact]
    public async Task ProviderSpansAndMetricsDoNotLeakFileIdentityOrContentMarkers()
    {
        using var activities = new ActivityCollector(HPDBaseActivitySourceNames.FilesInMemory);
        using var metrics = new MeterCollector(HPDBaseMeterNames.FilesInMemory);
        var provider = new InMemoryFileStorageProvider(Options.Create(new HPDBaseFilesInMemoryOptions()));
        var bucket = Bucket();
        var context = new FileOperationContext
        {
            SubjectId = "subject-secret",
            TenantId = "tenant-secret",
            CorrelationId = "corr-secret"
        };

        var upload = await provider.UploadAsync(bucket, new FileObjectUploadRequest
        {
            BucketId = new FileBucketId("bucket-secret"),
            Key = new FileObjectKey("object-key-secret"),
            Name = "file-name-secret.txt",
            ContentType = "text/plain",
            SizeBytes = 14,
            Checksum = null,
            Content = new MemoryStream("content-secret"u8.ToArray())
        }, context);
        var objectId = upload.Value!.Metadata.ObjectId;
        await provider.OpenDownloadAsync(bucket, new FileObjectDownloadRequest { BucketId = bucket.BucketId, ObjectId = objectId }, context);
        await provider.GetMetadataAsync(bucket, new FileObjectMetadataRequest { BucketId = bucket.BucketId, ObjectId = objectId }, context);
        await provider.ListMetadataAsync(bucket, new FileObjectListRequest { BucketId = bucket.BucketId, Prefix = new FileObjectKey("object-key-secret") }, context);
        await provider.DeleteAsync(bucket, new FileObjectDeleteRequest { BucketId = bucket.BucketId, ObjectId = objectId }, context);

        upload.Status.Should().Be(OperationStatus.Created);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.FilesProviderUpload);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.FilesProviderDownloadOpen);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.FilesProviderMetadataGet);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.FilesProviderList);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.FilesProviderDelete);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.FilesProviderOperations);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.FilesProviderDuration);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.FilesUploadBytes);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.FilesDownloadBytes);

        var forbidden = new[]
        {
            "bucket-secret",
            objectId.Value,
            "object-key-secret",
            "file-name-secret",
            "subject-secret",
            "tenant-secret",
            "corr-secret",
            "content-secret",
            "rev_"
        };
        activities.Stopped.Should().NotContain(activity => TagValues(activity).Any(value => forbidden.Any(marker => value.Contains(marker, StringComparison.Ordinal))));
    }

    private static FileBucketDescriptor Bucket() => new()
    {
        BucketId = new FileBucketId("bucket-secret"),
        ProviderRef = new FileProviderRef("inmemory")
    };

    private static string[] TagValues(Activity activity) =>
        activity.TagObjects.Select(tag => Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).ToArray();

}
