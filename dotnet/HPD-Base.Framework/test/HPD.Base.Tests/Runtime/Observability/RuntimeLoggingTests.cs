using System.Text.Json;
using HPD.Base;
using HPD.Base.Tests.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HPD.Base.Tests.Observability;

public sealed class RuntimeLoggingTests
{
    [Fact]
    public void ActiveRuntimeRegistryIsExactAndGloballyUnique()
    {
        var all = HPDBaseLogEventRegistry.Active;
        var active = all
            .Where(item => item.Owner == "HPD.Base.Runtime")
            .ToArray();

        Assert.Equal(8, active.Length);
        Assert.Equal(all.Length, all.Select(item => item.Id).Distinct().Count());
        Assert.Equal(all.Length, all.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(active, item =>
        {
            Assert.Equal("HPD.Base.Runtime", item.Owner);
            Assert.InRange(item.Id, 1000, 1499);
        });
    }

    [Fact]
    public void NormalizerEmitsExactMalformedResultContractsWithoutUnsafeState()
    {
        using var logs = new LogCollector();
        using var provider = Provider(logs);
        var normalizer = provider.GetRequiredService<IBaseResultNormalizer>();
        var operation = RuntimeTestData.Operation(BaseOperationKind.Get) with
        {
            CorrelationId = "__HPD_L23_CORRELATION_ID__",
            RecordId = "__HPD_L23_RECORD_ID__"
        };

        _ = normalizer.NormalizeStoreResult(
            new OperationResult<RecordEnvelope> { Status = OperationStatus.Ok },
            operation);
        _ = normalizer.NormalizeStoreResult(
            new OperationResult<RecordEnvelope> { Status = OperationStatus.NotFound },
            operation);

        AssertContract(Assert.Single(logs.RecordsFor(1001)));
        AssertContract(Assert.Single(logs.RecordsFor(1008)));
        AssertRuntimeSafety(logs.Records);
    }

    [Fact]
    public async Task MissingStoreEmitsOneSafeOwnerEvent()
    {
        using var logs = new LogCollector();
        using var provider = Provider(logs, registerStore: false);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
            "items",
            null,
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.List) with
            {
                CorrelationId = "__HPD_L23_CORRELATION_ID__"
            });

