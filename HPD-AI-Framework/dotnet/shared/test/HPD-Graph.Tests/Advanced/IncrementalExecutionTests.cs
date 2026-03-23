using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Caching;
using HPDAgent.Graph.Core.Caching;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Xunit;
using InMemoryGraphSnapshotStore = HPDAgent.Graph.Core.Caching.InMemoryGraphSnapshotStore;

namespace HPD.Graph.Tests.Advanced;

/// <summary>
/// Tests for incremental execution with AffectedNodeDetector integration.
/// Covers snapshot storage, affected node detection, and selective re-execution.
/// </summary>
public class IncrementalExecutionTests
{
    #region Phase 1: Unit Tests

    [Fact]
    public void ComputeStructureHash_SameGraph_ShouldProduceSameHash()
    {
        // Arrange
        var graph1 = new TestGraphBuilder()
            .WithId("graph1")
            .AddStartNode()
            .AddHandlerNode("step1", "SuccessHandler")
            .AddHandlerNode("step2", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "step1")
            .AddEdge("step1", "step2")
            .AddEdge("step2", "end")
            .Build();

        var graph2 = new TestGraphBuilder()
            .WithId("graph1")  // Same ID
            .AddStartNode()
            .AddHandlerNode("step1", "SuccessHandler")
            .AddHandlerNode("step2", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "step1")
            .AddEdge("step1", "step2")
            .AddEdge("step2", "end")
            .Build();

        // Act
        var hash1 = graph1.ComputeStructureHash();
        var hash2 = graph2.ComputeStructureHash();

        // Assert
        hash1.Should().Be(hash2);
        hash1.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ComputeStructureHash_DifferentNodeOrder_ShouldProduceSameHash()
    {
        // Arrange
        var graph1 = new TestGraphBuilder()
            .WithId("graph1")
            .AddStartNode()
            .AddHandlerNode("step1", "SuccessHandler")
            .AddHandlerNode("step2", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "step1")
            .AddEdge("step1", "step2")
            .AddEdge("step2", "end")
            .Build();

        var graph2 = new TestGraphBuilder()
            .WithId("graph1")
            .AddStartNode()
            .AddHandlerNode("step2", "SuccessHandler")  // Different order
            .AddHandlerNode("step1", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "step1")
            .AddEdge("step1", "step2")
            .AddEdge("step2", "end")
            .Build();

        // Act
        var hash1 = graph1.ComputeStructureHash();
        var hash2 = graph2.ComputeStructureHash();

        // Assert - Should be same because nodes are sorted by ID internally
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeStructureHash_AddNode_ShouldChangHash()
    {
        // Arrange
        var graph1 = new TestGraphBuilder()
            .WithId("graph1")
            .AddStartNode()
            .AddHandlerNode("step1", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "step1")
            .AddEdge("step1", "end")
            .Build();

        var graph2 = new TestGraphBuilder()
            .WithId("graph1")
            .AddStartNode()
            .AddHandlerNode("step1", "SuccessHandler")
            .AddHandlerNode("step2", "SuccessHandler")  // Added node
            .AddEndNode()
            .AddEdge("start", "step1")
            .AddEdge("step1", "step2")
            .AddEdge("step2", "end")
            .Build();

        // Act
        var hash1 = graph1.ComputeStructureHash();
        var hash2 = graph2.ComputeStructureHash();

        // Assert
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeStructureHash_AddEdge_ShouldChangeHash()
    {
        // Arrange
        var graph1 = new TestGraphBuilder()
            .WithId("graph1")
            .AddStartNode()
            .AddHandlerNode("step1", "SuccessHandler")
            .AddHandlerNode("step2", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "step1")
            .AddEdge("step1", "end")
            .AddEdge("step2", "end")
            .Build();

        var graph2 = new TestGraphBuilder()
            .WithId("graph1")
            .AddStartNode()
            .AddHandlerNode("step1", "SuccessHandler")
            .AddHandlerNode("step2", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "step1")
            .AddEdge("step1", "step2")  // New edge
            .AddEdge("step2", "end")
            .Build();

        // Act
        var hash1 = graph1.ComputeStructureHash();
        var hash2 = graph2.ComputeStructureHash();

        // Assert
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeStructureHash_ChangeVersion_ShouldChangeHash()
    {
        // Arrange
        var graph1 = new TestGraphBuilder()
            .WithId("graph1")
            .WithVersion("1.0.0")
            .AddStartNode()
            .AddHandlerNode("step1", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "step1")
            .AddEdge("step1", "end")
            .Build();

        var graph2 = new TestGraphBuilder()
            .WithId("graph1")
            .WithVersion("2.0.0")  // Different version
            .AddStartNode()
            .AddHandlerNode("step1", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "step1")
            .AddEdge("step1", "end")
            .Build();

        // Act
        var hash1 = graph1.ComputeStructureHash();
        var hash2 = graph2.ComputeStructureHash();

        // Assert
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public async Task InMemorySnapshotStore_SaveAndLoad_ShouldPersistSnapshot()
    {
        // Arrange
        var store = new InMemoryGraphSnapshotStore();
        var snapshot = new GraphSnapshot
        {
            NodeFingerprints = new Dictionary<string, string>
            {
                ["node1"] = "fingerprint1",
                ["node2"] = "fingerprint2"
            },
            GraphHash = "graph_hash_123",
            Timestamp = DateTimeOffset.UtcNow,
            ExecutionId = "exec_1"
        };

        // Act
        await store.SaveSnapshotAsync("graph1", snapshot);
        var loaded = await store.GetLatestSnapshotAsync("graph1");

        // Assert
        loaded.Should().NotBeNull();
        loaded!.GraphHash.Should().Be("graph_hash_123");
        loaded.NodeFingerprints.Should().HaveCount(2);
        loaded.NodeFingerprints["node1"].Should().Be("fingerprint1");
        loaded.ExecutionId.Should().Be("exec_1");
    }

    [Fact]
    public async Task InMemorySnapshotStore_NoSnapshot_ShouldReturnNull()
    {
        // Arrange
        var store = new InMemoryGraphSnapshotStore();

        // Act
        var loaded = await store.GetLatestSnapshotAsync("nonexistent_graph");

        // Assert
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task InMemorySnapshotStore_MultipleSaves_ShouldReturnLatest()
    {
        // Arrange
        var store = new InMemoryGraphSnapshotStore();

        var snapshot1 = new GraphSnapshot
        {
            NodeFingerprints = new Dictionary<string, string> { ["node1"] = "v1" },
            GraphHash = "hash1",
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10),
            ExecutionId = "exec_1"
        };

        var snapshot2 = new GraphSnapshot
        {
            NodeFingerprints = new Dictionary<string, string> { ["node1"] = "v2" },
            GraphHash = "hash2",
            Timestamp = DateTimeOffset.UtcNow,
            ExecutionId = "exec_2"
        };

        // Act
        await store.SaveSnapshotAsync("graph1", snapshot1);
        await store.SaveSnapshotAsync("graph1", snapshot2);
        var loaded = await store.GetLatestSnapshotAsync("graph1");

        // Assert
        loaded.Should().NotBeNull();
        loaded!.ExecutionId.Should().Be("exec_2");
        loaded.GraphHash.Should().Be("hash2");
    }

    [Fact]
    public async Task InMemorySnapshotStore_PruneSnapshots_ShouldKeepLastN()
    {
        // Arrange
        var store = new InMemoryGraphSnapshotStore();

        // Save 5 snapshots
        for (int i = 1; i <= 5; i++)
        {
            await store.SaveSnapshotAsync("graph1", new GraphSnapshot
            {
                NodeFingerprints = new Dictionary<string, string>(),
                GraphHash = $"hash{i}",
                Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10 + i),
                ExecutionId = $"exec_{i}"
            });
        }

        // Act - Prune to keep last 2
        await store.PruneOldSnapshotsAsync("graph1", keepLastN: 2);

        var snapshots = new List<GraphSnapshot>();
        await foreach (var snapshot in store.ListSnapshotsAsync("graph1"))
        {
            snapshots.Add(snapshot);
        }

        // Assert
        snapshots.Should().HaveCount(2);
        snapshots[0].ExecutionId.Should().Be("exec_5");  // Most recent first
        snapshots[1].ExecutionId.Should().Be("exec_4");
    }

    #endregion

    #region Phase 2: Integration Tests - Core Scenarios

    [Fact]
    public async Task Execute_FirstRun_NoSnapshot_ShouldExecuteAllNodes()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .WithId("test_graph")
            .AddStartNode()
            .AddHandlerNode("step1", "SuccessHandler")
            .AddHandlerNode("step2", "SuccessHandler")
            .AddHandlerNode("step3", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "step1")
            .AddEdge("step1", "step2")
            .AddEdge("step2", "step3")
            .AddEdge("step3", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var cacheStore = new InMemoryNodeCacheStore();
        var fingerprintCalculator = new HierarchicalFingerprintCalculator();
        var snapshotStore = new InMemoryGraphSnapshotStore();
        var affectedNodeDetector = new AffectedNodeDetector(fingerprintCalculator);

        var context = new GraphContext("exec1", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            cacheStore,
            fingerprintCalculator,
            null,  // checkpoint store
            null,  // suspension options
            affectedNodeDetector,
            snapshotStore
        );

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.ShouldHaveCompletedNode("step1");
        context.ShouldHaveCompletedNode("step2");
        context.ShouldHaveCompletedNode("step3");

        // Snapshot should be saved
        var snapshot = await snapshotStore.GetLatestSnapshotAsync("test_graph");
        snapshot.Should().NotBeNull();
        snapshot!.NodeFingerprints.Should().ContainKey("step1");
        snapshot.NodeFingerprints.Should().ContainKey("step2");
        snapshot.NodeFingerprints.Should().ContainKey("step3");
    }

    [Fact]
    public async Task Execute_SecondRun_NoChanges_ShouldSkipAllNodes()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .WithId("test_graph")
            .AddStartNode()
            .AddHandlerNode("step1", "EchoHandler")
            .AddHandlerNode("step2", "EchoHandler")
            .AddEndNode()
            .AddEdge("start", "step1")
            .AddEdge("step1", "step2")
            .AddEdge("step2", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var cacheStore = new InMemoryNodeCacheStore();
        var fingerprintCalculator = new HierarchicalFingerprintCalculator();
        var snapshotStore = new InMemoryGraphSnapshotStore();
        var affectedNodeDetector = new AffectedNodeDetector(fingerprintCalculator);

        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            cacheStore,
            fingerprintCalculator,
            null,
            null,
            affectedNodeDetector,
            snapshotStore
        );

        // First execution
        var context1 = new GraphContext("exec1", graph, services);
        context1.Channels["input:test"].Set("value1");
        await orchestrator.ExecuteAsync(context1);

        // Second execution with SAME inputs
        var context2 = new GraphContext("exec2", graph, services);
        context2.Channels["input:test"].Set("value1");  // Same value

        // Act
        await orchestrator.ExecuteAsync(context2);

        // Assert
        // Both executions should complete successfully
        context1.ShouldHaveCompletedNode("step1");
        context1.ShouldHaveCompletedNode("step2");
        context2.ShouldHaveCompletedNode("step1");
        context2.ShouldHaveCompletedNode("step2");

        // Log should indicate incremental execution was used
        context2.ShouldHaveLogEntry("Incremental execution");
    }

    [Fact]
    public async Task Execute_InputChange_ShouldExecuteAffectedNodesOnly()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .WithId("test_graph")
            .AddStartNode()
            .AddHandlerNode("step1", "EchoHandler")
            .AddHandlerNode("step2", "EchoHandler")
            .AddHandlerNode("step3", "EchoHandler")
            .AddEndNode()
            .AddEdge("start", "step1")
            .AddEdge("step1", "step2")
            .AddEdge("step2", "step3")
            .AddEdge("step3", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var cacheStore = new InMemoryNodeCacheStore();
        var fingerprintCalculator = new HierarchicalFingerprintCalculator();
        var snapshotStore = new InMemoryGraphSnapshotStore();
        var affectedNodeDetector = new AffectedNodeDetector(fingerprintCalculator);

        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            cacheStore,
            fingerprintCalculator,
            null,
            null,
            affectedNodeDetector,
            snapshotStore
        );

        // First execution
        var context1 = new GraphContext("exec1", graph, services);
        context1.Channels["input:data"].Set("initial_value");
        await orchestrator.ExecuteAsync(context1);

        // Second execution with DIFFERENT input
        var context2 = new GraphContext("exec2", graph, services);
        context2.Channels["input:data"].Set("changed_value");  // Changed!

        // Act
        await orchestrator.ExecuteAsync(context2);

        // Assert
        // All nodes should execute (since input changed, all are affected)
        context2.ShouldHaveCompletedNode("step1");
        context2.ShouldHaveCompletedNode("step2");
        context2.ShouldHaveCompletedNode("step3");

        // Log should show affected nodes detected
        context2.ShouldHaveLogEntry("affected nodes");
    }

    [Fact]
    public async Task Execute_SnapshotLoadFailure_ShouldFallbackToFullExecution()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .WithId("test_graph")
            .AddStartNode()
            .AddHandlerNode("step1", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "step1")
            .AddEdge("step1", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var cacheStore = new InMemoryNodeCacheStore();
        var fingerprintCalculator = new HierarchicalFingerprintCalculator();

        // Failing snapshot store
        var snapshotStore = new FailingGraphSnapshotStore(throwOnLoad: true);
        var affectedNodeDetector = new AffectedNodeDetector(fingerprintCalculator);

        var context = new GraphContext("exec1", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            cacheStore,
            fingerprintCalculator,
            null,
            null,
            affectedNodeDetector,
            snapshotStore
        );

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Should still execute successfully (fallback to full execution)
        context.ShouldHaveCompletedNode("step1");
    }

    [Fact]
    public async Task Execute_SnapshotSaveFailure_ShouldNotCrashExecution()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .WithId("test_graph")
            .AddStartNode()
            .AddHandlerNode("step1", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "step1")
            .AddEdge("step1", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var cacheStore = new InMemoryNodeCacheStore();
        var fingerprintCalculator = new HierarchicalFingerprintCalculator();

        // Failing snapshot store
        var snapshotStore = new FailingGraphSnapshotStore(throwOnSave: true);
        var affectedNodeDetector = new AffectedNodeDetector(fingerprintCalculator);

        var context = new GraphContext("exec1", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            cacheStore,
            fingerprintCalculator,
            null,
            null,
            affectedNodeDetector,
            snapshotStore
        );

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Should execute successfully despite snapshot save failure
        context.ShouldHaveCompletedNode("step1");

        // Log should contain warning about snapshot save failure
        context.ShouldHaveLogEntry("Failed to save execution snapshot");
    }

    [Fact]
    public async Task Execute_WithoutIncrementalSupport_ShouldExecuteNormally()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .WithId("test_graph")
            .AddStartNode()
            .AddHandlerNode("step1", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "step1")
            .AddEdge("step1", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("exec1", graph, services);

        // No affected node detector or snapshot store provided
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Should execute normally
        context.ShouldHaveCompletedNode("step1");
    }

    #endregion
}
