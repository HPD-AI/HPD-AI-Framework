using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Artifacts;
using HPDAgent.Graph.Core.Caching;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Xunit;

namespace HPD.Graph.Tests.Advanced;

/// <summary>
/// Helper extension methods for IAsyncEnumerable.
/// </summary>
internal static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source, CancellationToken ct = default)
    {
        var list = new List<T>();
        await foreach (var item in source.WithCancellation(ct))
        {
            list.Add(item);
        }
        return list;
    }
}

/// <summary>
/// Comprehensive tests for Phase 1: Artifact Registration.
/// Tests that artifacts are automatically registered during graph execution
/// and that lineage tracking works correctly.
/// </summary>
public class ArtifactRegistrationTests
{
    private GraphOrchestrator<GraphContext> CreateOrchestrator(IArtifactRegistry artifactRegistry)
    {
        var services = TestServiceProvider.Create();
        return new GraphOrchestrator<GraphContext>(
            services,
            cacheStore: new InMemoryNodeCacheStore(),
            fingerprintCalculator: new HierarchicalFingerprintCalculator(),
            checkpointStore: null,
            defaultSuspensionOptions: null,
            affectedNodeDetector: new AffectedNodeDetector(new HierarchicalFingerprintCalculator()),
            snapshotStore: new HPDAgent.Graph.Core.Caching.InMemoryGraphSnapshotStore(),
            artifactRegistry: artifactRegistry
        );
    }

    /// <summary>
    /// Helper: Wait for artifact registration to complete.
    /// Artifact registration happens in a background task, so we need to poll until it's registered.
    /// </summary>
    private async Task WaitForArtifactRegistrationAsync(IArtifactRegistry registry, ArtifactKey key, PartitionKey? partition = null, int maxAttempts = 50)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            var version = await registry.GetLatestVersionAsync(key, partition);
            if (version != null)
                return;

