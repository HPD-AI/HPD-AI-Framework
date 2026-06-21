using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HPD.Events;
using HPD.Events.Core;
using HPD.Graph.Abstractions.Artifacts;
using HPD.Graph.Abstractions.Execution;
using HPD.Graph.Abstractions.Handlers;
using HPD.Graph.Connectors.Abstractions.Assets;
using HPD.Graph.Connectors.Abstractions.Attributes;
using HPD.Graph.Connectors.Abstractions.Configuration;
using HPD.Graph.Connectors.Abstractions.Connections;
using HPD.Graph.Connectors.Abstractions.Events;
using HPD.Graph.Connectors.Abstractions.IO;
using HPD.Graph.Connectors.Abstractions.Materialization;
using HPD.Graph.Connectors.Abstractions.Options;
using HPD.Graph.Connectors.Abstractions.Sources;
using HPD.Graph.Connectors.Core.IO;
using HPD.Graph.Core.Artifacts;
using HPD.Graph.Core.Builders;
using HPD.Graph.Core.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Graph.Tests.V21.GeneratedConnectors;

public sealed class GeneratedConnectorBehaviorTests
{
    [Fact]
    public async Task GeneratedActionWrapper_ExecutesThroughGraphNodeHandlerWithClientFactory()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<IConnectionProvider, TestConnectionProvider>()
            .AddSingleton<IConnectorClientFactory<TestConnectorClient>, TestConnectorClientFactory>()
            .AddTestConnector()
            .BuildServiceProvider();

        var handler = provider
            .GetServices<IGraphNodeHandler<GraphContext>>()
            .Single(h => h.HandlerName == "test.create_issue");

        var graph = new GraphBuilder()
            .WithName("generated-connector-action")
            .AddTestCreateIssueNode("create", "Create", new TestCreateIssueConfig
            {
                ConnectionId = "test-main",
                Title = "hello"
            })
            .Build();

        var context = new GraphContext("exec-generated-connector-action", graph, provider);
        context.SetCurrentNode("create");

        var result = await handler.ExecuteAsync(context, new HandlerInputs());

