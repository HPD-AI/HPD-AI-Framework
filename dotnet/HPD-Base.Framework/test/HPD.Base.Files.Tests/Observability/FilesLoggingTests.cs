using HPD.Base.Tests.Observability;
using HPD.Base.Runtime.Results;
using Microsoft.Extensions.Logging;

namespace HPD.Base.Files.Tests.Observability;

public sealed class FilesLoggingTests
{
    [Fact]
    public async Task MissingProviderEmitsExactSafeOwnerEvent()
    {
        using var logs = new LogCollector();
        using var provider = Services(logs, fileProvider: null, allow: true);

        var result = await Service(provider).GetMetadataAsync(MetadataRequest(), UnsafeContext());

        result.Status.Should().Be(OperationStatus.CapabilityUnavailable);
        AssertContract(Assert.Single(logs.RecordsFor(4000)));
        logs.Records.Should().ContainSingle();
        AssertSafety(logs.Records);
    }

    [Fact]
    public async Task ReturnedProviderFailureEmitsOneClassifiedEventWithoutProviderDetails()
    {
        using var logs = new LogCollector();
        using var provider = Services(logs, new TestProvider(ProviderBehavior.ReturnFailure), allow: true);

        var result = await Service(provider).GetMetadataAsync(MetadataRequest(), UnsafeContext());

        result.Status.Should().Be(OperationStatus.StoreError);
        var record = Assert.Single(logs.RecordsFor(4001));
        AssertContract(record);
        Assert.Equal("files.provider.failure", State(record, "ErrorCode"));
        logs.Records.Should().ContainSingle();
        AssertSafety(logs.Records);
    }

    [Fact]
    public async Task ThrowingProviderEmitsOneSafeEventAndPreservesThrowBehavior()
    {
        using var logs = new LogCollector();
        using var provider = Services(logs, new TestProvider(ProviderBehavior.Throw), allow: true);

        var action = () => Service(provider).GetMetadataAsync(MetadataRequest(), UnsafeContext()).AsTask();

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*__HPD_L23_FILE_EXCEPTION_MESSAGE__*");
        var record = Assert.Single(logs.RecordsFor(4001));
        AssertContract(record);
        Assert.Equal("files.provider.exception", State(record, "ErrorCode"));
        logs.Records.Should().ContainSingle();
        AssertSafety(logs.Records);
    }

    [Fact]
    public async Task PolicyAndValidationRejectionsAreDebugOnlyAndExact()
    {
        using var logs = new LogCollector();
        using var deniedProvider = Services(logs, fileProvider: null, allow: false);

        var denied = await Service(deniedProvider).GetMetadataAsync(MetadataRequest(), UnsafeContext());

        denied.Status.Should().Be(OperationStatus.PolicyDenied);
        AssertContract(Assert.Single(logs.RecordsFor(4002)));

        using var validationLogs = new LogCollector();
        using var validationProvider = Services(validationLogs, fileProvider: null, allow: true);
        var rejected = await Service(validationProvider).UploadAsync(new FileObjectUploadRequest
        {
            BucketId = new FileBucketId("__HPD_L23_FILE_BUCKET_ID__"),
            Content = Stream.Null
        }, UnsafeContext());

        rejected.Status.Should().Be(OperationStatus.ValidationFailed);
        AssertContract(Assert.Single(validationLogs.RecordsFor(4003)));
        AssertSafety(logs.Records.Concat(validationLogs.Records));
    }

    [Fact]
    public async Task SuccessfulProviderOperationProducesNoFilesLog()
    {
        using var logs = new LogCollector();
        using var provider = Services(logs, new TestProvider(ProviderBehavior.Success), allow: true);

        var result = await Service(provider).GetMetadataAsync(MetadataRequest(), UnsafeContext());

        result.Status.Should().Be(OperationStatus.Ok);
        logs.Records.Where(record => record.Category.StartsWith("HPD.Base.Files.", StringComparison.Ordinal))
            .Should().BeEmpty();
    }

