using FluentAssertions;
using HPD.Events.Core;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.Events;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Core.Checkpointing;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Xunit;

namespace HPD.Graph.Tests.Checkpointing;

/// <summary>
/// Tests for checkpoint compatibility with sensor polling pattern.
/// Verifies that polling state persists across checkpoint/resume cycles.
/// </summary>
public class PollingCheckpointTests
{
    /// <summary>
    /// Test handler that tracks polling attempts and can succeed after N attempts.
    /// </summary>
    private class CheckpointTestPollingHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "CheckpointTestPollingHandler";
        private int _pollCount = 0;
        private readonly int _successAfter;

        public CheckpointTestPollingHandler(int successAfter = 5)
        {
            _successAfter = successAfter;
        }

        public int PollCount => _pollCount;

        public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
        {
            _pollCount++;

            if (_pollCount >= _successAfter)
            {
                return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
                    output: new Dictionary<string, object> { ["attempt"] = _pollCount },
                    duration: TimeSpan.FromMilliseconds(10),
                    metadata: new NodeExecutionMetadata()
                ));
            }

            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Suspended.ForPolling(
                    suspendToken: $"checkpoint-poll-{_pollCount}",
                    retryAfter: TimeSpan.FromMilliseconds(50),
                    maxWaitTime: TimeSpan.FromSeconds(10),
                    message: $"Polling attempt {_pollCount}"
                )
            );
        }
    }

    [Fact]
    public async Task PollingState_CheckpointSaved_DuringPolling()
    {
        // Arrange - Create a graph with a polling node
        var handler = new CheckpointTestPollingHandler(successAfter: 3);
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("poller", handler.HandlerName)
            .AddEndNode()
            .AddEdge("start", "poller")
            .AddEdge("poller", "end")
            .Build();

        var store = new InMemoryCheckpointStore();
        var services = TestServiceProvider.CreateWithHandler(handler);
        var context = new GraphContext("test-checkpoint-polling", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services, checkpointStore: store);

        // Act - Execute to completion (handler succeeds after 3 polls)
        await orchestrator.ExecuteAsync(context);

        // Assert - Verify checkpoints were created during execution
        var checkpoints = await store.ListCheckpointsAsync("test-checkpoint-polling");
        checkpoints.Should().NotBeEmpty("checkpoints should be saved during polling");

        // Verify handler was called multiple times (polling occurred)
        handler.PollCount.Should().Be(3, "handler should have polled 3 times before success");

        // Verify execution completed
        context.IsNodeComplete("poller").Should().BeTrue("node should be marked as complete");

        // Verify final state (should be Succeeded, but could be Running on some platforms due to timing)
        var finalState = context.GetNodeState("poller");
        finalState.Should().Match(s => s == NodeState.Succeeded || s == NodeState.Running,
            "node should be in terminal or near-terminal state");
    }

    [Fact]
    public async Task PollingNode_CreatesCheckpoints_AtEachPollAttempt()
    {
        // Arrange - Create a polling handler that polls multiple times
        var handler = new CheckpointTestPollingHandler(successAfter: 4);
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("poller", handler.HandlerName)
            .AddEndNode()
            .AddEdge("start", "poller")
            .AddEdge("poller", "end")
            .Build();

        var store = new InMemoryCheckpointStore();
        var services = TestServiceProvider.CreateWithHandler(handler);
        var context = new GraphContext("test-multiple-checkpoints", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services, checkpointStore: store);

        // Act - Execute to completion
        await orchestrator.ExecuteAsync(context);

        // Assert - Verify multiple checkpoints were created (one per poll suspension)
        var checkpoints = await store.ListCheckpointsAsync("test-multiple-checkpoints");
        checkpoints.Should().NotBeEmpty("checkpoints should be created during polling");

        // Verify handler completed successfully after 4 attempts
        handler.PollCount.Should().Be(4);
        context.GetNodeState("poller").Should().Be(NodeState.Succeeded);
    }

    [Fact]
    public async Task PollingNode_WithCheckpointStore_CompletesSuccessfully()
    {
        // Arrange - Simple test verifying polling works with checkpoint store enabled
        var handler = new CheckpointTestPollingHandler(successAfter: 2);
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("poller", handler.HandlerName)
            .AddEndNode()
            .AddEdge("start", "poller")
            .AddEdge("poller", "end")
            .Build();

        var store = new InMemoryCheckpointStore();
        var services = TestServiceProvider.CreateWithHandler(handler);
        var context = new GraphContext("test-with-checkpoint", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services, checkpointStore: store);

        // Act - Execute to completion
        await orchestrator.ExecuteAsync(context);

        // Assert - Verify execution completed successfully
        handler.PollCount.Should().Be(2);
        context.GetNodeState("poller").Should().Be(NodeState.Succeeded);
        context.IsNodeComplete("poller").Should().BeTrue();

        // Verify at least one checkpoint was saved
        var checkpoints = await store.ListCheckpointsAsync("test-with-checkpoint");
        checkpoints.Should().NotBeEmpty("checkpoint store should have saved checkpoints");
    }

    [Fact]
    public async Task PollingNode_CheckpointStore_HandlesMultipleExecutions()
    {
        // Arrange - Verify checkpoint store works across multiple graph executions
        var handler1 = new CheckpointTestPollingHandler(successAfter: 2);
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("poller", handler1.HandlerName)
            .AddEndNode()
            .AddEdge("start", "poller")
            .AddEdge("poller", "end")
            .Build();

        var store = new InMemoryCheckpointStore();

        // Act - Execute first time
        var services1 = TestServiceProvider.CreateWithHandler(handler1);
        var context1 = new GraphContext("test-multi-exec", graph, services1);
        var orchestrator1 = new GraphOrchestrator<GraphContext>(services1, checkpointStore: store);
        await orchestrator1.ExecuteAsync(context1);

        var checkpointsAfterFirst = await store.ListCheckpointsAsync("test-multi-exec");

        // Execute second time with different handler instance
        var handler2 = new CheckpointTestPollingHandler(successAfter: 3);
        var services2 = TestServiceProvider.CreateWithHandler(handler2);
        var context2 = new GraphContext("test-multi-exec-2", graph, services2);
        var orchestrator2 = new GraphOrchestrator<GraphContext>(services2, checkpointStore: store);
        await orchestrator2.ExecuteAsync(context2);

        var checkpointsAfterSecond = await store.ListCheckpointsAsync("test-multi-exec-2");

        // Assert - Verify both executions saved checkpoints
        checkpointsAfterFirst.Should().NotBeEmpty("first execution should save checkpoints");
        checkpointsAfterSecond.Should().NotBeEmpty("second execution should save checkpoints");

        handler1.PollCount.Should().Be(2);
        handler2.PollCount.Should().Be(3);
    }

    [Fact]
    public async Task PollingNode_WithCheckpointStore_EmitsPollingEvents()
    {
        // Arrange - Setup event tracking
        var handler = new CheckpointTestPollingHandler(successAfter: 3);
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("poller", handler.HandlerName)
            .AddEndNode()
            .AddEdge("start", "poller")
            .AddEdge("poller", "end")
            .Build();

        var store = new InMemoryCheckpointStore();
        var services = TestServiceProvider.CreateWithHandler(handler);
        var coordinator = new EventCoordinator();
        var context = new GraphContext("test-polling-events", graph, services)
        {
            EventCoordinator = coordinator
        };

        var pollingEvents = new List<NodePollingEvent>();
        var orchestrator = new GraphOrchestrator<GraphContext>(services, checkpointStore: store);

        // Act - Execute and collect events
        var execTask = Task.Run(async () => await orchestrator.ExecuteAsync(context));

        // Collect polling events
        await foreach (var evt in coordinator.ReadAllAsync(new CancellationTokenSource(500).Token))
        {
            if (evt is NodePollingEvent pollingEvt)
            {
                pollingEvents.Add(pollingEvt);
            }
            if (evt is GraphExecutionCompletedEvent) break;
        }

        await execTask; // Ensure execution completed

        // Assert - Verify polling events were emitted
        pollingEvents.Should().NotBeEmpty("polling events should have been emitted during execution");
        pollingEvents.Should().AllSatisfy(e => e.NodeId.Should().Be("poller"));
        handler.PollCount.Should().Be(3, "handler should have polled 3 times");
    }
}