        var success = result.Should().BeOfType<NodeExecutionResult.Success>().Subject;
        success.PortOutputs[0]["result"]
            .Should()
            .Be(new TestActionResult("test-main:hello"));
    }

    [Fact]
    public async Task GeneratedWebhookSourceProvider_DispatchesWorkflowSourceEvent()
    {
        TestIssueOpenedSource.ResetLifecycle();
        var dispatcher = new CapturingWorkflowSourceDispatcher();

        using var provider = new ServiceCollection()
            .AddSingleton<IWorkflowSourceDispatcher>(dispatcher)
            .AddSingleton<IConnectionProvider, TestConnectionProvider>()
            .AddTestConnector()
            .BuildServiceProvider();

        var sourceProvider = provider
            .GetServices<IWebhookWorkflowSourceProvider>()
            .Single(p => p.SourceType == "test.issue.opened");

        var source = new WorkflowSource
        {
            SourceId = "src",
            GraphId = "graph",
            SourceType = "test.issue.opened",
            Config = JsonSerializer.SerializeToElement(new TestIssueOpenedSource.Config
            {
                Repository = "HPD/repo"
            })
        };

        await sourceProvider.ReceiveAsync(
            source,
            new WebhookEnvelope
            {
                Method = "POST",
                Path = "/workflows/sources/test/webhook",
                EventType = "issues",
                Body = JsonSerializer.SerializeToElement(new { title = "opened" }),
                BodyBytes = JsonSerializer.SerializeToUtf8Bytes(new { title = "opened" })
            });

        dispatcher.Events.Should().ContainSingle();
        dispatcher.Events[0].SourceId.Should().Be("src");
        dispatcher.Events[0].GraphId.Should().Be("graph");
        dispatcher.Events[0].EventId.Should().Be("evt-opened");
        dispatcher.Events[0].Payload.GetProperty("Title").GetString().Should().Be("opened");
    }

    [Fact]
    public async Task GeneratedSourceProvider_InvokesAuthoredLifecycleMethods()
    {
        TestIssueOpenedSource.ResetLifecycle();

        using var provider = new ServiceCollection()
            .AddSingleton<IWorkflowSourceDispatcher>(new CapturingWorkflowSourceDispatcher())
            .AddSingleton<IConnectionProvider, TestConnectionProvider>()
            .AddTestConnector()
            .BuildServiceProvider();

        var sourceProvider = provider
            .GetServices<IWorkflowSourceProvider>()
            .Single(p => p.SourceType == "test.issue.opened");

        var source = new WorkflowSource
        {
            SourceId = "src-lifecycle",
            GraphId = "graph",
            SourceType = "test.issue.opened",
            Config = JsonSerializer.SerializeToElement(new TestIssueOpenedSource.Config
            {
                Repository = "HPD/lifecycle"
            })
        };

        await sourceProvider.RegisterAsync(source);
        await sourceProvider.UpdateAsync(source);
        await sourceProvider.UnregisterAsync(source.SourceId);

        TestIssueOpenedSource.RegisteredRepository.Should().Be("HPD/lifecycle");
        TestIssueOpenedSource.UpdatedRepository.Should().Be("HPD/lifecycle");
        TestIssueOpenedSource.UnregisteredSourceId.Should().Be("src-lifecycle");
    }

    [Fact]
    public async Task GeneratedOptionAndDataAdapters_RegisterAndExecute()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<IConnectionProvider, TestConnectionProvider>()
            .AddSingleton<IConnectorClientFactory<TestConnectorClient>, TestConnectorClientFactory>()
            .AddTestConnector()
            .BuildServiceProvider();

        var optionProvider = provider
            .GetServices<IConnectorOptionProvider>()
            .Single(p => p.OptionProviderName == "test.repositories");
        var options = await optionProvider.GetOptionsAsync(new ConnectorOptionRequest
        {
            ConnectionId = "test-main",
            Search = "repo",
            CurrentConfig = JsonSerializer.SerializeToElement(new
            {
                owner = "HPD"
            })
        });
        options.Should().ContainSingle(o => o.Value == "test-main:HPD/repo");

        var assetProvider = provider
            .GetServices<IConnectorAssetCatalogProvider>()
            .Single(p => p.CatalogProviderName == "test.catalog");
        var assets = await assetProvider.LoadAssetsAsync(new ConnectorAssetCatalogRequest
        {
            Config = JsonSerializer.SerializeToElement(new TestCatalog.Config { ConnectionId = "test-main" })
        });
        assets.Should().ContainSingle(a => a.ArtifactKey == ArtifactKey.FromPath("warehouse", "orders"));

        var materialization = provider
            .GetServices<IConnectorMaterializationProvider>()
            .Single(p => p.MaterializationType == "test.materialize");
        var materializationEvents = new List<Event>();
        await foreach (var evt in materialization.MaterializeAsync(new ConnectorMaterializationContext
        {
            GraphId = "graph",
            ArtifactKey = ArtifactKey.FromPath("warehouse", "orders"),
            Connections = provider.GetRequiredService<IConnectionProvider>(),
            Artifacts = new InMemoryArtifactRegistry(),
            Events = new EventCoordinator(),
            Config = JsonSerializer.SerializeToElement(new TestMaterializationConfig { ConnectionId = "test-main" })
        }))
        {
            materializationEvents.Add(evt);
        }
        materializationEvents.Should().ContainSingle(e => e is ExternalArtifactMaterializedEvent);

        var check = provider
            .GetServices<IConnectorAssetCheckProvider>()
            .Single(p => p.CheckName == "test.check");
        var checkEvents = new List<Event>();
        await foreach (var evt in check.CheckAsync(new ConnectorMaterializationContext
        {
            GraphId = "graph",
            ArtifactKey = ArtifactKey.FromPath("warehouse", "orders"),
            Connections = provider.GetRequiredService<IConnectionProvider>(),
            Artifacts = new InMemoryArtifactRegistry(),
            Events = new EventCoordinator()
        }))
        {
            checkEvents.Add(evt);
        }
        checkEvents.Should().ContainSingle(e => e is ArtifactCheckCompletedEvent);

        var ioRegistry = ActivatorUtilities.CreateInstance<ArtifactIOManagerRegistry>(provider);
        ioRegistry.GetRequired("test.io").Should().BeOfType<TestIOManager>();
    }

    [Fact]
    public void GeneratedDescriptorCatalog_IncludesConfigsActionsAndFields()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<IConnectionProvider, TestConnectionProvider>()
            .AddTestConnector()
            .BuildServiceProvider();

        var descriptor = provider.GetRequiredService<HPD.Graph.Connectors.Abstractions.Descriptors.ConnectorPackageDescriptor>();

        descriptor.Configs.Should().ContainSingle(c => c.ConfigType.EndsWith(nameof(TestCreateIssueConfig), StringComparison.Ordinal));
        descriptor.ConnectorActions.Should().ContainSingle(a =>
            a.ActionType == "test.create_issue" &&
            a.Fields.Any(f => f.Name == nameof(TestCreateIssueConfig.ConnectionId) && f.ConnectionType == "test.connection"));
        descriptor.Actions.Should().ContainSingle(a => a.HandlerName == "test.create_issue");
        descriptor.OptionProviders.Should().Contain("test.repositories");
        descriptor.ArtifactIOManagers.Should().Contain("test.io");
    }

    [Fact]
    public async Task GeneratedConnectorPartial_HandlesWebhookEnvelopeWithoutAspNetCoreContext()
    {
        TestConnector.ResetPortableHooks();
        var dispatcher = new CapturingWorkflowSourceDispatcher();
        var source = new WorkflowSource
        {
            SourceId = "src-portable",
            GraphId = "graph",
            SourceType = "test.issue.opened",
            Enabled = true,
            Config = JsonSerializer.SerializeToElement(new TestIssueOpenedSource.Config
            {
                Repository = "HPD/portable"
            })
        };

        using var provider = new ServiceCollection()
            .AddSingleton<IWorkflowSourceDispatcher>(dispatcher)
            .AddSingleton<IConnectionProvider, TestConnectionProvider>()
            .AddTestConnector()
            .BuildServiceProvider();

        await provider.GetRequiredService<TestConnector>().HandleWebhookAsync(
            new WebhookEnvelope
            {
                Method = "POST",
                Path = "/portable",
                Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["x-hpd-event-type"] = "ignored"
                },
                BodyBytes = JsonSerializer.SerializeToUtf8Bytes(new { title = "opened" })
            },
            provider);

        TestConnector.PortablePreDispatchSawEnvelope.Should().BeTrue();
        TestConnector.PortableBodyExtractorSawEnvelope.Should().BeTrue();
    }
}

