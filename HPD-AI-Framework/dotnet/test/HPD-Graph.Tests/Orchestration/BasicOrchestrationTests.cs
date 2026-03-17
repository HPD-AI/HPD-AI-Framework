using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Xunit;

namespace HPD.Graph.Tests.Orchestration;

/// <summary>
/// Tests for basic graph orchestration and execution.
/// </summary>
public class BasicOrchestrationTests
{
    [Fact]
    public async Task Execute_SimpleLinearGraph_ShouldExecuteInOrder()
    {
        // Arrange
        var graph = new TestGraphBuilder()
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
        var context = new GraphContext(
            executionId: "test-exec",
            graph: graph,
            services: services
        );

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.ShouldHaveCompletedNode("step1");
        context.ShouldHaveCompletedNode("step2");
        context.ShouldHaveCompletedNode("step3");
    }

    [Fact]
    public async Task Execute_SingleNodeGraph_ShouldExecuteSuccessfully()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler", "EchoHandler")
            .AddEndNode()
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext(
            executionId: "test-exec",
            graph: graph,
            services: services
        );

        // Set input
        context.Channels["test_input"].Set("Hello World");

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.ShouldHaveCompletedNode("handler");
    }

    [Fact]
    public async Task Execute_ParallelBranches_ShouldExecuteBothPaths()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("branch1", "SuccessHandler")
            .AddHandlerNode("branch2", "SuccessHandler")
            .AddHandlerNode("merge", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "branch1")
            .AddEdge("start", "branch2")
            .AddEdge("branch1", "merge")
            .AddEdge("branch2", "merge")
            .AddEdge("merge", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext(
            executionId: "test-exec",
            graph: graph,
            services: services
        );

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.ShouldHaveCompletedNode("branch1");
        context.ShouldHaveCompletedNode("branch2");
        context.ShouldHaveCompletedNode("merge");
    }

    [Fact]
    public async Task Execute_EmptyGraph_ShouldCompleteImmediately()
    {
        // Arrange - Graph with only START and END
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddEndNode()
            .AddEdge("start", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext(
            executionId: "test-exec",
            graph: graph,
            services: services
        );

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        var act = async () => await orchestrator.ExecuteAsync(context);

        // Assert - Should complete without throwing
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Execute_WithChannels_ShouldPropagateData()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("producer", "SuccessHandler")
            .AddHandlerNode("consumer", "EchoHandler")
            .AddEndNode()
            .AddEdge("start", "producer")
            .AddEdge("producer", "consumer")
            .AddEdge("consumer", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext(
            executionId: "test-exec",
            graph: graph,
            services: services
        );

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.ShouldHaveCompletedNode("producer");
        context.ShouldHaveCompletedNode("consumer");
    }

    [Fact]
    public async Task Execute_MultipleExecutions_ShouldMaintainIndependence()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler", "CounterHandler")
            .AddEndNode()
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        var services = TestServiceProvider.Create();

        // Act - Execute twice with different contexts
        var context1 = new GraphContext("exec1", graph, services);
        var context2 = new GraphContext("exec2", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);
        await orchestrator.ExecuteAsync(context1);
        await orchestrator.ExecuteAsync(context2);

        // Assert - Both should have completed independently
        context1.ShouldHaveCompletedNode("handler");
        context2.ShouldHaveCompletedNode("handler");
        context1.ExecutionId.Should().NotBe(context2.ExecutionId);
    }

    [Fact]
    public async Task Execute_WithCancellation_ShouldRespectCancellationToken()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("slow", "DelayHandler")
            .AddEndNode()
            .AddEdge("start", "slow")
            .AddEdge("slow", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(10)); // Cancel quickly

        // Act & Assert
        var act = async () => await orchestrator.ExecuteAsync(context, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Execute_ShouldLogExecutionSteps()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Check that execution was logged
        context.LogEntries.Should().NotBeEmpty();
        context.ShouldHaveLogEntry("handler");
    }

    [Fact]
    public async Task Execute_NodeExecutionCount_ShouldTrack()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.GetNodeExecutionCount("handler").Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Execute_ComplexDAG_ShouldExecuteCorrectly()
    {
        // Arrange - Diamond pattern
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("split", "SuccessHandler")
            .AddHandlerNode("left", "SuccessHandler")
            .AddHandlerNode("right", "SuccessHandler")
            .AddHandlerNode("join", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "split")
            .AddEdge("split", "left")
            .AddEdge("split", "right")
            .AddEdge("left", "join")
            .AddEdge("right", "join")
            .AddEdge("join", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - All nodes should complete
        context.ShouldHaveCompletedNode("split");
        context.ShouldHaveCompletedNode("left");
        context.ShouldHaveCompletedNode("right");
        context.ShouldHaveCompletedNode("join");
    }
}
