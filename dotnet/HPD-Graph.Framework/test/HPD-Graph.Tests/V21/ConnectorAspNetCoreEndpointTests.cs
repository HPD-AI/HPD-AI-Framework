using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HPD.Base;
using HPD.Events;
using HPD.Events.DependencyInjection;
using HPD.Graph.Abstractions.Config;
using HPD.Graph.Abstractions.Artifacts;
using HPD.Graph.Base;
using HPD.Graph.Connectors.Abstractions.Assets;
using HPD.Graph.Connectors.Abstractions.Connections;
using HPD.Graph.Connectors.Abstractions.Descriptors;
using HPD.Graph.Connectors.Abstractions.Events;
using HPD.Graph.Connectors.Abstractions.IO;
using HPD.Graph.Connectors.Abstractions.Materialization;
using HPD.Graph.Connectors.Abstractions.Options;
using HPD.Graph.Connectors.Abstractions.Sources;
using HPD.Graph.Connectors.AspNetCore.Data;
using HPD.Graph.Connectors.AspNetCore.DependencyInjection;
using HPD.Graph.Connectors.AspNetCore.EndpointMapping;
using HPD.Graph.Connectors.AspNetCore.Serialization;
using HPD.Graph.Connectors.Core.IO;
using HPD.Graph.Core.Artifacts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Graph.Tests.V21;

public sealed class ConnectorAspNetCoreEndpointTests
{
    [Fact]
    public void MapHPDGraphConnectors_MapsPhase4Routes()
    {
        using var app = CreateApp();

        var routes = GetEndpoints(app)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToHashSet(StringComparer.Ordinal);

        routes.Should().Contain(
        [
            "/workflows/connectors",
            "/workflows/connectors/{connectorId}",
            "/workflows/connectors/{connectorId}/options/{optionProvider}",
            "/workflows/connectors/{connectorId}/assets",
            "/workflows/connections",
            "/workflows/{graphId}/sources",
            "/workflows/sources",
            "/workflows/sources/{sourceId}",
            "/workflows/sources/{sourceId}/webhook",
            "/workflows/assets/{artifactKey}/materialize",
            "/workflows/assets/{artifactKey}/backfill",
            "/workflows/assets/{artifactKey}/observe",
            "/workflows/artifact-io-managers",
            "/workflows/events"
        ]);
    }

    [Fact]
    public async Task ConnectionEndpoints_CreateListGetUpdateAndDeleteConnection()
    {
        using var app = CreateApp();
        var connection = CreateConnection("github-main");

        var create = await InvokeJsonAsync(
            app,
            "/workflows/connections",
            "POST",
            connection,
            ConnectorAspNetCoreJsonSerializerContext.Default.ConnectionDefinition);

        create.StatusCode.Should().Be(StatusCodes.Status201Created);

        var list = await InvokeAsync(app, "/workflows/connections", "GET");
        list.StatusCode.Should().Be(StatusCodes.Status200OK);
        Deserialize<ConnectionListResponse>(
                list.Body,
                ConnectorAspNetCoreJsonSerializerContext.Default.ConnectionListResponse)
            .Connections
            .Should()
            .ContainSingle(c => c.ConnectionId == "github-main");

        var get = await InvokeAsync(app, "/workflows/connections/{connectionId}", "GET", ("connectionId", "github-main"));
        get.StatusCode.Should().Be(StatusCodes.Status200OK);

        var update = await InvokeJsonAsync(
            app,
            "/workflows/connections/{connectionId}",
            "PUT",
            connection with { DisplayName = "GitHub Updated" },
            ConnectorAspNetCoreJsonSerializerContext.Default.ConnectionDefinition,
            ("connectionId", "github-main"));
        update.StatusCode.Should().Be(StatusCodes.Status200OK);
        update.Body.Should().Contain("GitHub Updated");

        var delete = await InvokeAsync(app, "/workflows/connections/{connectionId}", "DELETE", ("connectionId", "github-main"));
        delete.StatusCode.Should().Be(StatusCodes.Status204NoContent);
    }

