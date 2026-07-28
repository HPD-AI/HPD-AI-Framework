using HPD.Base.Files.Providers;
using HPD.Base.Tests.Observability;
using Microsoft.Extensions.Logging;

namespace HPD.Base.Files.AspNetCore.Tests.Endpoints;

public sealed class FilesAspNetCoreLoggingTests
{
    [Fact]
    public async Task DownloadStreamFailureEmitsOneExactSafeOwnerEventAndStillPropagates()
    {
        using var logs = new LogCollector();
        await using var app = await CreateAsync(logs);
        var client = app.GetTestClient();

        var action = () => client.GetAsync(
            "/base/files/__HPD_L23_HTTP_BUCKET_ID__/objects/__HPD_L23_HTTP_OBJECT_ID__");

        await action.Should().ThrowAsync<Exception>();

        var record = Assert.Single(logs.RecordsFor(4500));
        var contract = Assert.Single(
            HPDBaseLogEventRegistry.Active,
            item => item.Owner == "HPD.Base.Files.AspNetCore" && item.Id == 4500);
        Assert.Equal(contract.Name, record.EventId.Name);
        Assert.Equal(contract.Level, record.Level);
        Assert.Equal(contract.Template, record.OriginalFormat);
        Assert.Equal(
            contract.Properties,
            record.State
                .Where(item => item.Key != "{OriginalFormat}")
                .Select(item => item.Key)
                .ToArray());
        Assert.Null(record.Exception);
        Assert.StartsWith("HPD.Base.Files.AspNetCore.", record.Category, StringComparison.Ordinal);
        logs.RecordsFor(4001).Should().BeEmpty();

        LogSafetyInspector.AssertSafe(
            [record with { Scopes = [] }],
            "__HPD_L23_HTTP_BUCKET_ID__",
            "__HPD_L23_HTTP_OBJECT_ID__",
            "__HPD_L23_HTTP_PROVIDER_REF__",
            "__HPD_L23_HTTP_STREAM_EXCEPTION__");
        LogSafetyInspector.AssertNoExceptions([record]);
    }

    private static async Task<WebApplication> CreateAsync(LogCollector logs)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddProvider(logs);
        builder.Services.AddHPDBaseRuntime();
        builder.Services.AddHPDBaseAspNetCore();
        builder.Services.AddHPDBaseFiles(options =>
        {
            options.Buckets.Add(new FileBucketDescriptor
            {
                BucketId = new FileBucketId("__HPD_L23_HTTP_BUCKET_ID__"),
                ProviderRef = new FileProviderRef("__HPD_L23_HTTP_PROVIDER_REF__")
            });
        });
        builder.Services.AddHPDBaseFilesAspNetCore();
        builder.Services.AddSingleton<IFilePolicyOrchestrator, AllowFilePolicy>();
        builder.Services.AddSingleton<IFileStorageProvider, ThrowingDownloadProvider>();

        var app = builder.Build();
        app.MapHPDBaseApi();
        app.MapHPDBaseFilesApi();
        await app.StartAsync();
        return app;
    }

    private sealed class AllowFilePolicy : IFilePolicyOrchestrator
    {
        public ValueTask<OperationResult<FilePolicyEvaluation>> EvaluateAsync(
            FilePolicyRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new OperationResult<FilePolicyEvaluation>
            {
                Status = OperationStatus.Ok,
                Value = new FilePolicyEvaluation { Allowed = true }
            });
    }

    private sealed class ThrowingDownloadProvider : IFileStorageProvider
    {
        public FileProviderRef ProviderRef => new("__HPD_L23_HTTP_PROVIDER_REF__");

        public ValueTask<OperationResult<FileObjectDownloadResult>> OpenDownloadAsync(
            FileBucketDescriptor bucket,
            FileObjectDownloadRequest request,
            FileOperationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new OperationResult<FileObjectDownloadResult>
            {
                Status = OperationStatus.Ok,
                Value = new FileObjectDownloadResult
                {
                    Metadata = new FileObjectMetadata
                    {
                        BucketId = bucket.BucketId,
                        ObjectId = request.ObjectId
                    },
                    Content = new ThrowingReadStream(),
                    ContentType = "application/octet-stream"
                }
            });

        public ValueTask<OperationResult<FileObjectUploadResult>> UploadAsync(
            FileBucketDescriptor bucket,
            FileObjectUploadRequest request,
            FileOperationContext context,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<OperationResult<FileObjectMetadata>> GetMetadataAsync(
            FileBucketDescriptor bucket,
            FileObjectMetadataRequest request,
            FileOperationContext context,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<OperationResult> DeleteAsync(
            FileBucketDescriptor bucket,
            FileObjectDeleteRequest request,
            FileOperationContext context,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<OperationResult<FileObjectListResult>> ListMetadataAsync(
            FileBucketDescriptor bucket,
            FileObjectListRequest request,
            FileOperationContext context,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() =>
            throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException("__HPD_L23_HTTP_STREAM_EXCEPTION__");

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(
                new IOException("__HPD_L23_HTTP_STREAM_EXCEPTION__"));

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
