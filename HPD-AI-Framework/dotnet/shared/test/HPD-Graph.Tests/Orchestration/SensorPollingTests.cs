using FluentAssertions;
using HPD.Events.Core;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Events;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Core.Builders;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Graph.Tests.Orchestration;

/// <summary>
/// Unit tests for sensor polling core logic in GraphOrchestrator.
/// Tests the iterative polling pattern, timeout handling, and retry logic.
/// </summary>
public class SensorPollingTests
{
    /// <summary>
    /// Test handler that polls for a condition N times before succeeding.
    /// </summary>
    private class PollingHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "PollingHandler";
        private int _pollCount = 0;
        private readonly int _pollsBeforeSuccess;
        private readonly TimeSpan _retryAfter;

        public PollingHandler(int pollsBeforeSuccess = 3, TimeSpan? retryAfter = null)
        {
            _pollsBeforeSuccess = pollsBeforeSuccess;
            _retryAfter = retryAfter ?? TimeSpan.FromMilliseconds(50);
        }

        public int PollCount => _pollCount;

        public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
        {
            _pollCount++;

            if (_pollCount < _pollsBeforeSuccess)
            {
                // Still polling - condition not met
                return Task.FromResult<NodeExecutionResult>(
                    NodeExecutionResult.Suspended.ForPolling(
                        suspendToken: $"poll-{_pollCount}",
                        retryAfter: _retryAfter,
                        maxWaitTime: TimeSpan.FromSeconds(10),
                        message: $"Poll attempt {_pollCount}"
                    )
                );
            }

            // Condition met - succeed
            return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
                output: new Dictionary<string, object> { ["pollCount"] = _pollCount },
                duration: TimeSpan.FromMilliseconds(10),
                metadata: new NodeExecutionMetadata()
            ));
        }
    }

    /// <summary>
    /// Test handler that polls forever (never succeeds).
    /// </summary>
    private class InfinitePollingHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "InfinitePollingHandler";
        private int _pollCount = 0;
        private readonly TimeSpan _retryAfter;

        public InfinitePollingHandler(TimeSpan? retryAfter = null)
        {
            _retryAfter = retryAfter ?? TimeSpan.FromMilliseconds(50);
        }

        public int PollCount => _pollCount;

        public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
        {
            _pollCount++;

            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Suspended.ForPolling(
                    suspendToken: $"infinite-poll-{_pollCount}",
                    retryAfter: _retryAfter,
                    maxWaitTime: TimeSpan.FromMilliseconds(200),
                    message: $"Infinite poll {_pollCount}"
                )
            );
        }
    }

    /// <summary>
    /// Test handler that respects max retries.
    /// </summary>
    private class MaxRetriesPollingHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "MaxRetriesPollingHandler";
        private int _pollCount = 0;

        public int PollCount => _pollCount;

        public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
        {
            _pollCount++;

            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Suspended.ForPolling(
                    suspendToken: $"retry-poll-{_pollCount}",
                    retryAfter: TimeSpan.FromMilliseconds(50),
                    maxRetries: 3,
                    message: $"Retry poll {_pollCount}"
                )
            );
        }
    }

    /// <summary>
    /// Test handler that transitions from polling to success after N attempts.
    /// </summary>
    private class ConditionalPollingHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "ConditionalPollingHandler";
        private int _pollCount = 0;
        private readonly int _successAfter;

        public ConditionalPollingHandler(int successAfter = 2)
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
                    output: new Dictionary<string, object> { ["result"] = "condition met" },
                    duration: TimeSpan.FromMilliseconds(10),
                    metadata: new NodeExecutionMetadata()
                ));
            }

            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Suspended.ForPolling(
                    suspendToken: $"conditional-{_pollCount}",
                    retryAfter: TimeSpan.FromMilliseconds(50),
                    maxWaitTime: TimeSpan.FromSeconds(10)
                )
            );
        }
    }

    /// <summary>
    /// Test handler for resource wait pattern.
    /// </summary>
    private class ResourceWaitHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "ResourceWaitHandler";
        private int _checkCount = 0;
        private readonly int _availableAfter;

        public ResourceWaitHandler(int availableAfter = 2)
        {
            _availableAfter = availableAfter;
        }

        public int CheckCount => _checkCount;

        public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
        {
            _checkCount++;

            if (_checkCount < _availableAfter)
            {
                return Task.FromResult<NodeExecutionResult>(
                    NodeExecutionResult.Suspended.ForResourceWait(
                        suspendToken: $"resource-{_checkCount}",
                        retryAfter: TimeSpan.FromMilliseconds(50),
                        maxWaitTime: TimeSpan.FromSeconds(10)
                    )
                );
            }

            return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
                output: new Dictionary<string, object> { ["acquired"] = true },
                duration: TimeSpan.FromMilliseconds(10),
                metadata: new NodeExecutionMetadata()
            ));
        }
    }

    [Fact]
    public async Task ExecutePollingNode_SucceedsAfterRetries_ShouldComplete()
    {
        // Arrange
        var handler = new ConditionalPollingHandler(successAfter: 3);
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("poller", handler.HandlerName)
            .AddEndNode()
            .AddEdge("start", "poller")
            .AddEdge("poller", "end")
            .Build();

        var services = TestServiceProvider.CreateWithHandler(handler);
        var context = new GraphContext("test-polling", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        handler.PollCount.Should().Be(3, "handler should be called 3 times before succeeding");
        context.Tags.Should().ContainKey("node_state:poller");
        context.Tags["node_state:poller"].Should().Contain(NodeState.Succeeded.ToString());
    }

    [Fact]
    public async Task ExecutePollingNode_Timeout_ShouldFailNode()
    {
        // Arrange
        var handler = new InfinitePollingHandler(retryAfter: TimeSpan.FromMilliseconds(50));
        var graph = new GraphBuilder()
            .WithName("TestGraph")
            .AddStartNode()
            .AddNode("poller", "Poller", NodeType.Handler, handler.HandlerName,
                n => n.WithErrorPolicy(ErrorPropagationPolicy.Isolate()))
            .AddEndNode()
            .AddEdge("START", "poller")
            .AddEdge("poller", "END")
            .Build();

        var services = TestServiceProvider.CreateWithHandler(handler);
        var context = new GraphContext("test-timeout", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        handler.PollCount.Should().BeGreaterThan(1, "handler should poll multiple times before timeout");
        context.Tags.Should().ContainKey("node_state:poller");
        context.Tags["node_state:poller"].Should().Contain(NodeState.Failed.ToString());
    }

    [Fact]
    public async Task ExecutePollingNode_MaxRetriesExceeded_ShouldFailNode()
    {
        // Arrange
        var handler = new MaxRetriesPollingHandler();
        var graph = new GraphBuilder()
            .WithName("TestGraph")
            .AddStartNode()
            .AddNode("poller", "Poller", NodeType.Handler, handler.HandlerName,
                n => n.WithErrorPolicy(ErrorPropagationPolicy.Isolate()))
            .AddEndNode()
            .AddEdge("START", "poller")
            .AddEdge("poller", "END")
            .Build();

        var services = TestServiceProvider.CreateWithHandler(handler);
        var context = new GraphContext("test-retries", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        handler.PollCount.Should().Be(3, "handler should be called exactly maxRetries times");
        context.Tags.Should().ContainKey("node_state:poller");
        context.Tags["node_state:poller"].Should().Contain(NodeState.Failed.ToString());
    }

    [Fact]
    public async Task ExecutePollingNode_ShouldSetPollingState()
    {
        // Arrange
        var handler = new ConditionalPollingHandler(successAfter: 2);
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("poller", handler.HandlerName)
            .AddEndNode()
            .AddEdge("start", "poller")
            .AddEdge("poller", "end")
            .Build();

        var services = TestServiceProvider.CreateWithHandler(handler);
        var context = new GraphContext("test-state", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.Tags.Should().ContainKey("node_state:poller");
        context.Tags["node_state:poller"].Should().Contain(NodeState.Succeeded.ToString());
    }

    [Fact]
    public async Task ExecutePollingNode_ShouldEmitPollingEvent()
    {
        // Arrange
        var handler = new ConditionalPollingHandler(successAfter: 2);
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("poller", handler.HandlerName)
            .AddEndNode()
            .AddEdge("start", "poller")
            .AddEdge("poller", "end")
            .Build();

        var services = TestServiceProvider.CreateWithHandler(handler);
        var coordinator = new EventCoordinator();
        var context = new GraphContext("test-events", graph, services)
        {
            EventCoordinator = coordinator
        };
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        var pollingEvents = new List<NodePollingEvent>();

        // Act
        var execTask = Task.Run(async () => await orchestrator.ExecuteAsync(context));
        await Task.Delay(100); // Give time for events

        // Collect events
        await foreach (var evt in coordinator.ReadAllAsync(new CancellationTokenSource(500).Token))
        {
            if (evt is NodePollingEvent pollingEvt)
            {
                pollingEvents.Add(pollingEvt);
            }
            if (evt is GraphExecutionCompletedEvent) break;
        }

        await execTask;

        // Assert
        pollingEvents.Should().NotBeEmpty("polling events should be emitted");
        pollingEvents[0].NodeId.Should().Be("poller");
        pollingEvents[0].ExecutionId.Should().Be("test-events");
    }

    [Fact]
    public async Task ExecutePollingNode_Timeout_ShouldEmitTimeoutEvent()
    {
        // Arrange
        var handler = new InfinitePollingHandler(retryAfter: TimeSpan.FromMilliseconds(50));
        var graph = new GraphBuilder()
            .WithName("TestGraph")
            .AddStartNode()
            .AddNode("poller", "Poller", NodeType.Handler, handler.HandlerName,
                n => n.WithErrorPolicy(ErrorPropagationPolicy.Isolate()))
            .AddEndNode()
            .AddEdge("START", "poller")
            .AddEdge("poller", "END")
            .Build();

        var services = TestServiceProvider.CreateWithHandler(handler);
        var coordinator = new EventCoordinator();
        var context = new GraphContext("test-timeout-event", graph, services)
        {
            EventCoordinator = coordinator
        };
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        var timeoutEvents = new List<NodePollingTimeoutEvent>();

        // Act
        var execTask = Task.Run(async () => await orchestrator.ExecuteAsync(context));

        // Collect events
        await foreach (var evt in coordinator.ReadAllAsync(new CancellationTokenSource(2000).Token))
        {
            if (evt is NodePollingTimeoutEvent timeoutEvt)
            {
                timeoutEvents.Add(timeoutEvt);
            }
            if (evt is GraphExecutionCompletedEvent) break;
        }

        await execTask;

        // Assert
        timeoutEvents.Should().ContainSingle("one timeout event should be emitted");
        timeoutEvents[0].NodeId.Should().Be("poller");
        timeoutEvents[0].Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public async Task ExecutePollingNode_MaxRetries_ShouldEmitMaxRetriesEvent()
    {
        // Arrange
        var handler = new MaxRetriesPollingHandler();
        var graph = new GraphBuilder()
            .WithName("TestGraph")
            .AddStartNode()
            .AddNode("poller", "Poller", NodeType.Handler, handler.HandlerName,
                n => n.WithErrorPolicy(ErrorPropagationPolicy.Isolate()))
            .AddEndNode()
            .AddEdge("START", "poller")
            .AddEdge("poller", "END")
            .Build();

        var services = TestServiceProvider.CreateWithHandler(handler);
        var coordinator = new EventCoordinator();
        var context = new GraphContext("test-maxretries-event", graph, services)
        {
            EventCoordinator = coordinator
        };
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        var maxRetriesEvents = new List<NodePollingMaxRetriesEvent>();

        // Act
        var execTask = Task.Run(async () => await orchestrator.ExecuteAsync(context));

        // Collect events
        await foreach (var evt in coordinator.ReadAllAsync(new CancellationTokenSource(2000).Token))
        {
            if (evt is NodePollingMaxRetriesEvent maxRetryEvt)
            {
                maxRetriesEvents.Add(maxRetryEvt);
            }
            if (evt is GraphExecutionCompletedEvent) break;
        }

        await execTask;

        // Assert
        maxRetriesEvents.Should().ContainSingle("one max retries event should be emitted");
        maxRetriesEvents[0].NodeId.Should().Be("poller");
        maxRetriesEvents[0].Attempts.Should().Be(3);
    }

    [Fact]
    public async Task ExecutePollingNode_ShouldRespectRetryDelay()
    {
        // Arrange
        var handler = new ConditionalPollingHandler(successAfter: 3);
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("poller", handler.HandlerName)
            .AddEndNode()
            .AddEdge("start", "poller")
            .AddEdge("poller", "end")
            .Build();

        var services = TestServiceProvider.CreateWithHandler(handler);
        var context = new GraphContext("test-delay", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        var startTime = DateTimeOffset.UtcNow;

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        var elapsed = DateTimeOffset.UtcNow - startTime;
        // Should take at least 2 retries * 50ms = 100ms
        elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(90));
    }

    [Fact]
    public async Task ExecutePollingNode_ShouldClearPollingInfoOnSuccess()
    {
        // Arrange
        var handler = new ConditionalPollingHandler(successAfter: 2);
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("poller", handler.HandlerName)
            .AddEndNode()
            .AddEdge("start", "poller")
            .AddEdge("poller", "end")
            .Build();

        var services = TestServiceProvider.CreateWithHandler(handler);
        var context = new GraphContext("test-polling-info", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - polling info should be cleared after success
        context.Tags.Should().NotContainKey("polling_info:poller",
            "polling info should be removed after completion");
    }

    [Fact]
    public async Task ExecutePollingNode_ResourceWait_ShouldPollUntilAvailable()
    {
        // Arrange
        var handler = new ResourceWaitHandler(availableAfter: 2);
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("resource", handler.HandlerName)
            .AddEndNode()
            .AddEdge("start", "resource")
            .AddEdge("resource", "end")
            .Build();

        var services = TestServiceProvider.CreateWithHandler(handler);
        var context = new GraphContext("test-resource", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        handler.CheckCount.Should().Be(2);
        context.Tags["node_state:resource"].Should().Contain(NodeState.Succeeded.ToString());
    }

    [Fact]
    public async Task ExecutePollingNode_CancellationRequested_ShouldCancelGracefully()
    {
        // Arrange
        var handler = new InfinitePollingHandler(retryAfter: TimeSpan.FromMilliseconds(100));
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("poller", handler.HandlerName)
            .AddEndNode()
            .AddEdge("start", "poller")
            .AddEdge("poller", "end")
            .Build();

        var services = TestServiceProvider.CreateWithHandler(handler);
        var context = new GraphContext("test-cancel", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50)); // Cancel before timeout (200ms)

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await orchestrator.ExecuteAsync(context, cts.Token));

        handler.PollCount.Should().BeGreaterThan(0, "at least one poll should have occurred");
    }

    [Fact]
    public async Task ExecutePollingNode_TransitionToSuccess_ShouldUpdateStateCorrectly()
    {
        // Arrange
        var handler = new ConditionalPollingHandler(successAfter: 2);
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("poller", handler.HandlerName)
            .AddEndNode()
            .AddEdge("start", "poller")
            .AddEdge("poller", "end")
            .Build();

        var services = TestServiceProvider.CreateWithHandler(handler);
        var context = new GraphContext("test-transition", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.Tags["node_state:poller"].Should().Contain(NodeState.Succeeded.ToString());
        context.Tags.Should().NotContainKey("polling_info:poller");
    }

    [Fact]
    public async Task ExecutePollingNode_ShouldCompleteSuccessfully()
    {
        // Arrange
        var handler = new ConditionalPollingHandler(successAfter: 2);
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("poller", handler.HandlerName)
            .AddEndNode()
            .AddEdge("start", "poller")
            .AddEdge("poller", "end")
            .Build();

        var services = TestServiceProvider.CreateWithHandler(handler);
        var context = new GraphContext("test-complete", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        handler.PollCount.Should().Be(2, "handler should be called 2 times before succeeding");
        context.CompletedNodes.Should().Contain("poller");
        context.Tags["node_state:poller"].Should().Contain(NodeState.Succeeded.ToString());
    }
}