    [Fact]
    public async Task SourceEndpoints_CreateListGetEnableDisableStateAndDeleteSource()
    {
        using var app = CreateApp();
        var source = CreateSource("source-1");

        var create = await InvokeJsonAsync(
            app,
            "/workflows/{graphId}/sources",
            "POST",
            source,
            ConnectorAspNetCoreJsonSerializerContext.Default.WorkflowSource,
            ("graphId", "graph-1"));
        create.StatusCode.Should().Be(StatusCodes.Status201Created);

        var list = await InvokeAsync(app, "/workflows/sources", "GET");
        Deserialize<WorkflowSourceListResponse>(
                list.Body,
                ConnectorAspNetCoreJsonSerializerContext.Default.WorkflowSourceListResponse)
            .Sources
            .Should()
            .ContainSingle(s => s.SourceId == "source-1");

        var graphList = await InvokeAsync(app, "/workflows/{graphId}/sources", "GET", ("graphId", "graph-1"));
        graphList.StatusCode.Should().Be(StatusCodes.Status200OK);

        var disable = await InvokeAsync(app, "/workflows/sources/{sourceId}/disable", "POST", ("sourceId", "source-1"));
        disable.Body.Should().Contain("\"enabled\":false");

        var enable = await InvokeAsync(app, "/workflows/sources/{sourceId}/enable", "POST", ("sourceId", "source-1"));
        enable.Body.Should().Contain("\"enabled\":true");

        var state = await InvokeAsync(app, "/workflows/sources/{sourceId}/state", "GET", ("sourceId", "source-1"));
        state.StatusCode.Should().Be(StatusCodes.Status404NotFound);

        var delete = await InvokeAsync(app, "/workflows/sources/{sourceId}", "DELETE", ("sourceId", "source-1"));
        delete.StatusCode.Should().Be(StatusCodes.Status204NoContent);
    }

    [Fact]
    public async Task ConnectorCatalogOptionAssetAndIoEndpoints_ReturnRegisteredProviders()
    {
        using var app = CreateApp();

        var connectors = await InvokeAsync(app, "/workflows/connectors", "GET");
        Deserialize<ConnectorListResponse>(
                connectors.Body,
                ConnectorAspNetCoreJsonSerializerContext.Default.ConnectorListResponse)
            .Connectors
            .Should()
            .ContainSingle(c => c.ConnectorId == "github");

        var options = await InvokeJsonAsync(
            app,
            "/workflows/connectors/{connectorId}/options/{optionProvider}",
            "POST",
            new ConnectorOptionRequest(),
            ConnectorAspNetCoreJsonSerializerContext.Default.ConnectorOptionRequest,
            ("connectorId", "github"),
            ("optionProvider", "github.repositories"));
        options.StatusCode.Should().Be(StatusCodes.Status200OK);
        options.Body.Should().Contain("HPD/HPD-AI-Framework");

        var assets = await InvokeAsync(app, "/workflows/connectors/{connectorId}/assets", "GET", ("connectorId", "dbt"));
        assets.StatusCode.Should().Be(StatusCodes.Status200OK);
        assets.Body.Should().Contain("orders");

        var io = await InvokeAsync(app, "/workflows/artifact-io-managers", "GET");
        io.Body.Should().Contain("memory");
    }

    [Fact]
    public async Task GenericWebhookEndpoint_DispatchesToRegisteredWebhookProvider()
    {
        using var app = CreateApp();
        await InvokeJsonAsync(
            app,
            "/workflows/{graphId}/sources",
            "POST",
            CreateSource("source-1"),
            ConnectorAspNetCoreJsonSerializerContext.Default.WorkflowSource,
            ("graphId", "graph-1"));

        var response = await InvokeRawJsonAsync(
            app,
            "/workflows/sources/{sourceId}/webhook",
            "POST",
            """{"id":123}""",
            ("sourceId", "source-1"));

        response.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        var provider = app.Services.GetRequiredService<CapturingWebhookProvider>();
        provider.Received.Should().ContainSingle();
        provider.Received[0].Envelope.Body!.Value.GetProperty("id").GetInt32().Should().Be(123);
    }

