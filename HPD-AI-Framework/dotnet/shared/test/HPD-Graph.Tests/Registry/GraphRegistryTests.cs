using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Registry;
using Xunit;

namespace HPD.Graph.Tests.Registry;

/// <summary>
/// Tests for graph registry functionality.
/// Validates thread-safe registration, lookup, and multi-graph scenarios.
/// </summary>
public class GraphRegistryTests
{
    #region Basic Registration Tests

    [Fact]
    public void RegisterGraph_WithValidGraph_Succeeds()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();
        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddEndNode()
            .Build();

        // Act
        registry.RegisterGraph("test-graph", graph);

        // Assert
        registry.ContainsGraph("test-graph").Should().BeTrue();
        registry.Count.Should().Be(1);
    }

    [Fact]
    public void RegisterGraph_WithNullGraphId_ThrowsArgumentException()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();
        var graph = new TestGraphBuilder().Build();

        // Act & Assert
        Action act = () => registry.RegisterGraph(null!, graph);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Graph ID cannot be null or empty*");
    }

    [Fact]
    public void RegisterGraph_WithEmptyGraphId_ThrowsArgumentException()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();
        var graph = new TestGraphBuilder().Build();

        // Act & Assert
        Action act = () => registry.RegisterGraph("", graph);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Graph ID cannot be null or empty*");
    }

    [Fact]
    public void RegisterGraph_WithNullGraph_ThrowsArgumentNullException()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();

        // Act & Assert
        Action act = () => registry.RegisterGraph("test", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegisterGraph_WithDuplicateId_ThrowsArgumentException()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();
        var graph1 = new TestGraphBuilder().WithId("graph1").Build();
        var graph2 = new TestGraphBuilder().WithId("graph2").Build();

        registry.RegisterGraph("duplicate-id", graph1);

        // Act & Assert
        Action act = () => registry.RegisterGraph("duplicate-id", graph2);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Graph with ID 'duplicate-id' is already registered*");
    }

    #endregion

    #region Lookup Tests

    [Fact]
    public void GetGraph_WithExistingId_ReturnsGraph()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();
        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddEndNode()
            .Build();

        registry.RegisterGraph("test-graph", graph);

        // Act
        var retrieved = registry.GetGraph("test-graph");

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be("test-graph");
        retrieved.Should().BeSameAs(graph); // Same reference
    }

    [Fact]
    public void GetGraph_WithNonExistentId_ReturnsNull()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();

        // Act
        var retrieved = registry.GetGraph("non-existent");

        // Assert
        retrieved.Should().BeNull();
    }

    [Fact]
    public void GetGraph_WithNullId_ReturnsNull()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();

        // Act
        var retrieved = registry.GetGraph(null!);

        // Assert
        retrieved.Should().BeNull();
    }

    [Fact]
    public void GetGraph_WithEmptyId_ReturnsNull()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();

        // Act
        var retrieved = registry.GetGraph("");

        // Assert
        retrieved.Should().BeNull();
    }

    [Fact]
    public void ContainsGraph_WithExistingId_ReturnsTrue()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();
        var graph = new TestGraphBuilder().Build();
        registry.RegisterGraph("test", graph);

        // Act & Assert
        registry.ContainsGraph("test").Should().BeTrue();
    }

    [Fact]
    public void ContainsGraph_WithNonExistentId_ReturnsFalse()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();

        // Act & Assert
        registry.ContainsGraph("non-existent").Should().BeFalse();
    }

    #endregion

    #region Unregister Tests

    [Fact]
    public void UnregisterGraph_WithExistingId_RemovesGraphAndReturnsTrue()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();
        var graph = new TestGraphBuilder().Build();
        registry.RegisterGraph("test", graph);

        // Act
        var result = registry.UnregisterGraph("test");

        // Assert
        result.Should().BeTrue();
        registry.ContainsGraph("test").Should().BeFalse();
        registry.Count.Should().Be(0);
    }

    [Fact]
    public void UnregisterGraph_WithNonExistentId_ReturnsFalse()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();

        // Act
        var result = registry.UnregisterGraph("non-existent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void UnregisterGraph_WithNullId_ReturnsFalse()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();

        // Act
        var result = registry.UnregisterGraph(null!);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Multi-Graph Tests

    [Fact]
    public void RegisterGraph_MultipleGraphs_AllAccessible()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();

        var etlGraph = new TestGraphBuilder()
            .WithId("etl-pipeline")
            .AddStartNode()
            .AddNode(new Node { Id = "extract", Name = "Extract", Type = NodeType.Handler, HandlerName = "ExtractHandler" })
            .AddEndNode()
            .Build();

        var mlGraph = new TestGraphBuilder()
            .WithId("ml-pipeline")
            .AddStartNode()
            .AddNode(new Node { Id = "train", Name = "Train", Type = NodeType.Handler, HandlerName = "TrainHandler" })
            .AddEndNode()
            .Build();

        var reportGraph = new TestGraphBuilder()
            .WithId("report-pipeline")
            .AddStartNode()
            .AddNode(new Node { Id = "generate", Name = "Generate", Type = NodeType.Handler, HandlerName = "ReportHandler" })
            .AddEndNode()
            .Build();

        // Act
        registry.RegisterGraph("etl", etlGraph);
        registry.RegisterGraph("ml", mlGraph);
        registry.RegisterGraph("report", reportGraph);

        // Assert
        registry.Count.Should().Be(3);
        registry.GetGraph("etl")!.Id.Should().Be("etl-pipeline");
        registry.GetGraph("ml")!.Id.Should().Be("ml-pipeline");
        registry.GetGraph("report")!.Id.Should().Be("report-pipeline");
    }

    [Fact]
    public void GetGraphIds_WithMultipleGraphs_ReturnsAllIds()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();
        registry.RegisterGraph("graph1", new TestGraphBuilder().Build());
        registry.RegisterGraph("graph2", new TestGraphBuilder().Build());
        registry.RegisterGraph("graph3", new TestGraphBuilder().Build());

        // Act
        var ids = registry.GetGraphIds().ToList();

        // Assert
        ids.Should().HaveCount(3);
        ids.Should().Contain(new[] { "graph1", "graph2", "graph3" });
    }

    [Fact]
    public void GetGraphIds_WithEmptyRegistry_ReturnsEmptyCollection()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();

        // Act
        var ids = registry.GetGraphIds().ToList();

        // Assert
        ids.Should().BeEmpty();
    }

    #endregion

    #region Clear Tests

    [Fact]
    public void Clear_RemovesAllGraphs()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();
        registry.RegisterGraph("graph1", new TestGraphBuilder().Build());
        registry.RegisterGraph("graph2", new TestGraphBuilder().Build());
        registry.RegisterGraph("graph3", new TestGraphBuilder().Build());

        // Act
        registry.Clear();

        // Assert
        registry.Count.Should().Be(0);
        registry.GetGraphIds().Should().BeEmpty();
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public void RegisterGraph_ConcurrentRegistration_AllSucceed()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();
        var tasks = new List<Task>();

        // Act - Register 100 graphs concurrently
        for (int i = 0; i < 100; i++)
        {
            var graphId = $"graph-{i}";
            var graph = new TestGraphBuilder().WithId(graphId).Build();

            tasks.Add(Task.Run(() => registry.RegisterGraph(graphId, graph)));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        registry.Count.Should().Be(100);

        // Verify all graphs are accessible
        for (int i = 0; i < 100; i++)
        {
            registry.ContainsGraph($"graph-{i}").Should().BeTrue();
        }
    }

    [Fact]
    public void GetGraph_ConcurrentAccess_AllSucceed()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();
        var graph = new TestGraphBuilder().WithId("shared-graph").Build();
        registry.RegisterGraph("shared", graph);

        var tasks = new List<Task<HPDAgent.Graph.Abstractions.Graph.Graph?>>();

        // Act - Access same graph 100 times concurrently
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => registry.GetGraph("shared")));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert - All should return the same graph reference
        tasks.Select(t => t.Result).Should().OnlyContain(g => g != null && g.Id == "shared-graph");
    }

    [Fact]
    public void UnregisterGraph_ConcurrentUnregister_OnlyOneSucceeds()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();
        var graph = new TestGraphBuilder().Build();
        registry.RegisterGraph("target", graph);

        var tasks = new List<Task<bool>>();

        // Act - Try to unregister same graph 10 times concurrently
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() => registry.UnregisterGraph("target")));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert - Only one should succeed
        tasks.Select(t => t.Result).Count(success => success).Should().Be(1);
        tasks.Select(t => t.Result).Count(success => !success).Should().Be(9);
        registry.ContainsGraph("target").Should().BeFalse();
    }

    #endregion

    #region Integration Scenario Tests

    [Fact]
    public void Scenario_MultiTenantApplication_IsolatesGraphsByTenant()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();

        // Tenant A graphs
        registry.RegisterGraph("tenant-a:etl", new TestGraphBuilder().WithId("tenant-a-etl").Build());
        registry.RegisterGraph("tenant-a:ml", new TestGraphBuilder().WithId("tenant-a-ml").Build());

        // Tenant B graphs
        registry.RegisterGraph("tenant-b:etl", new TestGraphBuilder().WithId("tenant-b-etl").Build());
        registry.RegisterGraph("tenant-b:ml", new TestGraphBuilder().WithId("tenant-b-ml").Build());

        // Act & Assert
        registry.GetGraph("tenant-a:etl")!.Id.Should().Be("tenant-a-etl");
        registry.GetGraph("tenant-b:etl")!.Id.Should().Be("tenant-b-etl");

        registry.Count.Should().Be(4);
    }

    [Fact]
    public void Scenario_HotSwap_UpdateGraphAtRuntime()
    {
        // Arrange
        var registry = new InMemoryGraphRegistry();

        var v1Graph = new TestGraphBuilder()
            .WithId("pipeline-v1")
            .AddStartNode()
            .AddNode(new Node { Id = "old-logic", Name = "Old Logic", Type = NodeType.Handler })
            .AddEndNode()
            .Build();

        var v2Graph = new TestGraphBuilder()
            .WithId("pipeline-v2")
            .AddStartNode()
            .AddNode(new Node { Id = "new-logic", Name = "New Logic", Type = NodeType.Handler })
            .AddEndNode()
            .Build();

        registry.RegisterGraph("pipeline", v1Graph);

        // Act - Hot swap to v2
        registry.UnregisterGraph("pipeline");
        registry.RegisterGraph("pipeline", v2Graph);

        // Assert
        var retrieved = registry.GetGraph("pipeline");
        retrieved!.Id.Should().Be("pipeline-v2");
        retrieved.Nodes.Should().Contain(n => n.Id == "new-logic");
    }

    #endregion
}
