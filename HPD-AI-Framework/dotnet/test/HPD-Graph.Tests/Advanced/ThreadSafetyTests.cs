using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Xunit;

namespace HPD.Graph.Tests.Advanced;

/// <summary>
/// Tests for thread safety in concurrent graph execution.
/// </summary>
public class ThreadSafetyTests
{
    [Fact]
    public async Task Execute_ParallelNodes_ShouldHandleConcurrentContextAccess()
    {
        // Arrange - Graph with parallel branches
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("parallel1", "SuccessHandler")
            .AddHandlerNode("parallel2", "SuccessHandler")
            .AddHandlerNode("parallel3", "SuccessHandler")
            .AddHandlerNode("merge", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "parallel1")
            .AddEdge("start", "parallel2")
            .AddEdge("start", "parallel3")
            .AddEdge("parallel1", "merge")
            .AddEdge("parallel2", "merge")
            .AddEdge("parallel3", "merge")
            .AddEdge("merge", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act - Execute with parallel nodes
        await orchestrator.ExecuteAsync(context);

        // Assert - All nodes should be marked complete
        context.ShouldHaveCompletedNode("parallel1");
        context.ShouldHaveCompletedNode("parallel2");
        context.ShouldHaveCompletedNode("parallel3");
        context.ShouldHaveCompletedNode("merge");
    }

    [Fact]
    public async Task Execute_ConcurrentMarkComplete_ShouldNotLoseData()
    {
        // Arrange - Wide parallel graph to stress test
        var builder = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("merge", "SuccessHandler")
            .AddEndNode();

        // Add 10 parallel branches
        for (int i = 0; i < 10; i++)
        {
            var nodeId = $"parallel_{i}";
            builder.AddHandlerNode(nodeId, "SuccessHandler");
            builder.AddEdge("start", nodeId);
            builder.AddEdge(nodeId, "merge");
        }

        builder.AddEdge("merge", "end");
        var graph = builder.Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - All 10 parallel nodes should be tracked
        for (int i = 0; i < 10; i++)
        {
            context.ShouldHaveCompletedNode($"parallel_{i}");
        }
        context.ShouldHaveCompletedNode("merge");
    }

    [Fact]
    public async Task Execute_ConcurrentLogging_ShouldCaptureAllLogs()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("node1", "SuccessHandler")
            .AddHandlerNode("node2", "SuccessHandler")
            .AddHandlerNode("node3", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "node1")
            .AddEdge("start", "node2")
            .AddEdge("start", "node3")
            .AddEdge("node1", "end")
            .AddEdge("node2", "end")
            .AddEdge("node3", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Should have logs from all nodes
        context.LogEntries.Should().NotBeEmpty();
        // At minimum we should have logs for each node execution
        context.LogEntries.Count.Should().BeGreaterThan(2);
    }

    [Fact]
    public async Task Execute_ConcurrentTagging_ShouldStoreAllTags()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("tagger1", "SuccessHandler")
            .AddHandlerNode("tagger2", "SuccessHandler")
            .AddHandlerNode("tagger3", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "tagger1")
            .AddEdge("start", "tagger2")
            .AddEdge("start", "tagger3")
            .AddEdge("tagger1", "end")
            .AddEdge("tagger2", "end")
            .AddEdge("tagger3", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        // Add tags concurrently during setup
        var tasks = new[]
        {
            Task.Run(() => context.AddTag("test", "value1")),
            Task.Run(() => context.AddTag("test", "value2")),
            Task.Run(() => context.AddTag("test", "value3"))
        };

        await Task.WhenAll(tasks);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - All tags should be preserved
        context.Tags.Should().ContainKey("test");
        context.Tags["test"].Should().HaveCount(3);
    }

    [Fact]
    public async Task Execute_ConcurrentExecutionCounts_ShouldTrackAccurately()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("counted1", "SuccessHandler")
            .AddHandlerNode("counted2", "SuccessHandler")
            .AddHandlerNode("counted3", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "counted1")
            .AddEdge("start", "counted2")
            .AddEdge("start", "counted3")
            .AddEdge("counted1", "end")
            .AddEdge("counted2", "end")
            .AddEdge("counted3", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Each node should have exactly 1 execution
        context.GetNodeExecutionCount("counted1").Should().Be(1);
        context.GetNodeExecutionCount("counted2").Should().Be(1);
        context.GetNodeExecutionCount("counted3").Should().Be(1);
    }

    [Fact]
    public async Task Execute_MultipleGraphsInParallel_ShouldIsolateContexts()
    {
        // Arrange - Create 5 identical graphs
        var graphs = Enumerable.Range(0, 5).Select(_ =>
            new TestGraphBuilder()
                .AddStartNode()
                .AddHandlerNode("handler", "SuccessHandler")
                .AddEndNode()
                .AddEdge("start", "handler")
                .AddEdge("handler", "end")
                .Build()
        ).ToArray();

        var services = TestServiceProvider.Create();

        // Act - Execute all graphs in parallel
        var tasks = graphs.Select((graph, index) =>
        {
            var context = new GraphContext($"exec_{index}", graph, services);
            var orchestrator = new GraphOrchestrator<GraphContext>(services);
            return orchestrator.ExecuteAsync(context);
        }).ToArray();

        await Task.WhenAll(tasks);

        // Assert - All should complete without interference
        tasks.Should().AllSatisfy(t => t.IsCompletedSuccessfully.Should().BeTrue());
    }
}