[HpdConnector("test", DisplayName = "Test", JsonContextType = typeof(TestConnectorJsonContext))]
public sealed partial class TestConnector
{
    public static bool PortablePreDispatchSawEnvelope { get; private set; }
    public static bool PortableBodyExtractorSawEnvelope { get; private set; }

    public static void ResetPortableHooks()
    {
        PortablePreDispatchSawEnvelope = false;
        PortableBodyExtractorSawEnvelope = false;
    }

    [HpdConnectorPreDispatch]
    private static IResult? VerifyWebhook(WebhookEnvelope envelope, IServiceProvider services, byte[] bodyBytes)
    {
        PortablePreDispatchSawEnvelope = envelope.Path == "/portable" && bodyBytes.Length > 0;
        return null;
    }

    [HpdConnectorBodyExtractor]
    private static (string? EventType, byte[] DispatchBytes) ExtractEventType(
        WebhookEnvelope envelope,
        byte[] bodyBytes)
    {
        PortableBodyExtractorSawEnvelope = envelope.Path == "/portable";
        return ("issues", bodyBytes);
    }
}

[HpdConnection("test.connection", AppId = "test", AuthKind = ConnectionAuthKind.BearerToken)]
public sealed partial record TestConnection;

[HpdActionConfig("test.create_issue", DisplayName = "Create Issue")]
public sealed partial record TestCreateIssueConfig : IConnectorConfig
{
    [ConnectorConnection("test.connection")]
    public string ConnectionId { get; init; } = "";

    public string Title { get; init; } = "";
}

[HpdConnectorAction("test.create_issue", ConfigType = typeof(TestCreateIssueConfig))]
public static partial class TestIssueActions
{
    public static Task<TestActionResult> RunAsync(
        TestConnectorClient client,
        TestCreateIssueConfig config,
        CancellationToken ct)
        => Task.FromResult(new TestActionResult($"{client.ConnectionId}:{config.Title}"));
}

public sealed record TestActionResult(string Value);

[HpdWebhookSource("test.issue.opened", DisplayName = "Issue Opened")]
public sealed partial class TestIssueOpenedSource
{
    public static string? RegisteredRepository { get; private set; }
    public static string? UpdatedRepository { get; private set; }
    public static string? UnregisteredSourceId { get; private set; }

    public sealed record Config : IConnectorConfig
    {
        public string Repository { get; init; } = "";
    }

    public static void ResetLifecycle()
    {
        RegisteredRepository = null;
        UpdatedRepository = null;
        UnregisteredSourceId = null;
    }

    public static Task RegisterAsync(
        WorkflowSource source,
        Config config,
        CancellationToken ct)
    {
        RegisteredRepository = config.Repository;
        return Task.CompletedTask;
    }

    public static Task UpdateAsync(
        WorkflowSource source,
        Config config,
        CancellationToken ct)
    {
        UpdatedRepository = config.Repository;
        return Task.CompletedTask;
    }

    public static Task UnregisterAsync(
        string sourceId,
        CancellationToken ct)
    {
        UnregisteredSourceId = sourceId;
        return Task.CompletedTask;
    }

    public static WorkflowSourceEvent? FromWebhook(
        WebhookEnvelope envelope,
        Config config)
        => new(
            Payload: new TestIssuePayload("opened", config.Repository),
            EventId: "evt-opened",
            Summary: config.Repository);
}

public sealed record TestIssuePayload(string Title, string Repository);

