using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HPD.Events;
using HPD.Events.Core;
using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Core.Artifacts;
using HPDAgent.Graph.Abstractions.Storage;
using HPDAgent.Graph.Connectors.Abstractions.Assets;
using HPDAgent.Graph.Connectors.Abstractions.Connections;
using HPDAgent.Graph.Connectors.Abstractions.Events;
using HPDAgent.Graph.Connectors.Abstractions.IO;
using HPDAgent.Graph.Connectors.Abstractions.Materialization;
using HPDAgent.Graph.Connectors.Abstractions.Sources;
using HPDAgent.Graph.Connectors.Core.Catalog;
using HPDAgent.Graph.Connectors.Core.Connections;
using HPDAgent.Graph.Connectors.Core.Dedupe;
using HPDAgent.Graph.Connectors.Core.Dispatch;
using HPDAgent.Graph.Connectors.Core.IO;
using HPDAgent.Graph.Connectors.Core.Materialization;
using HPDAgent.Graph.Connectors.Core.Polling;
using HPDAgent.Graph.Connectors.Core.Sources;
using HPDAgent.Graph.Hosting.Data;
using HPDAgent.Graph.Hosting.Lifecycle;

namespace HPD.Graph.Tests.V21;

public sealed class ConnectorCoreTests
{
    [Fact]
    public async Task InMemoryWorkflowSourceStore_SavesLoadsListsAndDeletesSources()
    {
        var store = new InMemoryWorkflowSourceStore();
        var source = new WorkflowSource
        {
            SourceId = "source-1",
            GraphId = "graph-1",
            SourceType = "github.issue.opened",
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch
        };

        await store.SaveAsync(source);

        (await store.LoadAsync("source-1")).Should().Be(source);
        (await store.ListAsync()).Should().ContainSingle(s => s.SourceId == "source-1");
        (await store.ListByGraphAsync("graph-1")).Should().ContainSingle(s => s.SourceId == "source-1");

        await store.DeleteAsync("source-1");

        (await store.LoadAsync("source-1")).Should().BeNull();
    }

    [Fact]
    public async Task JsonWorkflowSourceStore_SavesLoadsListsAndDeletesSourcesAndState()
    {
        var store = new JsonWorkflowSourceStore(CreateTempDirectory());
        var source = new WorkflowSource
        {
            SourceId = "source/1",
            GraphId = "graph-1",
            SourceType = "github.issue.opened",
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch
        };
        var state = new WorkflowSourceState
        {
            SourceId = "source/1",
            Values = new Dictionary<string, string> { ["cursor"] = "abc" },
            UpdatedAt = DateTimeOffset.UnixEpoch
        };

        await store.SaveAsync(source);
        await store.SaveStateAsync(state);

        (await store.LoadAsync("source/1")).Should().BeEquivalentTo(source);
        (await store.ListAsync()).Should().ContainSingle(s => s.SourceId == "source/1");
        (await store.ListByGraphAsync("graph-1")).Should().ContainSingle(s => s.SourceId == "source/1");
        (await store.LoadStateAsync("source/1")).Should().BeEquivalentTo(state);

        await store.DeleteAsync("source/1");

        (await store.LoadAsync("source/1")).Should().BeNull();
        (await store.LoadStateAsync("source/1")).Should().BeNull();
    }

    [Fact]
    public async Task WorkflowSourceStateAccessor_SavesReadsAndRemovesTypedValues()
    {
        var store = new InMemoryWorkflowSourceStore();
        var accessor = new WorkflowSourceStateAccessor(store, "source-1");

        await accessor.SetAsync(
            "cursor",
            new ConnectorCoreCursorState("abc", 42),
            ConnectorCoreTestsJsonContext.Default.ConnectorCoreCursorState);

        var cursor = await accessor.GetAsync(
            "cursor",
            ConnectorCoreTestsJsonContext.Default.ConnectorCoreCursorState);
        cursor.Should().Be(new ConnectorCoreCursorState("abc", 42));

        await accessor.RemoveAsync("cursor");

        (await accessor.GetAsync(
            "cursor",
            ConnectorCoreTestsJsonContext.Default.ConnectorCoreCursorState)).Should().BeNull();
    }

