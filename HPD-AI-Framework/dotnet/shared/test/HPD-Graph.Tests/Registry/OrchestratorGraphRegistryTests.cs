using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Artifacts;
using HPDAgent.Graph.Core.Caching;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using HPDAgent.Graph.Core.Registry;
using Xunit;

namespace HPD.Graph.Tests.Registry;

/// <summary>
/// Tests for GraphOrchestrator integration with IGraphRegistry.
/// Validates stateless orchestration with explicit graph references.
/// </summary>
[Collection("MaterializationTests")] // Run sequentially to avoid lock contention
public class OrchestratorGraphRegistryTests
{
    private GraphOrchestrator<GraphContext> CreateOrchestrator(InMemoryGraphRegistry registry)
    {
        var services = TestServiceProvider.Create();
        return new GraphOrchestrator<GraphContext>(
            services,
            cacheStore: new InMemoryNodeCacheStore(),
            fingerprintCalculator: new HierarchicalFingerprintCalculator(),
            artifactRegistry: new InMemoryArtifactRegistry(),
            graphRegistry: registry
        );
    }

    #region MaterializeAsync Tests

    [Fact]
    public async Task MaterializeAsync_WithValidGraphId_ExecutesCorrectGraph()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();

        var etlGraph = new TestGraphBuilder()
            .WithId("etl-pipeline")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "extract",
                Name = "Extract Node",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = ArtifactKey.FromPath("users")
            })
            .AddEndNode()
            .AddEdge("start", "extract")
            .AddEdge("extract", "end")
            .Build();

        registry.RegisterGraph("etl", etlGraph);

        var orchestrator = CreateOrchestrator(registry);
        var context = new GraphContext("exec-1", etlGraph, TestServiceProvider.Create());

        // Execute graph first to populate artifact registry
        await orchestrator.ExecuteAsync(context);

        // Act
        var artifact = await orchestrator.MaterializeAsync<object>(
            graphId: "etl",
            artifactKey: ArtifactKey.FromPath("users")
        );

        // Assert
        artifact.Should().NotBeNull();
        artifact.Key.Path.Should().Equal("users");
        artifact.ProducedByNodeId.Should().Be("extract");
    }

    [Fact]
    public async Task MaterializeAsync_WithNonExistentGraphId_ThrowsInvalidOperationException()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();
        var orchestrator = CreateOrchestrator(registry);

        // Act
        Func<Task> act = async () => await orchestrator.MaterializeAsync<object>(
            graphId: "non-existent",
            artifactKey: ArtifactKey.FromPath("users")
        );

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Graph with ID 'non-existent' is not registered*");
    }

    [Fact]
    public async Task MaterializeAsync_WithoutRegistry_ThrowsInvalidOperationException()
    {
        // Arrange - Create orchestrator WITHOUT registry
        var services = TestServiceProvider.Create();
        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            artifactRegistry: new InMemoryArtifactRegistry()
            // No graphRegistry parameter
        );

        // Act
        Func<Task> act = async () => await orchestrator.MaterializeAsync<object>(
            graphId: "any-graph",
            artifactKey: ArtifactKey.FromPath("users")
        );

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Graph registry is not configured*");
    }

    [Fact]
    public async Task MaterializeAsync_MultipleGraphs_MaterializesFromCorrectGraph()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();

        // ETL pipeline produces "users" artifact
        var etlGraph = new TestGraphBuilder()
            .WithId("etl")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "extract-users",
                Name = "Extract Users",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = ArtifactKey.FromPath("users")
            })
            .AddEndNode()
            .AddEdge("start", "extract-users")
            .AddEdge("extract-users", "end")
            .Build();

        // ML pipeline produces "model" artifact
        var mlGraph = new TestGraphBuilder()
            .WithId("ml")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "train-model",
                Name = "Train Model",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = ArtifactKey.FromPath("model")
            })
            .AddEndNode()
            .AddEdge("start", "train-model")
            .AddEdge("train-model", "end")
            .Build();

        registry.RegisterGraph("etl", etlGraph);
        registry.RegisterGraph("ml", mlGraph);

        var orchestrator = CreateOrchestrator(registry);

        // Execute both graphs
        await orchestrator.ExecuteAsync(new GraphContext("exec-etl", etlGraph, TestServiceProvider.Create()));
        await orchestrator.ExecuteAsync(new GraphContext("exec-ml", mlGraph, TestServiceProvider.Create()));

        // Act - Materialize from ETL graph
        var usersArtifact = await orchestrator.MaterializeAsync<object>(
            graphId: "etl",
            artifactKey: ArtifactKey.FromPath("users")
        );

        // Act - Materialize from ML graph
        var modelArtifact = await orchestrator.MaterializeAsync<object>(
            graphId: "ml",
            artifactKey: ArtifactKey.FromPath("model")
        );

        // Assert
        usersArtifact.ProducedByNodeId.Should().Be("extract-users");
        modelArtifact.ProducedByNodeId.Should().Be("train-model");
    }

    #endregion

    #region MaterializeManyAsync Tests

    [Fact]
    public async Task MaterializeManyAsync_WithValidGraphId_MaterializesAllArtifacts()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();

        var graph = new TestGraphBuilder()
            .WithId("test")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "node1",
                Name = "Node 1",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = ArtifactKey.FromPath("artifact1")
            })
            .AddNode(new Node
            {
                Id = "node2",
                Name = "Node 2",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = ArtifactKey.FromPath("artifact2")
            })
            .AddEndNode()
            .AddEdge("start", "node1")
            .AddEdge("start", "node2")
            .AddEdge("node1", "end")
            .AddEdge("node2", "end")
            .Build();

        registry.RegisterGraph("test", graph);

        var orchestrator = CreateOrchestrator(registry);
        await orchestrator.ExecuteAsync(new GraphContext("exec-1", graph, TestServiceProvider.Create()));

        // Act
        var artifacts = await orchestrator.MaterializeManyAsync(
            graphId: "test",
            artifactKeys: new[]
            {
                ArtifactKey.FromPath("artifact1"),
                ArtifactKey.FromPath("artifact2")
            }
        );

        // Assert
        artifacts.Should().HaveCount(2);
        artifacts.Should().ContainKey(ArtifactKey.FromPath("artifact1"));
        artifacts.Should().ContainKey(ArtifactKey.FromPath("artifact2"));
    }

    [Fact]
    public async Task MaterializeManyAsync_WithoutRegistry_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = TestServiceProvider.Create();
        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            artifactRegistry: new InMemoryArtifactRegistry()
        );

        // Act
        Func<Task> act = async () => await orchestrator.MaterializeManyAsync(
            graphId: "any",
            artifactKeys: new[] { ArtifactKey.FromPath("artifact") }
        );

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Graph registry is not configured*");
    }

    #endregion

    #region BackfillAsync Tests

    [Fact]
    public async Task BackfillAsync_WithValidGraphId_BackfillsAllPartitions()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();

        var graph = new TestGraphBuilder()
            .WithId("partitioned")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "processor",
                Name = "Processor",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = ArtifactKey.FromPath("data"),
                Partitions = TimePartitionDefinition.Daily(
                    new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2025, 1, 3, 0, 0, 0, TimeSpan.Zero)
                )
            })
            .AddEndNode()
            .AddEdge("start", "processor")
            .AddEdge("processor", "end")
            .Build();

        registry.RegisterGraph("partitioned", graph);

        var orchestrator = CreateOrchestrator(registry);

        var partitions = new[]
        {
            new PartitionKey { Dimensions = new[] { "2025-01-01" } },
            new PartitionKey { Dimensions = new[] { "2025-01-02" } }
        };

        // Act
        var events = new List<HPD.Events.Event>();
        await foreach (var evt in orchestrator.BackfillAsync<object>(
            graphId: "partitioned",
            artifactKey: ArtifactKey.FromPath("data"),
            partitions: partitions
        ))
        {
            events.Add(evt);
        }

        // Assert
        events.Should().NotBeEmpty();
    }

    [Fact]
    public async Task BackfillAsync_WithoutRegistry_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = TestServiceProvider.Create();
        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            artifactRegistry: new InMemoryArtifactRegistry()
        );

        // Act
        async Task ActAsync()
        {
            await foreach (var _ in orchestrator.BackfillAsync<object>(
                graphId: "any",
                artifactKey: ArtifactKey.FromPath("data"),
                partitions: new[] { new PartitionKey { Dimensions = new[] { "2025-01-01" } } }
            ))
            {
                // Enumerate to trigger execution
            }
        }

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(ActAsync);
    }

    #endregion

    #region Stateless Behavior Tests

    [Fact]
    public async Task Orchestrator_ConcurrentExecutions_DifferentGraphs_IsolatedCorrectly()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();

        var graph1 = new TestGraphBuilder()
            .WithId("graph1")
            .AddStartNode()
            .AddNode(new Node { Id = "node1", Name = "Node 1", Type = NodeType.Handler, HandlerName = "SuccessHandler" })
            .AddEndNode()
            .AddEdge("start", "node1")
            .AddEdge("node1", "end")
            .Build();

        var graph2 = new TestGraphBuilder()
            .WithId("graph2")
            .AddStartNode()
            .AddNode(new Node { Id = "node2", Name = "Node 2", Type = NodeType.Handler, HandlerName = "SuccessHandler" })
            .AddEndNode()
            .AddEdge("start", "node2")
            .AddEdge("node2", "end")
            .Build();

        registry.RegisterGraph("g1", graph1);
        registry.RegisterGraph("g2", graph2);

        var orchestrator = CreateOrchestrator(registry);

        // Act - Execute both graphs concurrently
        var context1 = new GraphContext("exec-1", graph1, TestServiceProvider.Create());
        var context2 = new GraphContext("exec-2", graph2, TestServiceProvider.Create());

        var task1 = orchestrator.ExecuteAsync(context1);
        var task2 = orchestrator.ExecuteAsync(context2);

        await Task.WhenAll(task1, task2);

        // Assert - Both completed successfully with correct nodes
        context1.CompletedNodes.Should().Contain("node1");
        context1.CompletedNodes.Should().NotContain("node2");

        context2.CompletedNodes.Should().Contain("node2");
        context2.CompletedNodes.Should().NotContain("node1");
    }

    [Fact]
    public async Task Orchestrator_NoStateBetweenExecutions()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();

        var graph1 = new TestGraphBuilder().WithId("first").Build();
        var graph2 = new TestGraphBuilder().WithId("second").Build();

        registry.RegisterGraph("first", graph1);
        registry.RegisterGraph("second", graph2);

        var orchestrator = CreateOrchestrator(registry);

        // Act - Execute first graph
        var context1 = new GraphContext("exec-1", graph1, TestServiceProvider.Create());
        await orchestrator.ExecuteAsync(context1);

        // Execute second graph
        var context2 = new GraphContext("exec-2", graph2, TestServiceProvider.Create());
        await orchestrator.ExecuteAsync(context2);

        // Assert - Orchestrator should have no state from previous execution
        // This is validated by the fact that both executions succeed independently
        context1.IsComplete.Should().BeTrue();
        context2.IsComplete.Should().BeTrue();
    }

    #endregion

    #region Error Message Tests

    [Fact]
    public async Task MaterializeAsync_NonExistentGraph_ProvidesClearErrorWithAvailableGraphs()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();
        registry.RegisterGraph("etl", new TestGraphBuilder().Build());
        registry.RegisterGraph("ml", new TestGraphBuilder().Build());
        registry.RegisterGraph("report", new TestGraphBuilder().Build());

        var orchestrator = CreateOrchestrator(registry);

        // Act
        Func<Task> act = async () => await orchestrator.MaterializeAsync<object>(
            graphId: "invalid",
            artifactKey: ArtifactKey.FromPath("data")
        );

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Graph with ID 'invalid' is not registered*")
            .WithMessage("*Available graphs: *");
    }

    #endregion
}
