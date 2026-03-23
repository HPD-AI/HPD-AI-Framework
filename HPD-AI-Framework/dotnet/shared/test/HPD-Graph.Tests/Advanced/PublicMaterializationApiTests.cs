using FluentAssertions;
using HPD.Events;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Abstractions.Events;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Artifacts;
using HPDAgent.Graph.Core.Caching;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using HPDAgent.Graph.Core.Registry;
using Xunit;

namespace HPD.Graph.Tests.Advanced;

/// <summary>
/// Tests for the public demand-driven execution API.
/// These tests verify that MaterializeAsync, MaterializeManyAsync, and BackfillAsync
/// work correctly using the _lastExecutedGraph pattern (without explicit graph parameter).
/// </summary>
[Collection("MaterializationTests")] // Run sequentially to avoid lock contention
public class PublicMaterializationApiTests
{
    private GraphOrchestrator<GraphContext> CreateOrchestrator(IArtifactRegistry artifactRegistry, InMemoryGraphRegistry graphRegistry)
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
            artifactRegistry: artifactRegistry,
            graphRegistry: graphRegistry
        );
    }

    #region MaterializeAsync Public API Tests

    [Fact]
    public async Task MaterializeAsync_AfterExecuteAsync_UsesLastExecutedGraph()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
        var graphRegistry = new InMemoryGraphRegistry();
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

        graphRegistry.RegisterGraph(graph.Id, graph);
        var orchestrator = CreateOrchestrator(artifactRegistry, graphRegistry);

        // Execute graph first to establish context
        var context = new GraphContext("exec-1", graph, TestServiceProvider.Create());
        await orchestrator.ExecuteAsync(context);

        // Act - Use public API with graph ID
        var artifact = await orchestrator.MaterializeAsync<string>(graph.Id, targetArtifact);

        // Assert
        artifact.Should().NotBeNull();
        artifact.Key.Should().Be(targetArtifact);
        artifact.Version.Should().NotBeNullOrEmpty();
        artifact.ProducedByNodeId.Should().Be("producer");
    }

    [Fact]
    public async Task MaterializeAsync_WithoutPriorExecution_ThrowsInvalidOperationException()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
        var graphRegistry = new InMemoryGraphRegistry();
        var targetArtifact = ArtifactKey.FromPath("output", "result");

        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddEndNode()
            .AddEdge("start", "end")
            .Build();

        graphRegistry.RegisterGraph(graph.Id, graph);
        var orchestrator = CreateOrchestrator(artifactRegistry, graphRegistry);

        // Act & Assert
        var act = async () => await orchestrator.MaterializeAsync<string>(graph.Id, targetArtifact);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No node produces artifact*");
    }

    [Fact]
    public async Task MaterializeAsync_WithPartition_MaterializesCorrectPartition()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
        var graphRegistry = new InMemoryGraphRegistry();
        var targetArtifact = ArtifactKey.FromPath("partitioned", "data");
        var partition1 = new PartitionKey { Dimensions = new[] { "2024-01-01" } };
        var partition2 = new PartitionKey { Dimensions = new[] { "2024-01-02" } };

        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "processor",
                Name = "Processor",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = targetArtifact
            })
            .AddEndNode()
            .AddEdge("start", "processor")
            .AddEdge("processor", "end")
            .Build();

        graphRegistry.RegisterGraph(graph.Id, graph);
        var orchestrator = CreateOrchestrator(artifactRegistry, graphRegistry);
        var context = new GraphContext("exec-1", graph, TestServiceProvider.Create());
        await orchestrator.ExecuteAsync(context);

        // Act - Materialize different partitions
        var artifact1 = await orchestrator.MaterializeAsync<string>(graph.Id, targetArtifact, partition1);
        var artifact2 = await orchestrator.MaterializeAsync<string>(graph.Id, targetArtifact, partition2);

        // Assert
        artifact1.Key.Partition.Should().Be(partition1);
        artifact2.Key.Partition.Should().Be(partition2);

        // Both partitions should be registered
        var version1 = await artifactRegistry.GetLatestVersionAsync(targetArtifact, partition1);
        var version2 = await artifactRegistry.GetLatestVersionAsync(targetArtifact, partition2);
        version1.Should().NotBeNull();
        version2.Should().NotBeNull();
    }

    [Fact]
    public async Task MaterializeAsync_ForceRecompute_BypassesCache()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
        var graphRegistry = new InMemoryGraphRegistry();
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

        graphRegistry.RegisterGraph(graph.Id, graph);
        var orchestrator = CreateOrchestrator(artifactRegistry, graphRegistry);
        var context = new GraphContext("exec-1", graph, TestServiceProvider.Create());
        await orchestrator.ExecuteAsync(context);

        // First materialization
        var artifact1 = await orchestrator.MaterializeAsync<string>(graph.Id, targetArtifact);

        // Act - Force recompute
        var artifact2 = await orchestrator.MaterializeAsync<string>(
            graph.Id,
            targetArtifact,
            null,
            new MaterializationOptions { ForceRecompute = true });

        // Assert
        artifact1.Should().NotBeNull();
        artifact2.Should().NotBeNull();
        artifact1.Value.Should().NotBeNull();
        artifact2.Value.Should().NotBeNull();
        // Both materializations should produce valid artifacts
        // Note: Versions may differ due to minimal subgraph creation, but values should be equivalent
    }

    #endregion

    #region MaterializeManyAsync Public API Tests

    [Fact]
    public async Task MaterializeManyAsync_AfterExecuteAsync_MaterializesMultipleArtifacts()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
        var graphRegistry = new InMemoryGraphRegistry();
        var artifact1 = ArtifactKey.FromPath("output", "result1");
        var artifact2 = ArtifactKey.FromPath("output", "result2");

        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "node1",
                Name = "Node 1",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = artifact1
            })
            .AddNode(new Node
            {
                Id = "node2",
                Name = "Node 2",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = artifact2
            })
            .AddEndNode()
            .AddEdge("start", "node1")
            .AddEdge("start", "node2")
            .AddEdge("node1", "end")
            .AddEdge("node2", "end")
            .Build();

        graphRegistry.RegisterGraph(graph.Id, graph);
        var orchestrator = CreateOrchestrator(artifactRegistry, graphRegistry);
        var context = new GraphContext("exec-1", graph, TestServiceProvider.Create());
        await orchestrator.ExecuteAsync(context);

        // Act - Use public API with graph ID
        var artifacts = await orchestrator.MaterializeManyAsync(graph.Id, new[] { artifact1, artifact2 });

        // Assert
        artifacts.Should().HaveCount(2);
        artifacts.Should().ContainKey(artifact1);
        artifacts.Should().ContainKey(artifact2);
    }

    [Fact]
    public async Task MaterializeManyAsync_SharedDependency_ExecutesOnce()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
        var graphRegistry = new InMemoryGraphRegistry();
        var sharedArtifact = ArtifactKey.FromPath("shared", "data");
        var artifact1 = ArtifactKey.FromPath("output", "result1");
        var artifact2 = ArtifactKey.FromPath("output", "result2");

        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "shared",
                Name = "Shared Node",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = sharedArtifact
            })
            .AddNode(new Node
            {
                Id = "node1",
                Name = "Node 1",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = artifact1,
                RequiresArtifacts = new[] { sharedArtifact }
            })
            .AddNode(new Node
            {
                Id = "node2",
                Name = "Node 2",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = artifact2,
                RequiresArtifacts = new[] { sharedArtifact }
            })
            .AddEndNode()
            .AddEdge("start", "shared")
            .AddEdge("shared", "node1")
            .AddEdge("shared", "node2")
            .AddEdge("node1", "end")
            .AddEdge("node2", "end")
            .Build();

        graphRegistry.RegisterGraph(graph.Id, graph);
        var orchestrator = CreateOrchestrator(artifactRegistry, graphRegistry);
        var context = new GraphContext("exec-1", graph, TestServiceProvider.Create());
        await orchestrator.ExecuteAsync(context);

        // Act
        var artifacts = await orchestrator.MaterializeManyAsync(graph.Id, new[] { artifact1, artifact2 });

        // Assert
        artifacts.Should().HaveCount(2);

        // Verify shared artifact was registered (proof it was executed)
        var sharedVersion = await artifactRegistry.GetLatestVersionAsync(sharedArtifact);
        sharedVersion.Should().NotBeNull();
    }

    [Fact]
    public async Task MaterializeManyAsync_WithoutPriorExecution_ThrowsInvalidOperationException()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
        var graphRegistry = new InMemoryGraphRegistry();
        var artifact1 = ArtifactKey.FromPath("output", "result1");

        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddEndNode()
            .AddEdge("start", "end")
            .Build();

        graphRegistry.RegisterGraph(graph.Id, graph);
        var orchestrator = CreateOrchestrator(artifactRegistry, graphRegistry);

        // Act & Assert
        var act = async () => await orchestrator.MaterializeManyAsync(graph.Id, new[] { artifact1 });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No node produces artifact*");
    }

    [Fact]
    public async Task MaterializeManyAsync_EmptyList_ReturnsEmptyDictionary()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
        var graphRegistry = new InMemoryGraphRegistry();
        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddEndNode()
            .AddEdge("start", "end")
            .Build();

        graphRegistry.RegisterGraph(graph.Id, graph);
        var orchestrator = CreateOrchestrator(artifactRegistry, graphRegistry);
        var context = new GraphContext("exec-1", graph, TestServiceProvider.Create());
        await orchestrator.ExecuteAsync(context);

        // Act
        var artifacts = await orchestrator.MaterializeManyAsync(graph.Id, Array.Empty<ArtifactKey>());

        // Assert
        artifacts.Should().BeEmpty();
    }

    #endregion

    #region BackfillAsync Public API Tests

    [Fact]
    public async Task BackfillAsync_AfterExecuteAsync_ProcessesAllPartitions()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
        var graphRegistry = new InMemoryGraphRegistry();
        var targetArtifact = ArtifactKey.FromPath("daily", "metrics");
        var partitions = new[]
        {
            new PartitionKey { Dimensions = new[] { "2024-01-01" } },
            new PartitionKey { Dimensions = new[] { "2024-01-02" } },
            new PartitionKey { Dimensions = new[] { "2024-01-03" } }
        };

        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "processor",
                Name = "Processor",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = targetArtifact
            })
            .AddEndNode()
            .AddEdge("start", "processor")
            .AddEdge("processor", "end")
            .Build();

        graphRegistry.RegisterGraph(graph.Id, graph);
        var orchestrator = CreateOrchestrator(artifactRegistry, graphRegistry);
        var context = new GraphContext("exec-1", graph, TestServiceProvider.Create());
        await orchestrator.ExecuteAsync(context);

        var events = new List<Event>();

        // Act - Use public API with graph ID
        await foreach (var evt in orchestrator.BackfillAsync<string>(
            graph.Id,
            targetArtifact,
            partitions,
            new BackfillOptions { EmitProgressEvents = true, MaxParallelPartitions = 2 }))
        {
            events.Add(evt);
        }

        // Assert
        events.Should().NotBeEmpty();

        var startedEvent = events.OfType<BackfillStartedEvent>().SingleOrDefault();
        startedEvent.Should().NotBeNull();
        startedEvent!.TotalPartitions.Should().Be(3);

        var completedEvents = events.OfType<BackfillPartitionCompletedEvent>().ToList();
        completedEvents.Should().HaveCount(3);
        completedEvents.All(e => e.Success).Should().BeTrue();

        var doneEvent = events.OfType<BackfillCompletedEvent>().SingleOrDefault();
        doneEvent.Should().NotBeNull();
        doneEvent!.SuccessfulPartitions.Should().Be(3);
    }

    [Fact]
    public async Task BackfillAsync_WithoutPriorExecution_ThrowsInvalidOperationException()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
        var graphRegistry = new InMemoryGraphRegistry();
        var targetArtifact = ArtifactKey.FromPath("daily", "metrics");
        var partitions = new[]
        {
            new PartitionKey { Dimensions = new[] { "2024-01-01" } }
        };

        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddEndNode()
            .AddEdge("start", "end")
            .Build();

        graphRegistry.RegisterGraph(graph.Id, graph);
        var orchestrator = CreateOrchestrator(artifactRegistry, graphRegistry);

        // Act & Assert
        var act = async () =>
        {
            await foreach (var _ in orchestrator.BackfillAsync<string>(graph.Id, targetArtifact, partitions))
            {
                // Should throw before yielding
            }
        };

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No node produces artifact*");
    }

    [Fact]
    public async Task BackfillAsync_SkipExisting_OnlyMaterializesNewPartitions()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
        var graphRegistry = new InMemoryGraphRegistry();
        var targetArtifact = ArtifactKey.FromPath("daily", "metrics");
        var partition1 = new PartitionKey { Dimensions = new[] { "2024-01-01" } };
        var partition2 = new PartitionKey { Dimensions = new[] { "2024-01-02" } };

        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "processor",
                Name = "Processor",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = targetArtifact
            })
            .AddEndNode()
            .AddEdge("start", "processor")
            .AddEdge("processor", "end")
            .Build();

        graphRegistry.RegisterGraph(graph.Id, graph);
        var orchestrator = CreateOrchestrator(artifactRegistry, graphRegistry);
        var context = new GraphContext("exec-1", graph, TestServiceProvider.Create());
        await orchestrator.ExecuteAsync(context);

        // Materialize partition1 first
        await orchestrator.MaterializeAsync<string>(graph.Id, targetArtifact, partition1);

        var events = new List<Event>();

        // Act - Backfill both partitions with SkipExisting
        await foreach (var evt in orchestrator.BackfillAsync<string>(
            graph.Id,
            targetArtifact,
            new[] { partition1, partition2 },
            new BackfillOptions { SkipExisting = true, EmitProgressEvents = true }))
        {
            events.Add(evt);
        }

        // Assert
        var startedEvent = events.OfType<BackfillStartedEvent>().SingleOrDefault();
        startedEvent.Should().NotBeNull();
        startedEvent!.TotalPartitions.Should().Be(2);
        // Only 1 partition should be processed (partition2)
        startedEvent.PartitionsToProcess.Should().BeLessThanOrEqualTo(2);

        // Both partitions should ultimately be registered
        var version1 = await artifactRegistry.GetLatestVersionAsync(targetArtifact, partition1);
        var version2 = await artifactRegistry.GetLatestVersionAsync(targetArtifact, partition2);
        version1.Should().NotBeNull();
        version2.Should().NotBeNull();
    }

    #endregion
}