public static partial class TestOptions
{
    [HpdConnectorOption("test.repositories")]
    public static ValueTask<ConnectorOptionPage> GetRepositoriesAsync(
        TestConnectorClient client,
        TestRepositoryOptionRequest request,
        CancellationToken ct)
        => ValueTask.FromResult(new ConnectorOptionPage
        {
            Options =
            [
                new ConnectorOption { Value = $"{client.ConnectionId}:{request.Owner}/{request.Search}", Label = $"{request.Owner}/{request.Search}" }
            ]
        });
}

public sealed record TestRepositoryOptionRequest
{
    public string ConnectionId { get; init; } = "";
    public string Owner { get; init; } = "";
    public string? Search { get; init; }
    public string? Cursor { get; init; }
    public int? Limit { get; init; }
}

[HpdConnectorAssetCatalog("test.catalog")]
public sealed partial class TestCatalog
{
    public sealed record Config : IConnectorConfig
    {
        [ConnectorConnection("test.connection")]
        public string ConnectionId { get; init; } = "";
    }

    public static Task<IReadOnlyList<ConnectorAssetDescriptor>> LoadAssetsAsync(
        TestConnectorClient client,
        Config config,
        CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ConnectorAssetDescriptor>>(
        [
            new ConnectorAssetDescriptor
            {
                AssetType = "test.table",
                AppId = "test",
                ArtifactKey = ArtifactKey.FromPath("warehouse", "orders"),
                Metadata = new Dictionary<string, string>
                {
                    ["connectionId"] = client.ConnectionId
                }
            }
        ]);
}

public sealed partial record TestMaterializationConfig : IConnectorConfig
{
    [ConnectorConnection("test.connection")]
    public string ConnectionId { get; init; } = "";
}

[HpdConnectorMaterialization("test.materialize", ConfigType = typeof(TestMaterializationConfig))]
public static partial class TestMaterialization
{
    public static async IAsyncEnumerable<Event> RunAsync(
        TestConnectorClient client,
        TestMaterializationConfig config,
        ConnectorMaterializationContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        yield return new ExternalArtifactMaterializedEvent
        {
            ArtifactKey = context.ArtifactKey,
            Version = client.ConnectionId,
            MaterializedAt = DateTimeOffset.UnixEpoch
        };
        await Task.CompletedTask;
    }
}

[HpdConnectorAssetCheck("test.check")]
public static partial class TestChecks
{
    public static ValueTask<ArtifactCheckCompletedEvent> RunAsync(
        ArtifactKey artifactKey,
        CancellationToken ct)
        => ValueTask.FromResult(new ArtifactCheckCompletedEvent
        {
            ArtifactKey = artifactKey,
            CheckName = "test.check",
            Passed = true
        });
}

[HpdArtifactIOManager("test.io")]
public sealed partial class TestIOManager : IArtifactIOManager
{
    public string Name => "test.io";

    public ValueTask StoreAsync(
        ArtifactWriteContext context,
        object? value,
        CancellationToken ct = default)
        => ValueTask.CompletedTask;

    public ValueTask<object?> LoadAsync(
        ArtifactReadContext context,
        CancellationToken ct = default)
        => ValueTask.FromResult<object?>("loaded");
}

public sealed class TestConnectorClient
{
    public required string ConnectionId { get; init; }
}

public sealed class TestConnectorClientFactory : IConnectorClientFactory<TestConnectorClient>
{
    public ValueTask<TestConnectorClient> CreateAsync(
        ResolvedConnection connection,
        CancellationToken ct = default)
        => ValueTask.FromResult(new TestConnectorClient { ConnectionId = connection.ConnectionId });
}

public sealed class TestConnectionProvider : IConnectionProvider
{
    public Task<ResolvedConnection?> ResolveAsync(
        string connectionId,
        CancellationToken ct = default)
        => Task.FromResult<ResolvedConnection?>(new ResolvedConnection
        {
            ConnectionId = connectionId,
            ConnectionType = "test.connection",
            AppId = "test"
        });
}

public sealed class CapturingWorkflowSourceDispatcher : IWorkflowSourceDispatcher
{
    public List<WorkflowSourceEmittedEvent> Events { get; } = [];

    public Task DispatchAsync(
        WorkflowSourceEmittedEvent evt,
        CancellationToken ct = default)
    {
        Events.Add(evt);
        return Task.CompletedTask;
    }
}

[JsonSerializable(typeof(TestCreateIssueConfig))]
[JsonSerializable(typeof(TestIssueOpenedSource.Config))]
[JsonSerializable(typeof(TestIssuePayload))]
[JsonSerializable(typeof(TestRepositoryOptionRequest))]
[JsonSerializable(typeof(TestCatalog.Config))]
[JsonSerializable(typeof(TestMaterializationConfig))]
internal sealed partial class TestConnectorJsonContext : JsonSerializerContext;