    [Fact]
    public async Task InMemoryConnectionStore_AndConnectionProvider_SaveLoadResolveAndDelete()
    {
        var store = new InMemoryConnectionStore();
        var provider = new StoreBackedConnectionProvider(
            store,
            new StaticSecretResolver(new Dictionary<string, string>
            {
                ["token"] = "secret-token"
            }));

        var connection = new ConnectionDefinition
        {
            ConnectionId = "github-main",
            ConnectionType = "github.pat",
            AppId = "github",
            DisplayName = "GitHub",
            SecretRef = "github-secret",
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch
        };

        await store.SaveAsync(connection);

        (await store.LoadAsync("github-main")).Should().Be(connection);
        (await store.ListAsync()).Should().ContainSingle(c => c.ConnectionId == "github-main");

        var resolved = await provider.ResolveAsync("github-main");

        resolved.Should().NotBeNull();
        resolved!.ConnectionId.Should().Be("github-main");
        resolved.Secrets.Should().Contain("token", "secret-token");

        await store.DeleteAsync("github-main");

        (await provider.ResolveAsync("github-main")).Should().BeNull();
    }

    [Fact]
    public async Task JsonConnectionStore_SavesLoadsListsAndDeletesConnections()
    {
        var store = new JsonConnectionStore(CreateTempDirectory());
        var connection = new ConnectionDefinition
        {
            ConnectionId = "github/main",
            ConnectionType = "github.pat",
            AppId = "github",
            DisplayName = "GitHub",
            SecretRef = "github-secret",
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch
        };

        await store.SaveAsync(connection);

        (await store.LoadAsync("github/main")).Should().BeEquivalentTo(connection);
        (await store.ListAsync()).Should().ContainSingle(c => c.ConnectionId == "github/main");

        await store.DeleteAsync("github/main");

        (await store.LoadAsync("github/main")).Should().BeNull();
    }

    [Fact]
    public async Task WorkflowSourceDispatcher_CreatesExecutionAndEmitsDispatchEvent()
    {
        var store = new InMemoryWorkflowSourceStore();
        using var defaultInput = JsonDocument.Parse("""{"tenant":"acme"}""");
        using var payload = JsonDocument.Parse("""{"issue":{"number":123}}""");
        var runner = new CapturingWorkflowExecutionRunner();
        var coordinator = new EventCoordinator();
        var source = new WorkflowSource
        {
            SourceId = "source-1",
            GraphId = "graph-1",
            SourceType = "github.issue.opened",
            DefaultInput = defaultInput.RootElement.Clone(),
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch
        };
        await store.SaveAsync(source);

        var dispatcher = new WorkflowSourceDispatcher(
            store,
            runner,
            new WorkflowSourceDedupeService(store),
            coordinator);

        await dispatcher.DispatchAsync(new WorkflowSourceEmittedEvent
        {
            SourceId = "source-1",
            GraphId = "graph-1",
            SourceType = "github.issue.opened",
            Payload = payload.RootElement.Clone(),
            EventId = "delivery-1",
            Summary = "Issue opened",
            OccurredAt = DateTimeOffset.UnixEpoch
        });

        runner.Starts.Should().ContainSingle();
        runner.Starts[0].GraphId.Should().Be("graph-1");
        runner.Starts[0].Request.TriggeredBy.Should().Be("source:source-1");
        runner.Starts[0].Request.Input!.Value.GetProperty("tenant").GetString().Should().Be("acme");
        runner.Starts[0].Request.Input!.Value
            .GetProperty("source")
            .GetProperty("payload")
            .GetProperty("issue")
            .GetProperty("number")
            .GetInt32()
            .Should()
            .Be(123);

        var dispatchEvent = await ReadSynchronousEventAsync<WorkflowExecutionDispatchedEvent>(coordinator);

        dispatchEvent.ExecutionId.Should().Be("execution-1");
        dispatchEvent.EventId.Should().Be("delivery-1");
    }