    [Fact]
    public async Task ArtifactEndpoints_RecordObservationCheckAndMaterialization()
    {
        using var app = CreateApp();
        var encodedKey = Uri.EscapeDataString("warehouse/marts/orders");

        var observe = await InvokeJsonAsync(
            app,
            "/workflows/assets/{artifactKey}/observe",
            "POST",
            new ConnectorObserveRequest
            {
                ExternalRunId = "observe-1",
                ObservedAt = DateTimeOffset.UnixEpoch
            },
            ConnectorAspNetCoreJsonSerializerContext.Default.ConnectorObserveRequest,
            ("artifactKey", encodedKey));
        observe.StatusCode.Should().Be(StatusCodes.Status202Accepted);

        var check = await InvokeJsonAsync(
            app,
            "/workflows/assets/{artifactKey}/checks",
            "POST",
            new ConnectorCheckRequest
            {
                CheckName = "row_count_positive",
                Passed = true
            },
            ConnectorAspNetCoreJsonSerializerContext.Default.ConnectorCheckRequest,
            ("artifactKey", encodedKey));
        check.StatusCode.Should().Be(StatusCodes.Status202Accepted);

        var materialize = await InvokeJsonAsync(
            app,
            "/workflows/assets/{artifactKey}/materialize",
            "POST",
            new ConnectorMaterializeRequest { MaterializationType = "dbt.run" },
            ConnectorAspNetCoreJsonSerializerContext.Default.ConnectorMaterializeRequest,
            ("artifactKey", encodedKey));
        materialize.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        materialize.Body.Should().Contain("ExternalArtifactMaterializedEvent");

        var backfill = await InvokeJsonAsync(
            app,
            "/workflows/assets/{artifactKey}/backfill",
            "POST",
            new ConnectorBackfillRequest
            {
                MaterializationType = "dbt.run",
                Partitions = ["2026-05-06"]
            },
            ConnectorAspNetCoreJsonSerializerContext.Default.ConnectorBackfillRequest,
            ("artifactKey", encodedKey));
        backfill.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        backfill.Body.Should().Contain("ExternalArtifactMaterializedEvent");
    }

