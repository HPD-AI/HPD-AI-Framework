using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Xunit;

namespace HPD.Graph.Tests.Advanced;

/// <summary>
/// Tests for SubGraph node execution and composition.
/// </summary>
public class SubGraphExecutionTests
{
    [Fact]
    public async Task Execute_SimpleSubGraph_ShouldExecuteRecursively()
    {
        // Arrange - Create inner subgraph
        var subGraph = new TestGraphBuilder()
            .WithId("sub")
            .WithName("SubGraph")
            .AddStartNode("sub_start")
            .AddHandlerNode("sub_handler", "SuccessHandler")
            .AddEndNode("sub_end")
            .AddEdge("sub_start", "sub_handler")
            .AddEdge("sub_handler", "sub_end")
            .Build();

        // Create outer graph with subgraph node
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddSubGraphNode("subgraph_node", subGraph)
            .AddEndNode()
            .AddEdge("start", "subgraph_node")
            .AddEdge("subgraph_node", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - SubGraph node should be marked complete
        context.ShouldHaveCompletedNode("subgraph_node");
    }

    [Fact]
    public async Task Execute_NestedSubGraphs_ShouldExecuteAllLevels()
    {
        // Arrange - Level 2: Innermost graph
        var level2 = new TestGraphBuilder()
            .WithId("level2")
            .WithName("Level2")
            .AddStartNode("l2_start")
            .AddHandlerNode("l2_handler", "SuccessHandler")
            .AddEndNode("l2_end")
            .AddEdge("l2_start", "l2_handler")
            .AddEdge("l2_handler", "l2_end")
            .Build();

        // Level 1: Middle graph containing level 2
        var level1 = new TestGraphBuilder()
            .WithId("level1")
            .WithName("Level1")
            .AddStartNode("l1_start")
            .AddSubGraphNode("l1_sub", level2)
            .AddEndNode("l1_end")
            .AddEdge("l1_start", "l1_sub")
            .AddEdge("l1_sub", "l1_end")
            .Build();

        // Level 0: Outer graph containing level 1
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddSubGraphNode("l0_sub", level1)
            .AddEndNode()
            .AddEdge("start", "l0_sub")
            .AddEdge("l0_sub", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act & Assert - Should complete without errors (nested execution works)
        var act = async () => await orchestrator.ExecuteAsync(context);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Execute_SubGraphWithMultipleNodes_ShouldExecuteAll()
    {
        // Arrange - Complex subgraph
        var subGraph = new TestGraphBuilder()
            .WithId("sub")
            .WithName("ComplexSub")
            .AddStartNode("sub_start")
            .AddHandlerNode("sub_step1", "SuccessHandler")
            .AddHandlerNode("sub_step2", "SuccessHandler")
            .AddHandlerNode("sub_step3", "SuccessHandler")
            .AddEndNode("sub_end")
            .AddEdge("sub_start", "sub_step1")
            .AddEdge("sub_step1", "sub_step2")
            .AddEdge("sub_step2", "sub_step3")
            .AddEdge("sub_step3", "sub_end")
            .Build();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddSubGraphNode("subgraph", subGraph)
            .AddEndNode()
            .AddEdge("start", "subgraph")
            .AddEdge("subgraph", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.ShouldHaveCompletedNode("subgraph");
    }

    [Fact]
    public async Task Execute_SubGraphFollowedByHandler_ShouldExecuteInOrder()
    {
        // Arrange
        var subGraph = new TestGraphBuilder()
            .WithId("sub")
            .WithName("Sub")
            .AddStartNode("sub_start")
            .AddHandlerNode("sub_handler", "SuccessHandler")
            .AddEndNode("sub_end")
            .AddEdge("sub_start", "sub_handler")
            .AddEdge("sub_handler", "sub_end")
            .Build();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddSubGraphNode("subgraph", subGraph)
            .AddHandlerNode("after_sub", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "subgraph")
            .AddEdge("subgraph", "after_sub")
            .AddEdge("after_sub", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.ShouldHaveCompletedNode("subgraph");
        context.ShouldHaveCompletedNode("after_sub");
    }

    [Fact]
    public async Task Execute_MultipleSubGraphsInParallel_ShouldExecuteBoth()
    {
        // Arrange
        var subGraph1 = new TestGraphBuilder()
            .WithId("sub1")
            .WithName("Sub1")
            .AddStartNode("s1_start")
            .AddHandlerNode("s1_handler", "SuccessHandler")
            .AddEndNode("s1_end")
            .AddEdge("s1_start", "s1_handler")
            .AddEdge("s1_handler", "s1_end")
            .Build();

        var subGraph2 = new TestGraphBuilder()
            .WithId("sub2")
            .WithName("Sub2")
            .AddStartNode("s2_start")
            .AddHandlerNode("s2_handler", "SuccessHandler")
            .AddEndNode("s2_end")
            .AddEdge("s2_start", "s2_handler")
            .AddEdge("s2_handler", "s2_end")
            .Build();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddSubGraphNode("subgraph1", subGraph1)
            .AddSubGraphNode("subgraph2", subGraph2)
            .AddHandlerNode("merge", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "subgraph1")
            .AddEdge("start", "subgraph2")
            .AddEdge("subgraph1", "merge")
            .AddEdge("subgraph2", "merge")
            .AddEdge("merge", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.ShouldHaveCompletedNode("subgraph1");
        context.ShouldHaveCompletedNode("subgraph2");
        context.ShouldHaveCompletedNode("merge");
    }
}
