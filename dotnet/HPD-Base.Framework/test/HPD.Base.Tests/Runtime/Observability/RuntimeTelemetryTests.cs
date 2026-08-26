using System.Collections.Concurrent;
using System.Diagnostics;
using HPD.Base;
using System.Text.Json;
using HPD.Base.Tests.Observability;
using HPD.Base.Tests.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests.Observability;

public sealed class RuntimeTelemetryTests
{
    [Fact]
    public async Task RecordOperationsEmitParentAndBoundarySpans()
    {
        using var listener = new ActivityCollector(HPDBaseActivitySourceNames.Runtime);
        var store = new FakeRecordStore("primary");
        store.AddRecord(Record("rec_seed"));
        using var provider = OperationTestServices.Build(store);
        var runtime = provider.GetRequiredService<IBaseRecordRuntime>();

        await runtime.ListAsync("items", new RecordQuery(), RuntimeTestData.AnonymousPrincipal, RuntimeTestData.Operation(BaseOperationKind.List), CancellationToken.None);
        await runtime.GetAsync("items", RecordId.Create("rec_seed"), RuntimeTestData.AnonymousPrincipal, RuntimeTestData.Operation(BaseOperationKind.Get), CancellationToken.None);
        await runtime.CreateAsync("items", CreateRequest("rec_created"), RuntimeTestData.AnonymousPrincipal, RuntimeTestData.Operation(BaseOperationKind.Create), CancellationToken.None);
        await runtime.PatchAsync("items", RecordId.Create("rec_created"), PatchRequest(), RuntimeTestData.AnonymousPrincipal, RuntimeTestData.Operation(BaseOperationKind.Patch), CancellationToken.None);
        await runtime.ReplaceAsync("items", RecordId.Create("rec_created"), ReplaceRequest(), RuntimeTestData.AnonymousPrincipal, RuntimeTestData.Operation(BaseOperationKind.Replace), CancellationToken.None);
        await runtime.DeleteAsync("items", RecordId.Create("rec_created"), new RecordDeleteRequest(), RuntimeTestData.AnonymousPrincipal, RuntimeTestData.Operation(BaseOperationKind.Delete), CancellationToken.None);

        var names = listener.Stopped.Select(activity => activity.OperationName).ToArray();
        Assert.Contains(HPDBaseTelemetrySpans.RuntimeRecordsList, names);
        Assert.Contains(HPDBaseTelemetrySpans.RuntimeRecordsGet, names);
        Assert.Contains(HPDBaseTelemetrySpans.RuntimeRecordsCreate, names);
        Assert.Contains(HPDBaseTelemetrySpans.RuntimeRecordsPatch, names);
        Assert.Contains(HPDBaseTelemetrySpans.RuntimeRecordsReplace, names);
        Assert.Contains(HPDBaseTelemetrySpans.RuntimeRecordsDelete, names);
        Assert.Contains(HPDBaseTelemetrySpans.RuntimePolicyEvaluate, names);
        Assert.Contains(HPDBaseTelemetrySpans.RuntimeStoreInvoke, names);
        Assert.Contains(HPDBaseTelemetrySpans.RuntimeEventsDispatch, names);
    }

    [Fact]
    public async Task OperationDiagnosticsPreserveCorrelationAndAddActivityTraceId()
    {
        using var listener = new ActivityCollector(HPDBaseActivitySourceNames.Runtime);
        using var provider = OperationTestServices.Build(new FakeRecordStore("primary"));
        var context = RuntimeTestData.Operation(BaseOperationKind.Create) with { CorrelationId = "corr_present_but_not_tagged" };

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest(),
            RuntimeTestData.AnonymousPrincipal,
            context,
            CancellationToken.None);

