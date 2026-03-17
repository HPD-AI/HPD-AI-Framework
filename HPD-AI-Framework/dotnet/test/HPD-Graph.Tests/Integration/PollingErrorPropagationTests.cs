using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Core.Builders;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Graph.Tests.Integration;

/// <summary>
/// Integration tests for error propagation with polling nodes and upstream conditions.
/// Tests how polling timeouts and failures interact with:
/// - Upstream condition evaluation (UpstreamOneSuccess, UpstreamAllDone, UpstreamPartialSuccess)
/// - Error isolation policies
/// - Nested subgraphs
/// </summary>
public class PollingErrorPropagationTests
{
    [Fact]
    public async Task PollingTimeout_WithUpstreamOneSuccess_SubGraphExecutesWhenOneSucceeds()
    {
        // Arrange - One polling node times out, another succeeds
        var successPoller = new SinglePollHandler();
        var timeoutPoller = new TimeoutPollHandler();

        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(successPoller);
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(timeoutPoller);
        });

        var subGraph = new TestGraphBuilder()
            .AddStartNode("sub_start")
            .AddHandlerNode("sub_handler", "SuccessHandler")
            .AddEndNode("sub_end")
            .AddEdge("sub_start", "sub_handler")
            .AddEdge("sub_handler", "sub_end")
            .Build();

        var graph = new GraphBuilder()
            .WithName("TestGraph")
            .AddNode("start", "Start", NodeType.Start)
            .AddNode("upstream_success", "Success Poller", NodeType.Handler, "SinglePollHandler")
            .AddNode("upstream_timeout", "Timeout Poller", NodeType.Handler, "TimeoutPollHandler",
                n => n.WithErrorPolicy(ErrorPropagationPolicy.Isolate())) // Isolate timeout
            .AddSubGraphNode("subgraph", "SubGraph", subGraph)
            .AddNode("end", "End", NodeType.End)
            .AddEdge("start", "upstream_success")
            .AddEdge("start", "upstream_timeout")
            .AddEdge("upstream_success", "subgraph")
            .AddEdge("upstream_timeout", "subgraph")
            .RequireOneSuccess("subgraph") // Requires at least one upstream success
            .AddEdge("subgraph", "end")
            .Build();

        var context = new GraphContext("test-exec", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Graph should complete successfully
        context.IsComplete.Should().BeTrue();

        // Success poller completed
        context.IsNodeComplete("upstream_success").Should().BeTrue();
        var successResult = context.Channels["node_result:upstream_success"].Get<NodeExecutionResult>();
        successResult.Should().BeOfType<NodeExecutionResult.Success>();

        // Timeout poller failed (isolated)
        context.IsNodeComplete("upstream_timeout").Should().BeTrue();
        var timeoutResult = context.Channels["node_result:upstream_timeout"].Get<NodeExecutionResult>();
        timeoutResult.Should().BeOfType<NodeExecutionResult.Failure>();

        // SubGraph should have executed (one success satisfies condition)
        context.IsNodeComplete("subgraph").Should().BeTrue();
    }

    [Fact]
    public async Task AllPollingNodesTimeout_WithUpstreamOneSuccess_SubGraphSkipped()
    {
        // Arrange - All polling nodes timeout
        var timeoutPoller1 = new TimeoutPollHandler();
        var timeoutPoller2 = new TimeoutPollHandler();

        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(timeoutPoller1);
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(timeoutPoller2);
        });

        var subGraph = new TestGraphBuilder()
            .AddStartNode("sub_start")
            .AddHandlerNode("sub_handler", "SuccessHandler")
            .AddEndNode("sub_end")
            .AddEdge("sub_start", "sub_handler")
            .AddEdge("sub_handler", "sub_end")
            .Build();

        var graph = new GraphBuilder()
            .WithName("TestGraph")
            .AddNode("start", "Start", NodeType.Start)
            .AddNode("upstream1", "Timeout Poller 1", NodeType.Handler, "TimeoutPollHandler",
                n => n.WithErrorPolicy(ErrorPropagationPolicy.Isolate()))
            .AddNode("upstream2", "Timeout Poller 2", NodeType.Handler, "TimeoutPollHandler",
                n => n.WithErrorPolicy(ErrorPropagationPolicy.Isolate()))
            .AddSubGraphNode("subgraph", "SubGraph", subGraph)
            .AddNode("end", "End", NodeType.End)
            .AddEdge("start", "upstream1")
            .AddEdge("start", "upstream2")
            .AddEdge("upstream1", "subgraph")
            .AddEdge("upstream2", "subgraph")
            .RequireOneSuccess("subgraph") // Requires at least one success - won't be met
            .AddEdge("subgraph", "end")
            .Build();

        var context = new GraphContext("test-exec", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Graph should complete
        context.IsComplete.Should().BeTrue();

        // Both upstreams failed
        context.IsNodeComplete("upstream1").Should().BeTrue();
        context.IsNodeComplete("upstream2").Should().BeTrue();

        var result1 = context.Channels["node_result:upstream1"].Get<NodeExecutionResult>();
        var result2 = context.Channels["node_result:upstream2"].Get<NodeExecutionResult>();
        result1.Should().BeOfType<NodeExecutionResult.Failure>();
        result2.Should().BeOfType<NodeExecutionResult.Failure>();

        // SubGraph should be skipped (no successful upstreams)
        context.IsNodeComplete("subgraph").Should().BeTrue();
        var subgraphResult = context.Channels["node_result:subgraph"].Get<NodeExecutionResult>();
        subgraphResult.Should().BeOfType<NodeExecutionResult.Skipped>();
    }

    [Fact]
    public async Task PollingTimeout_WithUpstreamAllDone_SubGraphWaitsForAll()
    {
        // Arrange - Mixed success and timeout, UpstreamAllDone should wait for both
        var successPoller = new SinglePollHandler();
        var timeoutPoller = new TimeoutPollHandler();

        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(successPoller);
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(timeoutPoller);
        });

        var subGraph = new TestGraphBuilder()
            .AddStartNode("sub_start")
            .AddHandlerNode("sub_handler", "SuccessHandler")
            .AddEndNode("sub_end")
            .AddEdge("sub_start", "sub_handler")
            .AddEdge("sub_handler", "sub_end")
            .Build();

        var graph = new GraphBuilder()
            .WithName("TestGraph")
            .AddNode("start", "Start", NodeType.Start)
            .AddNode("upstream_success", "Success Poller", NodeType.Handler, "SinglePollHandler")
            .AddNode("upstream_timeout", "Timeout Poller", NodeType.Handler, "TimeoutPollHandler",
                n => n.WithErrorPolicy(ErrorPropagationPolicy.Isolate()))
            .AddSubGraphNode("subgraph", "SubGraph", subGraph)
            .AddNode("end", "End", NodeType.End)
            .AddEdge("start", "upstream_success")
            .AddEdge("start", "upstream_timeout")
            .AddEdge("upstream_success", "subgraph")
            .AddEdge("upstream_timeout", "subgraph")
            .RequireAllDone("subgraph") // Wait for ALL upstreams to complete
            .AddEdge("subgraph", "end")
            .Build();

        var context = new GraphContext("test-exec", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Graph should complete
        context.IsComplete.Should().BeTrue();

        // Both upstreams should have completed (one success, one timeout)
        context.IsNodeComplete("upstream_success").Should().BeTrue();
        context.IsNodeComplete("upstream_timeout").Should().BeTrue();

        // SubGraph should have executed (all done, regardless of success/failure)
        context.IsNodeComplete("subgraph").Should().BeTrue();
        var subgraphResult = context.Channels["node_result:subgraph"].Get<NodeExecutionResult>();
        subgraphResult.Should().BeOfType<NodeExecutionResult.Success>();
    }

    [Fact]
    public async Task PollingTimeout_WithUpstreamPartialSuccess_SubGraphExecutesWhenMet()
    {
        // Arrange - UpstreamPartialSuccess requires all done + at least one success
        var successPoller = new SinglePollHandler();
        var timeoutPoller = new TimeoutPollHandler();

        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(successPoller);
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(timeoutPoller);
        });

        var subGraph = new TestGraphBuilder()
            .AddStartNode("sub_start")
            .AddHandlerNode("sub_handler", "SuccessHandler")
            .AddEndNode("sub_end")
            .AddEdge("sub_start", "sub_handler")
            .AddEdge("sub_handler", "sub_end")
            .Build();

        var graph = new GraphBuilder()
            .WithName("TestGraph")
            .AddNode("start", "Start", NodeType.Start)
            .AddNode("upstream_success", "Success Poller", NodeType.Handler, "SinglePollHandler")
            .AddNode("upstream_timeout", "Timeout Poller", NodeType.Handler, "TimeoutPollHandler",
                n => n.WithErrorPolicy(ErrorPropagationPolicy.Isolate()))
            .AddSubGraphNode("subgraph", "SubGraph", subGraph)
            .AddNode("end", "End", NodeType.End)
            .AddEdge("start", "upstream_success")
            .AddEdge("start", "upstream_timeout")
            .AddEdge("upstream_success", "subgraph")
            .AddEdge("upstream_timeout", "subgraph")
            .RequirePartialSuccess("subgraph") // All done + at least one success
            .AddEdge("subgraph", "end")
            .Build();

        var context = new GraphContext("test-exec", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Graph should complete
        context.IsComplete.Should().BeTrue();

        // Both upstreams completed
        context.IsNodeComplete("upstream_success").Should().BeTrue();
        context.IsNodeComplete("upstream_timeout").Should().BeTrue();

        // SubGraph should execute (all done + one success)
        context.IsNodeComplete("subgraph").Should().BeTrue();
        var subgraphResult = context.Channels["node_result:subgraph"].Get<NodeExecutionResult>();
        subgraphResult.Should().BeOfType<NodeExecutionResult.Success>();
    }

    [Fact]
    public async Task PollingTimeout_WithoutErrorIsolation_PropagatesFailure()
    {
        // Arrange - Polling timeout without error isolation should propagate
        var timeoutPoller = new TimeoutPollHandler();
        var services = TestServiceProvider.CreateWithHandler(timeoutPoller);

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("poller", "TimeoutPollHandler") // No error isolation
            .AddHandlerNode("downstream", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "poller")
            .AddEdge("poller", "downstream")
            .AddEdge("downstream", "end")
            .Build();

        var context = new GraphContext("test-exec", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act & Assert - Should throw GraphExecutionException with TimeoutException as inner
        var ex = await Assert.ThrowsAsync<GraphExecutionException>(async () =>
            await orchestrator.ExecuteAsync(context));

        // Verify inner exception is TimeoutException
        ex.InnerException.Should().BeOfType<TimeoutException>();

        // Poller failed with timeout
        var pollerResult = context.Channels["node_result:poller"].Get<NodeExecutionResult>();
        pollerResult.Should().BeOfType<NodeExecutionResult.Failure>();
        var failure = (NodeExecutionResult.Failure)pollerResult;
        failure.Exception.Should().BeOfType<TimeoutException>();

        // Downstream should not have executed
        context.IsNodeComplete("downstream").Should().BeFalse();
    }

    /// <summary>
    /// Handler that polls once then succeeds.
    /// </summary>
    private class SinglePollHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "SinglePollHandler";
        private int _callCount = 0;

        public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
        {
            _callCount++;

            if (_callCount == 1)
            {
                return Task.FromResult<NodeExecutionResult>(
                    NodeExecutionResult.Suspended.ForPolling(
                        suspendToken: "poll-once",
                        retryAfter: TimeSpan.FromMilliseconds(50),
                        maxWaitTime: TimeSpan.FromSeconds(10),
                        message: "Polling once"
                    )
                );
            }

            return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
                output: new Dictionary<string, object> { ["result"] = "success" },
                duration: TimeSpan.FromMilliseconds(10),
                metadata: new NodeExecutionMetadata()
            ));
        }
    }

    /// <summary>
    /// Handler that always polls and times out.
    /// </summary>
    private class TimeoutPollHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "TimeoutPollHandler";

        public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Suspended.ForPolling(
                    suspendToken: "timeout-poll",
                    retryAfter: TimeSpan.FromMilliseconds(50),
                    maxWaitTime: TimeSpan.FromMilliseconds(200), // Short timeout
                    message: "Polling until timeout"
                )
            );
        }
    }
}