        Assert.Equal(OperationStatus.Unsupported, result.Status);
        AssertContract(Assert.Single(logs.RecordsFor(1002)));
        Assert.Single(logs.Records);
        AssertRuntimeSafety(logs.Records);
    }

    [Theory]
    [InlineData(true, 1009, LogLevel.Warning)]
    [InlineData(false, 1010, LogLevel.Error)]
    public async Task MappedStoreDependencyFailureEmitsOneOwnerEvent(
        bool retryable,
        int eventId,
        LogLevel level)
    {
        using var logs = new LogCollector();
        Exception exception = retryable
            ? new TimeoutException("__HPD_L23_EXCEPTION_MESSAGE__")
            : new IOException("__HPD_L23_EXCEPTION_MESSAGE__");
        using var provider = Provider(logs, new ThrowingRecordStore("primary", exception));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
            "items",
            null,
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.List) with
            {
                CorrelationId = "__HPD_L23_CORRELATION_ID__"
            });

        Assert.Equal(OperationStatus.StoreError, result.Status);
        var record = Assert.Single(logs.RecordsFor(eventId));
        Assert.Equal(level, record.Level);
        AssertContract(record);
        Assert.Single(logs.Records);
        AssertRuntimeSafety(logs.Records);
    }

    [Fact]
    public async Task EventDispatchFailureEmitsOneSafePostCommitWarning()
    {
        using var logs = new LogCollector();
        using var provider = Provider(logs, configure: services =>
            services.AddSingleton<IBaseEventPublisher, FailingEventPublisher>());

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest("__HPD_L23_PAYLOAD_VALUE__"),
            RuntimeTestData.AnonymousPrincipal with { SubjectId = "__HPD_L23_SUBJECT_ID__" },
            RuntimeTestData.Operation(BaseOperationKind.Create) with
            {
                CorrelationId = "__HPD_L23_CORRELATION_ID__"
            });

        Assert.Equal(OperationStatus.Created, result.Status);
        AssertContract(Assert.Single(logs.RecordsFor(1004)));
        Assert.Single(logs.Records);
        AssertRuntimeSafety(logs.Records);
    }

    [Theory]
    [InlineData(true, 1005)]
    [InlineData(false, 1006)]
    public async Task ContributorFailureEmitsOneSafeEventAndStillPropagates(
        bool health,
        int eventId)
    {
        using var logs = new LogCollector();
        using var provider = Provider(logs, configure: services =>
        {
            if (health)
            {
                services.AddSingleton<IBaseHealthContributor, ThrowingContributor>();
            }
            else
            {
                services.AddSingleton<IBaseDiagnosticContributor, ThrowingContributor>();
            }
        });

        if (health)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await provider.GetRequiredService<IBaseHealthProvider>().GetHealthAsync(
                    RuntimeTestData.AnonymousPrincipal,
                    RuntimeTestData.Operation(BaseOperationKind.AdminInspect),
                    VisibilityLevel.Admin));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await provider.GetRequiredService<IBaseDiagnosticProvider>().GetDiagnosticsAsync(
                    RuntimeTestData.AnonymousPrincipal,
                    RuntimeTestData.Operation(BaseOperationKind.AdminInspect),
                    VisibilityLevel.Admin));
        }

        AssertContract(Assert.Single(logs.RecordsFor(eventId)));
        Assert.Single(logs.Records);
        AssertRuntimeSafety(logs.Records);
    }

    [Fact]
    public async Task SuccessfulAndExpectedStoreOutcomesAreSilent()
    {
        using var logs = new LogCollector();
        using var provider = Provider(logs);
        var runtime = provider.GetRequiredService<IBaseRecordRuntime>();

        var list = await runtime.ListAsync(
            "items",
            null,
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.List));
        var get = await runtime.GetAsync(
            "items",
            new RecordId("__HPD_L23_RECORD_ID__"),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Get));

        Assert.Equal(OperationStatus.Ok, list.Status);
        Assert.Equal(OperationStatus.NotFound, get.Status);
        Assert.Empty(logs.Records);
    }

    private static ServiceProvider Provider(
        LogCollector logs,
        IRecordStore? store = null,
        bool registerStore = true,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(logs);
        });
        services.AddSingleton<IBaseDescriptorContributor, CollectionContributor>();
        services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        configure?.Invoke(services);
        services.AddHPDBaseRuntime();

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBaseDescriptorRegistry>()
            .RebuildAsync()
            .AsTask()
            .GetAwaiter()
            .GetResult();

        if (registerStore)
        {
            var selectedStore = store ?? new FakeRecordStore("primary");
            provider.GetRequiredService<IRecordStoreRegistry>().Add(new RecordStoreRegistration
            {
                StoreId = selectedStore.Capabilities.StoreId,
                Store = selectedStore,
                CollectionIds = ["items"]
            });
        }

        return provider;
    }

    private static void AssertContract(CapturedLogRecord record)
    {
        var contract = Assert.Single(
            HPDBaseLogEventRegistry.Active,
            item => item.Id == record.EventId.Id);
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
        Assert.StartsWith("HPD.Base.", record.Category, StringComparison.Ordinal);
    }

    private static void AssertRuntimeSafety(IEnumerable<CapturedLogRecord> records)
    {
        var captured = records.ToArray();
        LogSafetyInspector.AssertNoExceptions(captured);
        LogSafetyInspector.AssertNoScopes(captured);
        LogSafetyInspector.AssertSafe(
            captured,
            "__HPD_L23_CORRELATION_ID__",
            "__HPD_L23_RECORD_ID__",
            "__HPD_L23_PAYLOAD_VALUE__",
            "__HPD_L23_SUBJECT_ID__",
            "__HPD_L23_EXCEPTION_MESSAGE__");
    }

    private static RecordCreateRequest CreateRequest(string value)
    {
        using var document = JsonDocument.Parse($$"""{"title":"{{value}}"}""");
        return new RecordCreateRequest
        {
            Payload = new RecordPayload
            {
                Kind = RecordPayloadKind.Json,
                Json = document.RootElement.Clone()
            }
        };
    }

    private sealed class CollectionContributor : IBaseDescriptorContributor
    {
        public string Id => "runtime-logging";

        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            builder.AddCollection(new CollectionDefinition
            {
                Id = "items",
                Name = "items",
                Kind = BaseCollectionKinds.Document,
                SchemaMode = SchemaMode.Loose,
                UnknownFields = UnknownFieldPolicy.Preserve,
                Operations = new CollectionOperationMatrix
                {
                    List = true,
                    Get = true,
                    Create = true
                }
            });
        }
    }

    private sealed class ThrowingContributor : IBaseHealthContributor, IBaseDiagnosticContributor
    {
        public string Id => "__HPD_L23_CONTRIBUTOR_ID__";

        public ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("__HPD_L23_EXCEPTION_MESSAGE__");

        public ValueTask<DiagnosticDescriptor[]> GetDiagnosticsAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("__HPD_L23_EXCEPTION_MESSAGE__");
    }
}