        Assert.Equal(OperationStatus.Created, result.Status);
        Assert.NotNull(result.Diagnostics);
        var createSpan = listener.Stopped.First(activity =>
            activity.OperationName == HPDBaseTelemetrySpans.RuntimeRecordsCreate &&
            activity.TraceId.ToString() == result.Diagnostics!.TraceId);
        Assert.Equal(createSpan.TraceId.ToString(), result.Diagnostics!.TraceId);
        Assert.Equal("corr_present_but_not_tagged", result.Diagnostics.CorrelationId);
        Assert.Equal(true, Tag(createSpan, HPDBaseTelemetryTags.CorrelationIdPresent));
        Assert.DoesNotContain(listener.Stopped, activity => TagValues(activity).Contains("corr_present_but_not_tagged", StringComparer.Ordinal));
    }

    [Fact]
    public async Task StoreErrorsMarkSpanAsErrorWithoutLeakingExceptionTextOrRecordIds()
    {
        using var listener = new ActivityCollector(HPDBaseActivitySourceNames.Runtime);
        using var provider = OperationTestServices.Build(new ThrowingRecordStore("primary", new TimeoutException("payload-secret-rec_secret")));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().ListAsync(
            "items",
            new RecordQuery(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.List),
            CancellationToken.None);

        var listSpan = listener.Stopped.First(activity =>
            activity.OperationName == HPDBaseTelemetrySpans.RuntimeRecordsList &&
            activity.Status == ActivityStatusCode.Error &&
            Equals(Tag(activity, HPDBaseTelemetryTags.ErrorCode), "base.runtime.store.dependencyFailure"));
        Assert.Equal(OperationStatus.StoreError, result.Status);
        Assert.Equal(ActivityStatusCode.Error, listSpan.Status);
        Assert.Equal("base.runtime.store.dependencyFailure", Tag(listSpan, HPDBaseTelemetryTags.ErrorCode));
        Assert.DoesNotContain(listener.Stopped, activity => TagValues(activity).Any(value => value.Contains("payload-secret", StringComparison.Ordinal) || value.Contains("rec_secret", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task RuntimeMetricsUseStableInstrumentNames()
    {
        using var metrics = new MeterCollector(HPDBaseMeterNames.Runtime);
        using var provider = OperationTestServices.Build(new FakeRecordStore("primary"));

        await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);

        Assert.Contains(HPDBaseTelemetryInstruments.RuntimeOperations, metrics.InstrumentNames);
        Assert.Contains(HPDBaseTelemetryInstruments.RuntimeOperationDuration, metrics.InstrumentNames);
        Assert.Contains(HPDBaseTelemetryInstruments.RuntimePolicyEvaluations, metrics.InstrumentNames);
        Assert.Contains(HPDBaseTelemetryInstruments.RuntimeStoreInvocations, metrics.InstrumentNames);
    }

    [Fact]
    public async Task RecordOperationsWorkWithoutConfiguredTelemetryListeners()
    {
        using var provider = OperationTestServices.Build(new FakeRecordStore("primary"));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Created, result.Status);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task RuntimeAdminSurfacesEmitSpansAndReadMetricsWithoutCorrelationOrRecordMarkers()
    {
        using var listener = new ActivityCollector(HPDBaseActivitySourceNames.Runtime);
        using var metrics = new MeterCollector(HPDBaseMeterNames.Runtime);
        using var provider = OperationTestServices.Build(new FakeRecordStore("primary"));
        var context = RuntimeTestData.Operation(BaseOperationKind.AdminInspect) with
        {
            CollectionId = "items",
            RecordId = "rec-secret",
            CorrelationId = "corr-secret"
        };

        await provider.GetRequiredService<IBaseDescriptorProvider>().GetManifestAsync(
            new BaseManifestRequest
            {
                View = VisibilityLevel.Admin,
                Principal = RuntimeTestData.AnonymousPrincipal,
                Operation = context
            },
            CancellationToken.None);
        await provider.GetRequiredService<IBaseDescriptorProvider>().GetExpandedManifestAsync(
            new BaseManifestExpansionRequest
            {
                View = VisibilityLevel.Admin,
                Expand = ["schema", "health", "diagnostics"],
                Principal = RuntimeTestData.AnonymousPrincipal,
                Operation = context
            },
            CancellationToken.None);
        await provider.GetRequiredService<IBaseSchemaProvider>().GetSchemaAsync(
            RuntimeTestData.AnonymousPrincipal,
            context,
            VisibilityLevel.Admin,
            CancellationToken.None);
        await provider.GetRequiredService<IBaseSchemaProvider>().GetCollectionAsync(
            "items",
            RuntimeTestData.AnonymousPrincipal,
            context,
            VisibilityLevel.Admin,
            CancellationToken.None);
        await provider.GetRequiredService<IBaseCapabilityProvider>().GetCapabilitiesAsync(
            RuntimeTestData.AnonymousPrincipal,
            context,
            VisibilityLevel.Admin,
            CancellationToken.None);
        await provider.GetRequiredService<IBaseHealthProvider>().GetHealthAsync(
            RuntimeTestData.AnonymousPrincipal,
            context,
            VisibilityLevel.Admin,
            CancellationToken.None);
        await provider.GetRequiredService<IBaseDiagnosticProvider>().GetDiagnosticsAsync(
            RuntimeTestData.AnonymousPrincipal,
            context,
            VisibilityLevel.Admin,
            CancellationToken.None);
        await provider.GetRequiredService<IBasePolicyExplainService>().ExplainAsync(
            new BasePolicyExplainRequest
            {
                Operation = BasePolicyExplainOperation.Query,
                CollectionId = "items"
            },
            RuntimeTestData.AnonymousPrincipal,
            context,
            CancellationToken.None);
        await provider.GetRequiredService<IHPDBaseRuntime>().ValidateAsync(CancellationToken.None);

        var names = listener.Stopped.Select(activity => activity.OperationName).ToArray();
        Assert.Contains(HPDBaseTelemetrySpans.RuntimeSchemaGet, names);
        Assert.Contains(HPDBaseTelemetrySpans.RuntimeSchemaCollectionGet, names);
        Assert.Contains(HPDBaseTelemetrySpans.RuntimeCapabilitiesGet, names);
        Assert.Contains(HPDBaseTelemetrySpans.RuntimeDescriptorsManifestGet, names);
        Assert.Contains(HPDBaseTelemetrySpans.RuntimeDescriptorsManifestExpand, names);
        Assert.Contains(HPDBaseTelemetrySpans.RuntimeHealthGet, names);
        Assert.Contains(HPDBaseTelemetrySpans.RuntimeDiagnosticsGet, names);
        Assert.Contains(HPDBaseTelemetrySpans.RuntimePolicyExplain, names);
        Assert.Contains(HPDBaseTelemetrySpans.RuntimeValidate, names);
        Assert.Contains(HPDBaseTelemetryInstruments.RuntimeHealthReads, metrics.InstrumentNames);
        Assert.Contains(HPDBaseTelemetryInstruments.RuntimeDiagnosticsReads, metrics.InstrumentNames);
        Assert.DoesNotContain(listener.Stopped, activity => TagValues(activity).Any(value =>
            value.Contains("corr-secret", StringComparison.Ordinal) ||
            value.Contains("rec-secret", StringComparison.Ordinal)));
    }

    private static object? Tag(Activity activity, string key) =>
        activity.TagObjects.FirstOrDefault(tag => tag.Key == key).Value;

    private static string[] TagValues(Activity activity) =>
        activity.TagObjects.Select(tag => Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).ToArray();

    private static RecordEnvelope Record(string id) => new()
    {
        CollectionId = "items",
        Id = RecordId.Create(id),
        Payload = Payload("seed"),
        Metadata = new RecordMetadata()
    };

    private static RecordCreateRequest CreateRequest(string requestedId) => new()
    {
        RequestedId = RecordId.Create(requestedId),
        Payload = Payload("created")
    };

    private static RecordCreateRequest CreateRequest() => new()
    {
        Payload = Payload("created")
    };

    private static RecordPatchRequest PatchRequest() => new()
    {
        Patch = Payload("patched")
    };

    private static RecordReplaceRequest ReplaceRequest() => new()
    {
        Payload = Payload("replaced")
    };

    private static RecordPayload Payload(string title)
    {
        using var document = JsonDocument.Parse($$"""{"title":"{{title}}"}""");
        return new RecordPayload
        {
            Kind = RecordPayloadKind.Json,
            Json = document.RootElement.Clone()
        };
    }

}