    [Fact]
    public async Task WorkflowSourceDispatcher_DoesNotDispatchDisabledSourceOrDuplicateUniqueEvent()
    {
        var store = new InMemoryWorkflowSourceStore();
        using var payload = JsonDocument.Parse("""{"id":1}""");
        var runner = new CapturingWorkflowExecutionRunner();
        var dispatcher = new WorkflowSourceDispatcher(
            store,
            runner,
            new WorkflowSourceDedupeService(store));

        await store.SaveAsync(new WorkflowSource
        {
            SourceId = "source-1",
            GraphId = "graph-1",
            SourceType = "github.issue.opened",
            Enabled = false,
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch
        });

        await dispatcher.DispatchAsync(new WorkflowSourceEmittedEvent
        {
            SourceId = "source-1",
            GraphId = "graph-1",
            SourceType = "github.issue.opened",
            Payload = payload.RootElement.Clone(),
            EventId = "delivery-1"
        });

        runner.Starts.Should().BeEmpty();

        await store.SaveAsync((await store.LoadAsync("source-1"))! with { Enabled = true });

        var evt = new WorkflowSourceEmittedEvent
        {
            SourceId = "source-1",
            GraphId = "graph-1",
            SourceType = "github.issue.opened",
            Payload = payload.RootElement.Clone(),
            EventId = "delivery-1"
        };

        await dispatcher.DispatchAsync(evt);
        await dispatcher.DispatchAsync(evt);

        runner.Starts.Should().ContainSingle();
    }

    [Fact]
    public async Task ConnectorAssetCatalog_LoadsAssetsFromRegisteredProviders()
    {
        var catalog = new ConnectorAssetCatalog(
        [
            new StaticAssetCatalogProvider("dbt.manifest", ArtifactKey.FromPath("warehouse", "marts", "orders")),
            new StaticAssetCatalogProvider("fivetran.workspace", ArtifactKey.FromPath("warehouse", "raw", "orders"))
        ]);

        var allAssets = await catalog.LoadAssetsAsync(new ConnectorAssetCatalogRequest());
        var selectedAssets = await catalog.LoadAssetsAsync(
            "dbt.manifest",
            new ConnectorAssetCatalogRequest());

        allAssets.Should().HaveCount(2);
        selectedAssets.Should().ContainSingle(asset => asset.ArtifactKey.ToString() == "warehouse/marts/orders");
    }

    [Fact]
    public async Task WorkflowSourcePollingService_PollsEnabledSourcesWithMatchingProvider()
    {
        var store = new InMemoryWorkflowSourceStore();
        var provider = new CapturingPollingProvider("github.issue.updated");
        var service = new WorkflowSourcePollingService(store, [provider]);
        var source = new WorkflowSource
        {
            SourceId = "source-1",
            GraphId = "graph-1",
            SourceType = "github.issue.updated",
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch
        };

        await store.SaveAsync(source);
        await store.SaveStateAsync(new WorkflowSourceState
        {
            SourceId = "source-1",
            Values = new Dictionary<string, string> { ["cursor"] = "abc" },
            UpdatedAt = DateTimeOffset.UnixEpoch
        });
        await store.SaveAsync(source with
        {
            SourceId = "source-2",
            SourceType = "github.issue.opened"
        });
        await store.SaveAsync(source with
        {
            SourceId = "source-3",
            Enabled = false
        });

        var polled = await service.PollOnceAsync();

        polled.Should().Be(1);
        provider.Polls.Should().ContainSingle();
        provider.Polls[0].Source.SourceId.Should().Be("source-1");
        provider.Polls[0].State!.Values.Should().Contain("cursor", "abc");
    }

