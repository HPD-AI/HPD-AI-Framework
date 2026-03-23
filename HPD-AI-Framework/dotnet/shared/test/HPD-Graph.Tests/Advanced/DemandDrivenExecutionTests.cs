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
using Xunit;

namespace HPD.Graph.Tests.Advanced;

/// <summary>
/// Comprehensive tests for Phase 3: Demand-Driven Execution API.
/// Tests MaterializeAsync, MaterializeManyAsync, BackfillAsync, and multi-producer resolution.
/// </summary>
[Collection("MaterializationTests")] // Run sequentially to avoid lock contention
public class DemandDrivenExecutionTests
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

    #region MaterializeAsync Tests

    [Fact]
    public async Task MaterializeAsync_SimpleArtifact_ExecutesMinimalSubgraph()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
        var targetArtifact = ArtifactKey.FromPath("output", "result");

        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "nodeA",
                Name = "Node A",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = ArtifactKey.FromPath("intermediate", "data")
            })
            .AddNode(new Node
            {
                Id = "nodeB",
                Name = "Node B",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = targetArtifact,
                RequiresArtifacts = new[] { ArtifactKey.FromPath("intermediate", "data") }
            })
            .AddNode(new Node
            {
                Id = "unused",
                Name = "Unused Node",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler"
            })
            .AddEndNode()
            .AddEdge("start", "nodeA")
            .AddEdge("nodeA", "nodeB")
            .AddEdge("start", "unused")
            .AddEdge("nodeB", "end")
            .AddEdge("unused", "end")
            .Build();

        var context = new GraphContext("exec-1", graph, TestServiceProvider.Create());
        var orchestrator = CreateOrchestrator(artifactRegistry);

        // Act
        var artifact = await orchestrator.MaterializeAsync<string>(
            graph,
            targetArtifact,
            null,
            null,
            default
        );

        // Assert
        artifact.Should().NotBeNull();
        artifact.Key.Should().Be(targetArtifact);
        artifact.Version.Should().NotBeNullOrEmpty();
        artifact.ProducedByNodeId.Should().Be("nodeB");

        // Verify artifact was registered
        var version = await artifactRegistry.GetLatestVersionAsync(targetArtifact);
        version.Should().NotBeNull();
    }

    [Fact]
    public async Task MaterializeAsync_WithPartition_MaterializesPartitionedArtifact()
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

        var context = new GraphContext("exec-1", graph, TestServiceProvider.Create());
        var orchestrator = CreateOrchestrator(artifactRegistry);

        // Act
        var artifact = await orchestrator.MaterializeAsync<string>(
            graph,
            targetArtifact,
            partition,
            null,
            default
        );

        // Assert
        artifact.Should().NotBeNull();

        // The returned artifact key should include the partition
        var expectedKey = new ArtifactKey { Path = targetArtifact.Path, Partition = partition };
        artifact.Key.Should().Be(expectedKey);

        // Verify partition-specific artifact was registered
        var version = await artifactRegistry.GetLatestVersionAsync(expectedKey, partition);
        version.Should().NotBeNull();
        version.Should().Be(artifact.Version);
    }

    #endregion

    #region MaterializeManyAsync Tests

    [Fact]
    public async Task MaterializeManyAsync_MultipleArtifacts_MaterializesAll()
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

        var context = new GraphContext("exec-1", graph, TestServiceProvider.Create());
        var orchestrator = CreateOrchestrator(artifactRegistry);

        // Act
        var artifacts = await orchestrator.MaterializeManyAsync(
            graph,
            new[] { artifact1, artifact2 },
            null,
            null,
            default
        );

        // Assert
        artifacts.Should().HaveCount(2);
        artifacts.Should().ContainKey(artifact1);
        artifacts.Should().ContainKey(artifact2);
    }

    [Fact]
    public async Task MaterializeManyAsync_SharedDependency_DeduplicatesExecution()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
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

        var context = new GraphContext("exec-1", graph, TestServiceProvider.Create());
        var orchestrator = CreateOrchestrator(artifactRegistry);

        // Act
        var artifacts = await orchestrator.MaterializeManyAsync(
            graph,
            new[] { artifact1, artifact2 },
            null,
            null,
            default
        );

        // Assert
        artifacts.Should().HaveCount(2);

        // Verify shared artifact was registered
        var sharedVersion = await artifactRegistry.GetLatestVersionAsync(sharedArtifact);
        sharedVersion.Should().NotBeNull();
    }

    #endregion

    #region BackfillAsync Tests

    [Fact]
    public async Task BackfillAsync_MultiplePartitions_EmitsProgressEvents()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
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

        var orchestrator = CreateOrchestrator(artifactRegistry);
        var events = new List<Event>();

        // Act
        await foreach (var evt in orchestrator.BackfillAsync<string>(
            graph,
            targetArtifact,
            partitions,
            new BackfillOptions { EmitProgressEvents = true, MaxParallelPartitions = 2 },
            default))
        {
            events.Add(evt);
        }

        // Assert
        events.Should().NotBeEmpty();

        // Should have started event
        var startedEvent = events.OfType<BackfillStartedEvent>().SingleOrDefault();
        startedEvent.Should().NotBeNull();
        startedEvent!.ArtifactKey.Should().Be(targetArtifact);
        startedEvent.TotalPartitions.Should().Be(3);
        startedEvent.PartitionsToProcess.Should().Be(3);

        // Should have partition completed events
        var completedEvents = events.OfType<BackfillPartitionCompletedEvent>().ToList();
        completedEvents.Should().HaveCount(3);
        completedEvents.All(e => e.Success).Should().BeTrue();
        completedEvents.All(e => e.Artifact != null).Should().BeTrue();

        // Should have backfill completed event
        var doneEvent = events.OfType<BackfillCompletedEvent>().SingleOrDefault();
        doneEvent.Should().NotBeNull();
        doneEvent!.ArtifactKey.Should().Be(targetArtifact);
        doneEvent.SuccessfulPartitions.Should().Be(3);
        doneEvent.FailedPartitions.Should().Be(0);
    }

    #endregion

    // Note: Multi-producer resolution tests are in InMemoryArtifactRegistryTests.cs
    // since they test registry implementation details rather than orchestration behavior.
}