    private static ServiceProvider Services(
        LogCollector logs,
        IFileStorageProvider? fileProvider,
        bool allow)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(logs);
        });
        services.AddHPDBaseRuntime();
        services.AddHPDBaseFiles(options =>
        {
            options.Buckets.Add(new FileBucketDescriptor
            {
                BucketId = new FileBucketId("__HPD_L23_FILE_BUCKET_ID__"),
                ProviderRef = new FileProviderRef("__HPD_L23_FILE_PROVIDER_REF__")
            });
        });
        services.AddSingleton<IFilePolicyOrchestrator>(
            allow ? new TestPolicy(true) : new TestPolicy(false));
        if (fileProvider is not null)
            services.AddSingleton(fileProvider);
        return services.BuildServiceProvider();
    }

    private static IFileObjectService Service(IServiceProvider provider) =>
        provider.GetRequiredService<IFileObjectService>();

    private static FileObjectMetadataRequest MetadataRequest() => new()
    {
        BucketId = new FileBucketId("__HPD_L23_FILE_BUCKET_ID__"),
        ObjectId = new FileObjectId("__HPD_L23_FILE_OBJECT_ID__")
    };

    private static FileOperationContext UnsafeContext() => new()
    {
        SubjectId = "__HPD_L23_FILE_SUBJECT_ID__",
        TenantId = "__HPD_L23_FILE_TENANT_ID__",
        CorrelationId = "__HPD_L23_FILE_CORRELATION_ID__"
    };

    private static object? State(CapturedLogRecord record, string key) =>
        Assert.Single(record.State, item => item.Key == key).Value;

    private static void AssertContract(CapturedLogRecord record)
    {
        var contract = Assert.Single(
            HPDBaseLogEventRegistry.Active,
            item => item.Owner == "HPD.Base.Files" && item.Id == record.EventId.Id);
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
        Assert.Empty(record.Scopes);
        Assert.StartsWith("HPD.Base.Files.", record.Category, StringComparison.Ordinal);
    }

    private static void AssertSafety(IEnumerable<CapturedLogRecord> records)
    {
        var captured = records.ToArray();
        LogSafetyInspector.AssertNoExceptions(captured);
        LogSafetyInspector.AssertNoScopes(captured);
        LogSafetyInspector.AssertSafe(
            captured,
            "__HPD_L23_FILE_BUCKET_ID__",
            "__HPD_L23_FILE_OBJECT_ID__",
            "__HPD_L23_FILE_PROVIDER_REF__",
            "__HPD_L23_FILE_SUBJECT_ID__",
            "__HPD_L23_FILE_TENANT_ID__",
            "__HPD_L23_FILE_CORRELATION_ID__",
            "__HPD_L23_FILE_PROVIDER_CODE__",
            "__HPD_L23_FILE_PROVIDER_MESSAGE__",
            "__HPD_L23_FILE_EXCEPTION_MESSAGE__");
    }

    private enum ProviderBehavior
    {
        Success,
        ReturnFailure,
        Throw
    }

    private sealed class TestPolicy(bool allowed) : IFilePolicyOrchestrator
    {
        public ValueTask<OperationResult<FilePolicyEvaluation>> EvaluateAsync(
            FilePolicyRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(allowed
                ? new OperationResult<FilePolicyEvaluation>
                {
                    Status = OperationStatus.Ok,
                    Value = new FilePolicyEvaluation { Allowed = true }
                }
                : OperationResults.PolicyDenied<FilePolicyEvaluation>(new BaseError
                {
                    Code = "__HPD_L23_FILE_POLICY_CODE__",
                    Message = "__HPD_L23_FILE_POLICY_MESSAGE__",
                    Category = ErrorCategory.Authorization
                }));
    }

    private sealed class TestProvider(ProviderBehavior behavior) : IFileStorageProvider
    {
        public FileProviderRef ProviderRef => new("__HPD_L23_FILE_PROVIDER_REF__");

        public ValueTask<OperationResult<FileObjectMetadata>> GetMetadataAsync(
            FileBucketDescriptor bucket,
            FileObjectMetadataRequest request,
            FileOperationContext context,
            CancellationToken cancellationToken = default) =>
            behavior switch
            {
                ProviderBehavior.Success => ValueTask.FromResult(new OperationResult<FileObjectMetadata>
                {
                    Status = OperationStatus.Ok,
                    Value = new FileObjectMetadata
                    {
                        BucketId = bucket.BucketId,
                        ObjectId = request.ObjectId
                    }
                }),
                ProviderBehavior.ReturnFailure => ValueTask.FromResult(OperationResults.StoreError<FileObjectMetadata>(
                    new BaseError
                    {
                        Code = "__HPD_L23_FILE_PROVIDER_CODE__",
                        Message = "__HPD_L23_FILE_PROVIDER_MESSAGE__",
                        Category = ErrorCategory.Store
                    })),
                _ => throw new InvalidOperationException("__HPD_L23_FILE_EXCEPTION_MESSAGE__")
            };

        public ValueTask<OperationResult<FileObjectUploadResult>> UploadAsync(
            FileBucketDescriptor bucket,
            FileObjectUploadRequest request,
            FileOperationContext context,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<OperationResult<FileObjectDownloadResult>> OpenDownloadAsync(
            FileBucketDescriptor bucket,
            FileObjectDownloadRequest request,
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
}