    [Fact]
    public async Task ConnectorMaterializationDispatcher_EmitsAndRecordsMaterializationEvents()
    {
        var artifacts = new InMemoryArtifactRegistry();
        var events = new EventCoordinator();
        var artifactKey = ArtifactKey.FromPath("warehouse", "marts", "orders");
        var dispatcher = new ConnectorMaterializationDispatcher(
            [new StaticMaterializationProvider("fivetran.sync", artifactKey)],
            new ConnectorArtifactEventRecorder());
        var context = new ConnectorMaterializationContext
        {
            GraphId = "graph-1",
            ArtifactKey = artifactKey,
            Connections = new NullConnectionProvider(),
            Artifacts = artifacts,
            Events = events
        };

        var emitted = new List<Event>();
        await foreach (var evt in dispatcher.MaterializeAsync("fivetran.sync", context))
        {
            emitted.Add(evt);
        }

        emitted.Should().ContainSingle(e => e is ExternalArtifactMaterializedEvent);
        (await artifacts.GetLatestVersionAsync(artifactKey)).Should().Be("v1");
        var metadata = await artifacts.GetMetadataAsync(artifactKey, "v1");
        metadata.Should().NotBeNull();
        metadata!.CustomMetadata.Should().ContainKey("connector.eventKind");

        var eventFromCoordinator = await ReadSynchronousEventAsync<ExternalArtifactMaterializedEvent>(events);
        eventFromCoordinator.Version.Should().Be("v1");
    }

    [Fact]
    public async Task ConnectorArtifactEventRecorder_RecordsObservationsAndCheckResults()
    {
        var artifacts = new InMemoryArtifactRegistry();
        var artifactKey = ArtifactKey.FromPath("warehouse", "raw", "orders");
        var recorder = new ConnectorArtifactEventRecorder();

        await recorder.RecordAsync(new ArtifactObservedEvent
        {
            ArtifactKey = artifactKey,
            ConnectionId = "warehouse-main",
            ExternalRunId = "observe-1",
            ObservedAt = DateTimeOffset.UnixEpoch
        }, artifacts);

        (await artifacts.GetLatestVersionAsync(artifactKey)).Should().Be("observation:observe-1");

        await recorder.RecordAsync(new ArtifactCheckCompletedEvent
        {
            ArtifactKey = artifactKey,
            CheckName = "row_count_positive",
            Passed = true,
            Severity = "info"
        }, artifacts);

        var metadata = await artifacts.GetMetadataAsync(artifactKey, "observation:observe-1");
        metadata.Should().NotBeNull();
        metadata!.CustomMetadata.Should().Contain("connector.checks.row_count_positive.passed", true);
        metadata.CustomMetadata.Should().Contain("connector.checks.row_count_positive.severity", "info");
    }

    [Fact]
    public async Task ArtifactIOManagerRegistry_FindsAndUsesRegisteredManagers()
    {
        var manager = new MemoryArtifactIOManager();
        var registry = new ArtifactIOManagerRegistry([manager]);
        var connection = new ResolvedConnection
        {
            ConnectionId = "duckdb-local",
            ConnectionType = "duckdb.file",
            AppId = "duckdb"
        };
        var writeContext = new ArtifactWriteContext
        {
            ArtifactKey = ArtifactKey.FromPath("warehouse", "marts", "orders"),
            Version = "v1",
            Connection = connection
        };

        await registry.GetRequired("memory").StoreAsync(writeContext, "stored-value");

        var loaded = await registry.GetRequired("memory").LoadAsync(new ArtifactReadContext
        {
            ArtifactKey = writeContext.ArtifactKey,
            Version = "v1",
            Connection = connection
        });

        loaded.Should().Be("stored-value");
    }