    private static WebApplication CreateApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddHPDGraphConnectors(ConnectorGraph());
        builder.Services.AddHPDEvents();
        builder.Services.AddSingleton<IArtifactRegistry, InMemoryArtifactRegistry>();
        builder.Services.AddSingleton(new ConnectorPackageDescriptor
        {
            ConnectorId = "github",
            DisplayName = "GitHub"
        });
        builder.Services.AddSingleton<IConnectorOptionProvider, StaticOptionProvider>();
        builder.Services.AddSingleton<IConnectorAssetCatalogProvider>(
            new StaticAssetCatalogProvider(ArtifactKey.FromPath("warehouse", "marts", "orders")));
        builder.Services.AddSingleton<CapturingWebhookProvider>();
        builder.Services.AddSingleton<IWebhookWorkflowSourceProvider>(sp => sp.GetRequiredService<CapturingWebhookProvider>());
        builder.Services.AddSingleton<IWorkflowSourceProvider>(sp => sp.GetRequiredService<CapturingWebhookProvider>());
        builder.Services.AddSingleton<IArtifactIOManager, TestArtifactIOManager>();
        builder.Services.AddSingleton<IConnectorMaterializationProvider, StaticMaterializationProvider>();
        var app = builder.Build();
        app.MapHPDGraphConnectors();
        return app;
    }

    internal static BaseGraphActivationDefinition ConnectorGraph() => BaseGraphActivationRegistration.Create(
        new GraphConfig
        {
            GraphId = "connector-test",
            GraphVersion = "1.0.0",
            Name = "connector-test",
            Nodes = new Dictionary<string, NodeConfig>(StringComparer.Ordinal),
            Edges = [],
        },
        1,
        new BaseActivationGrantSet
        {
            Enqueue = "graph.enqueue", Observe = "graph.observe", Claim = "graph.claim",
            Execute = "graph.execute", Renew = "graph.renew", Complete = "graph.complete",
            Fail = "graph.fail", Yield = "graph.yield", Cancel = "graph.cancel", Inspect = "graph.inspect",
            Replay = "graph.replay", Migrate = "graph.migrate", Reconcile = "graph.reconcile",
            Retry = "graph.retry", Dispose = "graph.dispose", Remove = "graph.remove", Repair = "graph.repair",
        },
        new BaseActivationLimits
        {
            MaximumInputBytes = 1_048_576,
            MaximumResultBytes = 65_536,
            MaximumAttempts = 3, MaximumYields = 0,
            MaximumRenewalsPerSlice = 128,
            MaximumChildrenPerSlice = 128,
            MaximumLineageDepth = 32,
            LeaseDuration = TimeSpan.FromMinutes(1),
            HandlerTimeout = TimeSpan.FromMinutes(30),
            Provider = new BaseActivationExecutionLimits
            {
                MaximumCandidates = 64, MaximumInputBytes = 1_048_576, MaximumResultBytes = 65_536,
                MaximumEvidenceBytes = 1_048_576, MaximumTransientBytes = 4_194_304,
                MaximumReadIntervals = 64, MaximumIndexOperations = 512,
                AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(30),
                CommitObservationTimeout = TimeSpan.FromSeconds(30), ReceiptResolutionTimeout = TimeSpan.FromSeconds(30),
            },
            AtomicCreation = new BaseAtomicMutationExecutionLimits
            {
                MaximumItems = 256, MaximumQueryNodes = 2_048, MaximumQueryDepth = 64,
                MaximumLiteralValues = 2_048, MaximumSelectedRecords = 256, MaximumProducedMutations = 256,
                MaximumQueryExecutions = 1, MaximumPreviousStateRequirements = 256,
                MaximumSelectedBytes = 1_048_576, MaximumEvidenceBytes = 1_048_576,
                MaximumTransientBytes = 4_194_304, MaximumReadIntervals = 256, MaximumSubjectValidations = 256,
                MaximumAuthorityReads = 512, MaximumRequestBytes = 1_048_576, MaximumResultBytes = 1_048_576,
                MaximumReceiptBytes = 1_048_576, MaximumWrittenBytes = 4_194_304, MaximumFactBytes = 4_194_304,
                MaximumJournalBytes = 4_194_304, MaximumGenerationBytes = 1_048_576,
                MaximumRelationChecks = 256, MaximumUniqueConstraintChecks = 256,
                MaximumGenerationReads = 256, MaximumGenerationComparisons = 256, MaximumGenerationIncrements = 256,
                MaximumGuardNodes = 2_048, MaximumGuardDepth = 64, MaximumStatements = 512,
                MaximumBranches = 64, MaximumExpressionNodes = 2_048,
                MaximumRecordCaptures = 256, MaximumRelationTargetCaptures = 256,
                MaximumRetirementProjections = 256, MaximumRetirementBarrierReads = 256,
                MaximumRetirementAcknowledgementReads = 256, MaximumRetirementPublications = 256,
                MaximumRetirementEvidenceBytes = 1_048_576, MaximumRetirementPublicationBytes = 1_048_576,
                Deadlines = new BaseAtomicMutationDeadlines
                {
                    AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(30),
                    CommitObservationTimeout = TimeSpan.FromSeconds(30), ReceiptResolutionTimeout = TimeSpan.FromSeconds(30),
                },
            },
        },
        ImmutableArray<string>.Empty);

    private static async Task<CapturedResponse> InvokeJsonAsync<T>(
        WebApplication app,
        string routePattern,
        string method,
        T body,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo,
        params (string Key, string Value)[] routeValues)
    {
        await using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, body, jsonTypeInfo);
        stream.Position = 0;

        return await InvokeAsync(app, routePattern, method, routeValues, stream, "application/json");
    }

    private static async Task<CapturedResponse> InvokeAsync(
        WebApplication app,
        string routePattern,
        string method,
        params (string Key, string Value)[] routeValues)
    {
        return await InvokeAsync(app, routePattern, method, routeValues, requestBody: null, contentType: null);
    }

    private static async Task<CapturedResponse> InvokeRawJsonAsync(
        WebApplication app,
        string routePattern,
        string method,
        string json,
        params (string Key, string Value)[] routeValues)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return await InvokeAsync(app, routePattern, method, routeValues, new MemoryStream(bytes), "application/json");
    }

    private static async Task<CapturedResponse> InvokeAsync(
        WebApplication app,
        string routePattern,
        string method,
        (string Key, string Value)[] routeValues,
        Stream? requestBody,
        string? contentType)
    {
        var endpoint = GetEndpoints(app)
            .OfType<RouteEndpoint>()
            .Single(endpoint =>
                string.Equals(endpoint.RoutePattern.RawText, routePattern, StringComparison.Ordinal) &&
                endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) == true);

        var responseBody = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = app.Services
        };
        context.Request.Method = method;
        context.Request.Body = requestBody ?? Stream.Null;
        if (contentType is not null)
        {
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new TestRequestBodyDetectionFeature(canHaveBody: true));
            context.Request.ContentType = contentType;
            context.Request.ContentLength = requestBody?.Length;
        }

        foreach (var (key, value) in routeValues)
        {
            context.Request.RouteValues[key] = value;
        }

        context.Response.Body = responseBody;

        await endpoint.RequestDelegate!(context);

        responseBody.Position = 0;
        using var reader = new StreamReader(responseBody);
        return new CapturedResponse(context.Response.StatusCode, context.Response.ContentType, await reader.ReadToEndAsync());
    }

    private static IEnumerable<Endpoint> GetEndpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints);

    private static T Deserialize<T>(
        string json,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo)
    {
        return JsonSerializer.Deserialize(json, jsonTypeInfo)
            ?? throw new InvalidOperationException("Response JSON could not be deserialized.");
    }

    private static WorkflowSource CreateSource(string sourceId) => new()
    {
        SourceId = sourceId,
        GraphId = "graph-1",
        SourceType = "github.issue.opened",
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch
    };

    private static ConnectionDefinition CreateConnection(string connectionId) => new()
    {
        ConnectionId = connectionId,
        ConnectionType = "github.pat",
        AppId = "github",
        DisplayName = "GitHub",
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch
    };

    private sealed record CapturedResponse(int StatusCode, string? ContentType, string Body);

    private sealed class TestRequestBodyDetectionFeature(bool canHaveBody) : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody { get; } = canHaveBody;
    }

    private sealed class StaticOptionProvider : IConnectorOptionProvider
    {
        public string OptionProviderName => "github.repositories";

        public Task<IReadOnlyList<ConnectorOption>> GetOptionsAsync(
            ConnectorOptionRequest request,
            CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<ConnectorOption>>(
            [
                new ConnectorOption
                {
                    Value = "HPD/HPD-AI-Framework",
                    Label = "HPD/HPD-AI-Framework"
                }
            ]);
        }
    }

    private sealed class StaticAssetCatalogProvider(ArtifactKey artifactKey) : IConnectorAssetCatalogProvider
    {
        public string CatalogProviderName => "dbt.manifest";

        public Task<IReadOnlyList<ConnectorAssetDescriptor>> LoadAssetsAsync(
            ConnectorAssetCatalogRequest request,
            CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<ConnectorAssetDescriptor>>(
            [
                new ConnectorAssetDescriptor
                {
                    AssetType = "dbt.model",
                    AppId = "dbt",
                    ArtifactKey = artifactKey
                }
            ]);
        }
    }

    private sealed class CapturingWebhookProvider : IWebhookWorkflowSourceProvider
    {
        public string SourceType => "github.issue.opened";

        public List<(WorkflowSource Source, WebhookEnvelope Envelope)> Received { get; } = [];

        public Task RegisterAsync(WorkflowSource source, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdateAsync(WorkflowSource source, CancellationToken ct = default) => Task.CompletedTask;

        public Task UnregisterAsync(string sourceId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<WorkflowSourceStatus>> GetStatusAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WorkflowSourceStatus>>(Array.Empty<WorkflowSourceStatus>());

        public Task ReceiveAsync(WorkflowSource source, WebhookEnvelope envelope, CancellationToken ct = default)
        {
            Received.Add((source, envelope));
            return Task.CompletedTask;
        }
    }

    private sealed class TestArtifactIOManager : IArtifactIOManager
    {
        public string Name => "memory";

        public ValueTask StoreAsync(ArtifactWriteContext context, object? value, CancellationToken ct = default) =>
            ValueTask.CompletedTask;

        public ValueTask<object?> LoadAsync(ArtifactReadContext context, CancellationToken ct = default) =>
            ValueTask.FromResult<object?>(null);
    }

    private sealed class StaticMaterializationProvider : IConnectorMaterializationProvider
    {
        public string MaterializationType => "dbt.run";

        public async IAsyncEnumerable<Event> MaterializeAsync(
            ConnectorMaterializationContext context,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new ExternalArtifactMaterializedEvent
            {
                ArtifactKey = context.ArtifactKey,
                Version = "v1",
                ExternalRunId = "run-1",
                MaterializedAt = DateTimeOffset.UnixEpoch
            };
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(object))]
internal sealed partial class ConnectorAspNetCoreEndpointTestsJsonContext : JsonSerializerContext;