            await Task.Delay(10); // Wait 10ms between attempts
        }
    }

    [Fact]
    public async Task ExecuteAsync_NodeWithProducesArtifact_RegistersArtifactAutomatically()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
        var targetArtifact = ArtifactKey.FromPath("output", "result");

        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "producer",
                Name = "Producer Node",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = targetArtifact
            })
            .AddEndNode()
            .AddEdge("start", "producer")
            .AddEdge("producer", "end")
            .Build();

        var context = new GraphContext("exec-1", graph, TestServiceProvider.Create());
        var orchestrator = CreateOrchestrator(artifactRegistry);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        var version = await artifactRegistry.GetLatestVersionAsync(targetArtifact);
        version.Should().NotBeNull("artifact should be registered after execution");

        var metadata = await artifactRegistry.GetMetadataAsync(targetArtifact, version!);
        metadata.Should().NotBeNull();
        metadata!.ProducedByNodeId.Should().Be("producer");
        metadata.ExecutionId.Should().Be("exec-1");
        metadata.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ExecuteAsync_MultipleNodesWithArtifacts_RegistersAllArtifacts()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
        var artifact1 = ArtifactKey.FromPath("output", "result1");
        var artifact2 = ArtifactKey.FromPath("output", "result2");

        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "producer1",
                Name = "Producer 1",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = artifact1
            })
            .AddNode(new Node
            {
                Id = "producer2",
                Name = "Producer 2",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = artifact2
            })
            .AddEndNode()
            .AddEdge("start", "producer1")
            .AddEdge("start", "producer2")
            .AddEdge("producer1", "end")
            .AddEdge("producer2", "end")
            .Build();

        var context = new GraphContext("exec-1", graph, TestServiceProvider.Create());
        var orchestrator = CreateOrchestrator(artifactRegistry);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        var version1 = await artifactRegistry.GetLatestVersionAsync(artifact1);
        version1.Should().NotBeNull();

        var version2 = await artifactRegistry.GetLatestVersionAsync(artifact2);
        version2.Should().NotBeNull();

        // Both artifacts should be registered
        var allArtifacts = await artifactRegistry.ListArtifactsAsync().ToListAsync();
        allArtifacts.Should().Contain(artifact1);
        allArtifacts.Should().Contain(artifact2);
    }

    [Fact]
    public async Task ExecuteAsync_ArtifactLineage_TracksInputArtifactVersions()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
        var inputArtifact = ArtifactKey.FromPath("intermediate", "data");
        var outputArtifact = ArtifactKey.FromPath("output", "result");

        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "upstream",
                Name = "Upstream Producer",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = inputArtifact
            })
            .AddNode(new Node
            {
                Id = "downstream",
                Name = "Downstream Consumer",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = outputArtifact,
                RequiresArtifacts = new[] { inputArtifact }
            })
            .AddEndNode()
            .AddEdge("start", "upstream")
            .AddEdge("upstream", "downstream")
            .AddEdge("downstream", "end")
            .Build();

        var context = new GraphContext("exec-1", graph, TestServiceProvider.Create());
        var orchestrator = CreateOrchestrator(artifactRegistry);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Wait for background artifact registration to complete
        await WaitForArtifactRegistrationAsync(artifactRegistry, inputArtifact);
        await WaitForArtifactRegistrationAsync(artifactRegistry, outputArtifact);

        // Assert
        var upstreamVersion = await artifactRegistry.GetLatestVersionAsync(inputArtifact);
        upstreamVersion.Should().NotBeNull();

        var downstreamVersion = await artifactRegistry.GetLatestVersionAsync(outputArtifact);
        downstreamVersion.Should().NotBeNull();

        // Check lineage: downstream artifact should track upstream artifact version
        var lineage = await artifactRegistry.GetLineageAsync(outputArtifact, downstreamVersion!);
        lineage.Should().ContainKey(inputArtifact);
        lineage[inputArtifact].Should().Be(upstreamVersion);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutArtifactRegistry_DoesNotFail()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "producer",
                Name = "Producer Node",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = ArtifactKey.FromPath("output", "result")
            })
            .AddEndNode()
            .AddEdge("start", "producer")
            .AddEdge("producer", "end")
            .Build();

        var context = new GraphContext("exec-1", graph, TestServiceProvider.Create());

        // Create orchestrator WITHOUT artifact registry
        var services = TestServiceProvider.Create();
        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            cacheStore: null,
            fingerprintCalculator: null,
            checkpointStore: null,
            defaultSuspensionOptions: null,
            affectedNodeDetector: null,
            snapshotStore: null,
            artifactRegistry: null  // No registry
        );

        // Act & Assert - should not throw
        await orchestrator.ExecuteAsync(context);
        context.CompletedNodes.Should().Contain("producer");
    }

    [Fact]
    public async Task ExecuteAsync_NodeWithoutProducesArtifact_DoesNotRegisterAnything()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();

        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "normal-node",
                Name = "Normal Node",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler"
                // No ProducesArtifact
            })
            .AddEndNode()
            .AddEdge("start", "normal-node")
            .AddEdge("normal-node", "end")
            .Build();

        var context = new GraphContext("exec-1", graph, TestServiceProvider.Create());
        var orchestrator = CreateOrchestrator(artifactRegistry);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        var allArtifacts = await artifactRegistry.ListArtifactsAsync().ToListAsync();
        allArtifacts.Should().BeEmpty("no nodes declared artifacts");
    }

    [Fact]
    public async Task ExecuteAsync_SameArtifactMultipleTimes_UpdatesVersion()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
        var targetArtifact = ArtifactKey.FromPath("output", "result");

        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "producer",
                Name = "Producer Node",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = targetArtifact
            })
            .AddEndNode()
            .AddEdge("start", "producer")
            .AddEdge("producer", "end")
            .Build();

        var orchestrator = CreateOrchestrator(artifactRegistry);

        // Act - Execute twice
        var context1 = new GraphContext("exec-1", graph, TestServiceProvider.Create());
        await orchestrator.ExecuteAsync(context1);
        await WaitForArtifactRegistrationAsync(artifactRegistry, targetArtifact);
        var version1 = await artifactRegistry.GetLatestVersionAsync(targetArtifact);

        var context2 = new GraphContext("exec-2", graph, TestServiceProvider.Create());
        await orchestrator.ExecuteAsync(context2);
        await WaitForArtifactRegistrationAsync(artifactRegistry, targetArtifact);
        var version2 = await artifactRegistry.GetLatestVersionAsync(targetArtifact);

        // Assert
        version1.Should().NotBeNull();
        version2.Should().NotBeNull();

        // Versions should be the same if inputs haven't changed
        // (fingerprint calculator produces same hash for same inputs)
        version1.Should().Be(version2, "inputs are identical so fingerprint should match");
    }

    [Fact]
    public async Task ExecuteAsync_PartitionedArtifact_RegistersWithPartitionKey()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
        var targetArtifact = ArtifactKey.FromPath("partitioned", "data");
        var partition = new PartitionKey { Dimensions = new[] { "2024-01-01" } };

        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "producer",
                Name = "Producer Node",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = new ArtifactKey
                {
                    Path = targetArtifact.Path,
                    Partition = partition  // Artifact with partition
                }
            })
            .AddEndNode()
            .AddEdge("start", "producer")
            .AddEdge("producer", "end")
            .Build();

        var context = new GraphContext("exec-1", graph, TestServiceProvider.Create());
        var orchestrator = CreateOrchestrator(artifactRegistry);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Wait for background artifact registration to complete
        await WaitForArtifactRegistrationAsync(artifactRegistry, targetArtifact, partition);

        // Assert
        var version = await artifactRegistry.GetLatestVersionAsync(targetArtifact, partition);
        version.Should().NotBeNull("partitioned artifact should be registered");

        var metadata = await artifactRegistry.GetMetadataAsync(
            new ArtifactKey { Path = targetArtifact.Path, Partition = partition },
            version!);
        metadata.Should().NotBeNull();
    }
}
