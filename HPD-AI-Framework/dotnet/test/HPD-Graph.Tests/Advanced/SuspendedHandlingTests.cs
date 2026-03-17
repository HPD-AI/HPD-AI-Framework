using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Xunit;

namespace HPD.Graph.Tests.Advanced;

/// <summary>
/// Tests for suspended execution and resume functionality.
/// </summary>
public class SuspendedHandlingTests
{
    [Fact]
    public async Task Execute_SuspendingHandler_ShouldThrowGraphSuspendedException()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("suspender", "SuspendingHandler")
            .AddEndNode()
            .AddEdge("start", "suspender")
            .AddEdge("suspender", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act & Assert
        var act = async () => await orchestrator.ExecuteAsync(context);
        await act.Should().ThrowAsync<GraphSuspendedException>()
            .Where(ex => ex.NodeId == "suspender");
    }

    [Fact]
    public async Task Execute_Suspended_ShouldStoreSuspendToken()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("suspender", "SuspendingHandler")
            .AddEndNode()
            .AddEdge("start", "suspender")
            .AddEdge("suspender", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        try
        {
            await orchestrator.ExecuteAsync(context);
        }
        catch (GraphSuspendedException ex)
        {
            // Assert
            ex.NodeId.Should().Be("suspender");
            ex.SuspendToken.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task Execute_Suspended_ShouldAddSuspendedNodeTag()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("suspender", "SuspendingHandler")
            .AddEndNode()
            .AddEdge("start", "suspender")
            .AddEdge("suspender", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        try
        {
            await orchestrator.ExecuteAsync(context);
        }
        catch (GraphSuspendedException)
        {
            // Assert
            var tags = context.Tags["suspended_nodes"];
            tags.Should().Contain("suspender");
        }
    }

    [Fact]
    public async Task Execute_SuspendedBeforeCompletion_ShouldNotMarkNodeComplete()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("suspender", "SuspendingHandler")
            .AddEndNode()
            .AddEdge("start", "suspender")
            .AddEdge("suspender", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        try
        {
            await orchestrator.ExecuteAsync(context);
        }
        catch (GraphSuspendedException)
        {
            // Expected - ignore
        }

        // Assert - Node should NOT be marked complete since it suspended
        context.ShouldNotHaveCompletedNode("suspender");
    }

    [Fact]
    public async Task Execute_SuspendInMiddleOfGraph_ShouldStopExecution()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("before", "SuccessHandler")
            .AddHandlerNode("suspender", "SuspendingHandler")
            .AddHandlerNode("after", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "before")
            .AddEdge("before", "suspender")
            .AddEdge("suspender", "after")
            .AddEdge("after", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        try
        {
            await orchestrator.ExecuteAsync(context);
        }
        catch (GraphSuspendedException)
        {
            // Expected
        }

        // Assert
        context.ShouldHaveCompletedNode("before"); // Before suspend completed
        context.ShouldNotHaveCompletedNode("suspender"); // Suspended node not complete
        context.ShouldNotHaveCompletedNode("after"); // After suspend not executed
    }
}