    private static async Task<TEvent> ReadSynchronousEventAsync<TEvent>(IEventCoordinator coordinator)
        where TEvent : Event
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var evt in coordinator.ReadSynchronousAsync(cts.Token))
        {
            if (evt is TEvent typed)
            {
                return typed;
            }
        }

        throw new InvalidOperationException($"No synchronous event of type {typeof(TEvent).Name} was emitted.");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "hpd-connectors-core-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StaticSecretResolver : IConnectorSecretResolver
    {
        private readonly IReadOnlyDictionary<string, string> _secrets;

        public StaticSecretResolver(IReadOnlyDictionary<string, string> secrets)
        {
            _secrets = secrets;
        }

        public Task<IReadOnlyDictionary<string, string>> ResolveAsync(
            string secretRef,
            CancellationToken ct = default)
        {
            return Task.FromResult(_secrets);
        }
    }

    private sealed class CapturingWorkflowExecutionRunner : IWorkflowExecutionRunner
    {
        public List<(string GraphId, ExecuteWorkflowRequest Request)> Starts { get; } = [];

        public Task<WorkflowExecutionDto> StartAsync(
            string graphId,
            ExecuteWorkflowRequest request,
            CancellationToken ct = default)
        {
            Starts.Add((graphId, request));
            return Task.FromResult(new WorkflowExecutionDto
            {
                GraphId = graphId,
                ExecutionId = $"execution-{Starts.Count}",
                Status = WorkflowExecutionStatus.Created,
                CreatedAt = DateTimeOffset.UnixEpoch
            });
        }

        public Task<WorkflowExecutionDto?> RunAsync(
            string graphId,
            string executionId,
            CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<int> RunQueuedAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> RequeueInterruptedAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class StaticAssetCatalogProvider : IConnectorAssetCatalogProvider
    {
        private readonly ArtifactKey _artifactKey;

        public StaticAssetCatalogProvider(string catalogProviderName, ArtifactKey artifactKey)
        {
            CatalogProviderName = catalogProviderName;
            _artifactKey = artifactKey;
        }

        public string CatalogProviderName { get; }

        public Task<IReadOnlyList<ConnectorAssetDescriptor>> LoadAssetsAsync(
            ConnectorAssetCatalogRequest request,
            CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<ConnectorAssetDescriptor>>(
            [
                new ConnectorAssetDescriptor
                {
                    AssetType = CatalogProviderName,
                    AppId = CatalogProviderName.Split('.')[0],
                    ArtifactKey = _artifactKey
                }
            ]);
        }
    }

    private sealed class CapturingPollingProvider : IPollingWorkflowSourceProvider
    {
        public CapturingPollingProvider(string sourceType)
        {
            SourceType = sourceType;
        }

        public string SourceType { get; }

        public List<(WorkflowSource Source, WorkflowSourceState? State)> Polls { get; } = [];

        public Task RegisterAsync(WorkflowSource source, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdateAsync(WorkflowSource source, CancellationToken ct = default) => Task.CompletedTask;

        public Task UnregisterAsync(string sourceId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<WorkflowSourceStatus>> GetStatusAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WorkflowSourceStatus>>(Array.Empty<WorkflowSourceStatus>());

        public Task PollAsync(WorkflowSource source, WorkflowSourceState? state, CancellationToken ct = default)
        {
            Polls.Add((source, state));
            return Task.CompletedTask;
        }
    }

    private sealed class StaticMaterializationProvider : IConnectorMaterializationProvider
    {
        private readonly ArtifactKey _artifactKey;

        public StaticMaterializationProvider(string materializationType, ArtifactKey artifactKey)
        {
            MaterializationType = materializationType;
            _artifactKey = artifactKey;
        }

        public string MaterializationType { get; }

        public async IAsyncEnumerable<Event> MaterializeAsync(
            ConnectorMaterializationContext context,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new ExternalArtifactMaterializedEvent
            {
                ArtifactKey = _artifactKey,
                Version = "v1",
                ConnectionId = "fivetran-main",
                ExternalRunId = "run-1",
                MaterializedAt = DateTimeOffset.UnixEpoch
            };
        }
    }

    private sealed class NullConnectionProvider : IConnectionProvider
    {
        public Task<ResolvedConnection?> ResolveAsync(string connectionId, CancellationToken ct = default) =>
            Task.FromResult<ResolvedConnection?>(null);
    }

    private sealed class MemoryArtifactIOManager : IArtifactIOManager
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

        public string Name => "memory";

        public ValueTask StoreAsync(
            ArtifactWriteContext context,
            object? value,
            CancellationToken ct = default)
        {
            _values[Key(context.ArtifactKey, context.Version)] = value;
            return ValueTask.CompletedTask;
        }

        public ValueTask<object?> LoadAsync(
            ArtifactReadContext context,
            CancellationToken ct = default)
        {
            _values.TryGetValue(Key(context.ArtifactKey, context.Version), out var value);
            return ValueTask.FromResult(value);
        }

        private static string Key(ArtifactKey key, string? version) => $"{key}@{version}";
    }
}

internal sealed record ConnectorCoreCursorState(string Cursor, int Count);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ConnectorCoreCursorState))]
internal sealed partial class ConnectorCoreTestsJsonContext : JsonSerializerContext;
