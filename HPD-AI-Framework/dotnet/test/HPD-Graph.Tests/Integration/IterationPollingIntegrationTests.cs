using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPD.Graph.Tests.Iteration;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Graph.Tests.Integration;

/// <summary>
/// Integration tests for iteration synchronization with polling nodes.
/// Tests specification from Section 3.3.1 of HPD_GRAPH_WORKFLOW_PRIMITIVES_V5.md:
/// - Wait for polling nodes to resolve before evaluating back-edges
/// - Prevent premature convergence while nodes are polling
/// - Intelligent delay based on earliest retry time
/// </summary>
public class IterationPollingIntegrationTests
{
    [Fact]
    public async Task SimplePollingNode_CompletesAfterPolling()
    {
        // Arrange - Single polling node
        var handler = new SinglePollHandler();
        var services = TestServiceProvider.CreateWithHandler(handler);

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("poller", "SinglePollHandler")
            .AddEndNode()
            .AddEdge("start", "poller")
            .AddEdge("poller", "end")
            .Build();

        var context = new GraphContext("test-exec", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Should complete successfully after polling
        context.IsComplete.Should().BeTrue();
        context.IsNodeComplete("poller").Should().BeTrue();

        var pollerResult = context.Channels["node_result:poller"].Get<NodeExecutionResult>();
        pollerResult.Should().BeOfType<NodeExecutionResult.Success>();
    }

    [Fact]
    public async Task MultiplePollingNodes_AllCompleteAfterPolling()
    {
        // Arrange - Multiple polling nodes in parallel
        var handler1 = new SinglePollHandler();
        var handler2 = new SinglePollHandler();

        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(handler1);
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(handler2);
        });

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("poller1", "SinglePollHandler")
            .AddHandlerNode("poller2", "SinglePollHandler")
            .AddHandlerNode("aggregator", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "poller1")
            .AddEdge("start", "poller2")
            .AddEdge("poller1", "aggregator")
            .AddEdge("poller2", "aggregator")
            .AddEdge("aggregator", "end")
            .Build();

        var context = new GraphContext("test-exec", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - All nodes should complete
        context.IsComplete.Should().BeTrue();
        context.IsNodeComplete("poller1").Should().BeTrue();
        context.IsNodeComplete("poller2").Should().BeTrue();
        context.IsNodeComplete("aggregator").Should().BeTrue();
    }

    [Fact]
    public async Task PollingWithTimeout_CreatesFailureResult()
    {
        // Arrange - Polling node that times out
        var handler = new TimeoutPollHandler();
        var services = TestServiceProvider.CreateWithHandler(handler);

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("poller", "TimeoutPollHandler")
            .AddEndNode()
            .AddEdge("start", "poller")
            .AddEdge("poller", "end")
            .Build();

        var context = new GraphContext("test-exec", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act & Assert - Should timeout and throw GraphExecutionException
        var ex = await Assert.ThrowsAsync<GraphExecutionException>(async () =>
            await orchestrator.ExecuteAsync(context));

        // Verify inner exception is TimeoutException
        ex.InnerException.Should().BeOfType<TimeoutException>();

        // Verify Failure result was created
        var pollerResult = context.Channels["node_result:poller"].Get<NodeExecutionResult>();
        pollerResult.Should().BeOfType<NodeExecutionResult.Failure>();
        var failure = (NodeExecutionResult.Failure)pollerResult;
        failure.Exception.Should().BeOfType<TimeoutException>();
    }

    [Fact]
    public async Task IterationWithPolling_WaitsForPollingBeforeBackEdge()
    {
        // Arrange - Iterative graph where handler polls then decides whether to retry
        var handler = new RetryWithPollingHandler(retriesBeforeSuccess: 2);
        var services = TestServiceProvider.CreateWithHandler(handler);

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("processor", "RetryWithPollingHandler")
            .AddEndNode()
            .AddEdge("start", "processor")
            .AddEdge("processor", "processor", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "retry",
                Value = true
            })
            .AddEdge("processor", "end", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "retry",
                Value = false
            })
            .Build();

        var context = new GraphContext("test-exec", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Should complete after retries
        context.IsComplete.Should().BeTrue();
        context.IsNodeComplete("processor").Should().BeTrue();

        // Should have executed 3 times (2 retries + 1 success)
        context.GetNodeExecutionCount("processor").Should().Be(3);
    }

    [Fact]
    public async Task NonPollingIteration_CompletesNormally()
    {
        // Arrange - Simple iterative graph without polling (baseline test)
        var handler = new RetryHandler(retriesBeforeSuccess: 2);
        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(handler);
        });

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("processor", "RetryHandler")
            .AddEndNode()
            .AddEdge("start", "processor")
            .AddEdge("processor", "processor", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "retry",
                Value = true
            })
            .AddEdge("processor", "end", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "retry",
                Value = false
            })
            .Build();

        var context = new GraphContext("test-exec", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Should complete normally
        context.IsComplete.Should().BeTrue();
        context.IsNodeComplete("processor").Should().BeTrue();
        context.GetNodeExecutionCount("processor").Should().Be(3);
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
                // First call - return Suspended for polling
                return Task.FromResult<NodeExecutionResult>(
                    NodeExecutionResult.Suspended.ForPolling(
                        suspendToken: "poll-once",
                        retryAfter: TimeSpan.FromMilliseconds(50),
                        maxWaitTime: TimeSpan.FromSeconds(10),
                        message: "Polling once"
                    )
                );
            }

            // Second call after polling - return Success
            return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
                output: new Dictionary<string, object> { ["result"] = "success" },
                duration: TimeSpan.FromMilliseconds(10),
                metadata: new NodeExecutionMetadata()
            ));
        }
    }

    /// <summary>
    /// Handler that always polls (never succeeds) to test timeout.
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
                    maxWaitTime: TimeSpan.FromMilliseconds(200), // Short timeout for test
                    message: "Infinite polling"
                )
            );
        }
    }

    /// <summary>
    /// Handler that polls once per execution, then decides whether to retry.
    /// Combines polling pattern with iteration pattern.
    /// </summary>
    private class RetryWithPollingHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "RetryWithPollingHandler";
        private int _executionCount = 0;
        private int _callCount = 0;
        private readonly int _retriesBeforeSuccess;

        public RetryWithPollingHandler(int retriesBeforeSuccess = 2)
        {
            _retriesBeforeSuccess = retriesBeforeSuccess;
        }

        public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
        {
            _callCount++;

            // On odd calls (1, 3, 5...), poll
            if (_callCount % 2 == 1)
            {
                return Task.FromResult<NodeExecutionResult>(
                    NodeExecutionResult.Suspended.ForPolling(
                        suspendToken: $"retry-poll-{_executionCount}",
                        retryAfter: TimeSpan.FromMilliseconds(50),
                        maxWaitTime: TimeSpan.FromSeconds(10),
                        message: $"Polling for execution {_executionCount}"
                    )
                );
            }

            // On even calls (2, 4, 6...), complete the execution
            _executionCount++;
            var needsRetry = _executionCount <= _retriesBeforeSuccess;

            return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
                output: new Dictionary<string, object>
                {
                    ["retry"] = needsRetry,
                    ["execution"] = _executionCount
                },
                duration: TimeSpan.FromMilliseconds(10),
                metadata: new NodeExecutionMetadata()
            ));
        }
    }
}
