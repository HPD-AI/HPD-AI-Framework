using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Xunit;

namespace HPD.Graph.Tests.Integration;

/// <summary>
/// Integration tests for Map nodes with polling support.
/// Tests specification from Section 3.2 of HPD_GRAPH_WORKFLOW_PRIMITIVES_V5.md:
/// - Per-item polling with independent retry tracking
/// - Dynamic parallelism (adjusts concurrency based on items ready)
/// - Intelligent delay (earliest retry time calculation)
/// - Per-item timeout and max retries
/// </summary>
public class MapPollingIntegrationTests
{
    private readonly IServiceProvider _services;

    public MapPollingIntegrationTests()
    {
        _services = TestServiceProvider.Create();
    }

    /// <summary>
    /// Test handler that polls a specific number of times before succeeding.
    /// Used to test per-item polling behavior.
    /// </summary>
    private class ItemPollingHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "ItemPollingHandler";
        private readonly Dictionary<string, int> _pollCounts = new();
        private readonly int _pollsBeforeSuccess;

        public ItemPollingHandler(int pollsBeforeSuccess = 2)
        {
            _pollsBeforeSuccess = pollsBeforeSuccess;
        }

        public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
        {
            var item = inputs.TryGet<string>("item", out var value) ? value : "unknown";

            if (!_pollCounts.ContainsKey(item))
                _pollCounts[item] = 0;

            _pollCounts[item]++;
            var pollCount = _pollCounts[item];

            if (pollCount < _pollsBeforeSuccess)
            {
                // Still polling
                return Task.FromResult<NodeExecutionResult>(
                    NodeExecutionResult.Suspended.ForPolling(
                        suspendToken: $"item-{item}-poll-{pollCount}",
                        retryAfter: TimeSpan.FromMilliseconds(50),
                        maxWaitTime: TimeSpan.FromSeconds(10),
                        message: $"Polling item {item}, attempt {pollCount}"
                    )
                );
            }

            // Condition met
            return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
                output: new Dictionary<string, object> { ["result"] = $"processed-{item}", ["pollCount"] = pollCount },
                duration: TimeSpan.FromMilliseconds(10),
                metadata: new NodeExecutionMetadata()
            ));
        }
    }

    /// <summary>
    /// Test handler that always polls (never succeeds).
    /// Used to test per-item timeout behavior.
    /// </summary>
    private class InfiniteItemPollingHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "InfiniteItemPollingHandler";

        public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
        {
            var item = inputs.TryGet<string>("item", out var value) ? value : "unknown";

            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Suspended.ForPolling(
                    suspendToken: $"infinite-{item}",
                    retryAfter: TimeSpan.FromMilliseconds(50),
                    maxWaitTime: TimeSpan.FromMilliseconds(200), // Short timeout for test
                    message: $"Infinite poll for {item}"
                )
            );
        }
    }

    /// <summary>
    /// Test handler that respects max retries.
    /// </summary>
    private class MaxRetriesItemHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "MaxRetriesItemHandler";

        public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
        {
            var item = inputs.TryGet<string>("item", out var value) ? value : "unknown";

            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Suspended.ForPolling(
                    suspendToken: $"retry-{item}",
                    retryAfter: TimeSpan.FromMilliseconds(50),
                    maxRetries: 2, // Only 2 retries allowed
                    message: $"Retry-limited poll for {item}"
                )
            );
        }
    }

    [Fact]
    public async Task MapNode_WithPollingItems_AllItemsSucceed()
    {
        // Arrange - Processor graph that polls twice before succeeding
        var handler = new ItemPollingHandler(pollsBeforeSuccess: 2);
        var services = TestServiceProvider.CreateWithCustomHandler(handler);

        var processorGraph = new TestGraphBuilder()
            .AddStartNode("sub_start")
            .AddHandlerNode("sub_handler", "ItemPollingHandler")
            .AddEndNode("sub_end")
            .AddEdge("sub_start", "sub_handler")
            .AddEdge("sub_handler", "sub_end")
            .Build();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("prepare", "ListProducerHandler") // Produces 3 items
            .AddMapNode("map", processorGraph, maxParallelMapTasks: 2)
            .AddEndNode()
            .AddEdge("start", "prepare")
            .AddEdge("prepare", "map")
            .AddEdge("map", "end")
            .Build();

        var context = new GraphContext("test-exec", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - All items should complete after polling
        context.IsComplete.Should().BeTrue();
        var results = context.Channels["node_output:map"].Get<List<object?>>();
        results.Should().NotBeNull();
        results.Should().HaveCount(3);
        results.Should().AllSatisfy(r => r.Should().NotBeNull());
    }

    [Fact]
    public async Task MapNode_WithMixedPollingAndImmediate_HandlesCorrectly()
    {
        // Arrange - Some items poll, others succeed immediately
        var mixedHandler = new MixedPollingHandler(itemsToPolls: new[] { "item1", "item3" });
        var services = TestServiceProvider.CreateWithCustomHandler(mixedHandler);

        var processorGraph = new TestGraphBuilder()
            .AddStartNode("sub_start")
            .AddHandlerNode("sub_handler", "MixedPollingHandler")
            .AddEndNode("sub_end")
            .AddEdge("sub_start", "sub_handler")
            .AddEdge("sub_handler", "sub_end")
            .Build();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("prepare", "ListProducerHandler") // Produces ["item1", "item2", "item3"]
            .AddMapNode("map", processorGraph, maxParallelMapTasks: 3)
            .AddEndNode()
            .AddEdge("start", "prepare")
            .AddEdge("prepare", "map")
            .AddEdge("map", "end")
            .Build();

        var context = new GraphContext("test-exec", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.IsComplete.Should().BeTrue();
        var results = context.Channels["node_output:map"].Get<List<object?>>();
        results.Should().NotBeNull();
        results.Should().HaveCount(3);
        // item2 completes immediately, item1 and item3 after polling
    }

    [Fact]
    public async Task MapNode_WithPerItemTimeout_FailsTimeoutItems()
    {
        // Arrange - Items that never complete (timeout after 200ms)
        var handler = new InfiniteItemPollingHandler();
        var services = TestServiceProvider.CreateWithCustomHandler(handler);

        var processorGraph = new TestGraphBuilder()
            .AddStartNode("sub_start")
            .AddHandlerNode("sub_handler", "InfiniteItemPollingHandler")
            .AddEndNode("sub_end")
            .AddEdge("sub_start", "sub_handler")
            .AddEdge("sub_handler", "sub_end")
            .Build();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("prepare", "ListProducerHandler")
            .AddMapNode("map", processorGraph, maxParallelMapTasks: 2, errorMode: MapErrorMode.ContinueWithNulls)
            .AddEndNode()
            .AddEdge("start", "prepare")
            .AddEdge("prepare", "map")
            .AddEdge("map", "end")
            .Build();

        var context = new GraphContext("test-exec", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Map completes but items are null due to timeout
        context.IsComplete.Should().BeTrue();
        var results = context.Channels["node_output:map"].Get<List<object?>>();
        results.Should().NotBeNull();
        results.Should().HaveCount(3);

        // Debug output
        for (int i = 0; i < results.Count; i++)
        {
            Console.WriteLine($"Result[{i}]: {results[i]?.GetType().Name ?? "null"} = {results[i]}");
        }

        // All items timeout and become null (ContinueWithNulls mode)
        results.Should().OnlyContain(r => r == null);
    }

    [Fact]
    public async Task MapNode_WithPerItemMaxRetries_FailsAfterRetryLimit()
    {
        // Arrange - Items that exceed max retries
        var handler = new MaxRetriesItemHandler();
        var services = TestServiceProvider.CreateWithCustomHandler(handler);

        var processorGraph = new TestGraphBuilder()
            .AddStartNode("sub_start")
            .AddHandlerNode("sub_handler", "MaxRetriesItemHandler")
            .AddEndNode("sub_end")
            .AddEdge("sub_start", "sub_handler")
            .AddEdge("sub_handler", "sub_end")
            .Build();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("prepare", "ListProducerHandler")
            .AddMapNode("map", processorGraph, maxParallelMapTasks: 2, errorMode: MapErrorMode.ContinueOmitFailures)
            .AddEndNode()
            .AddEdge("start", "prepare")
            .AddEdge("prepare", "map")
            .AddEdge("map", "end")
            .Build();

        var context = new GraphContext("test-exec", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Map completes but items are omitted (max retries exceeded)
        context.IsComplete.Should().BeTrue();
        var results = context.Channels["node_output:map"].Get<List<object?>>();
        results.Should().NotBeNull();
        // All items fail after 2 retries and are omitted
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task MapNode_WithDynamicParallelism_AdjustsConcurrencyDuringPolling()
    {
        // Arrange - Variable polling times to test dynamic parallelism
        var handler = new VariablePollingHandler(new Dictionary<string, int>
        {
            ["item1"] = 1, // Succeeds after 1 poll
            ["item2"] = 3, // Succeeds after 3 polls
            ["item3"] = 2  // Succeeds after 2 polls
        });
        var services = TestServiceProvider.CreateWithCustomHandler(handler);

        var processorGraph = new TestGraphBuilder()
            .AddStartNode("sub_start")
            .AddHandlerNode("sub_handler", "VariablePollingHandler")
            .AddEndNode("sub_end")
            .AddEdge("sub_start", "sub_handler")
            .AddEdge("sub_handler", "sub_end")
            .Build();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("prepare", "ListProducerHandler")
            .AddMapNode("map", processorGraph, maxParallelMapTasks: 2) // Limited concurrency
            .AddEndNode()
            .AddEdge("start", "prepare")
            .AddEdge("prepare", "map")
            .AddEdge("map", "end")
            .Build();

        var context = new GraphContext("test-exec", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - All items complete with dynamic parallelism
        context.IsComplete.Should().BeTrue();
        var results = context.Channels["node_output:map"].Get<List<object?>>();
        results.Should().NotBeNull();
        results.Should().HaveCount(3);
        results.Should().AllSatisfy(r => r.Should().NotBeNull());

        // Verify dynamic parallelism worked: item1 finishes first (1 poll),
        // then item3 (2 polls), then item2 (3 polls)
    }

    /// <summary>
    /// Handler that polls different items different numbers of times.
    /// Used to test dynamic parallelism.
    /// </summary>
    private class VariablePollingHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "VariablePollingHandler";
        private readonly Dictionary<string, int> _pollsRequired;
        private readonly Dictionary<string, int> _pollCounts = new();

        public VariablePollingHandler(Dictionary<string, int> pollsRequired)
        {
            _pollsRequired = pollsRequired;
        }

        public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
        {
            var item = inputs.TryGet<string>("item", out var value) ? value : "unknown";

            if (!_pollCounts.ContainsKey(item))
                _pollCounts[item] = 0;

            _pollCounts[item]++;
            var pollCount = _pollCounts[item];
            var required = _pollsRequired.GetValueOrDefault(item, 2);

            if (pollCount < required)
            {
                return Task.FromResult<NodeExecutionResult>(
                    NodeExecutionResult.Suspended.ForPolling(
                        suspendToken: $"var-{item}-{pollCount}",
                        retryAfter: TimeSpan.FromMilliseconds(50),
                        maxWaitTime: TimeSpan.FromSeconds(10),
                        message: $"Variable poll {item}: {pollCount}/{required}"
                    )
                );
            }

            return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
                output: new Dictionary<string, object> { ["result"] = $"done-{item}", ["polls"] = pollCount },
                duration: TimeSpan.FromMilliseconds(10),
                metadata: new NodeExecutionMetadata()
            ));
        }
    }

    /// <summary>
    /// Handler that polls only specific items, others succeed immediately.
    /// Used to test mixed polling/immediate execution.
    /// </summary>
    private class MixedPollingHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "MixedPollingHandler";
        private readonly HashSet<string> _itemsToPolls;
        private readonly Dictionary<string, int> _pollCounts = new();

        public MixedPollingHandler(string[] itemsToPolls)
        {
            _itemsToPolls = new HashSet<string>(itemsToPolls);
        }

        public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
        {
            var item = inputs.TryGet<string>("item", out var value) ? value : "unknown";

            if (!_itemsToPolls.Contains(item))
            {
                // Immediate success for non-polling items
                return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
                    output: new Dictionary<string, object> { ["result"] = $"immediate-{item}" },
                    duration: TimeSpan.FromMilliseconds(5),
                    metadata: new NodeExecutionMetadata()
                ));
            }

            // Polling items
            if (!_pollCounts.ContainsKey(item))
                _pollCounts[item] = 0;

            _pollCounts[item]++;
            var pollCount = _pollCounts[item];

            if (pollCount < 2)
            {
                return Task.FromResult<NodeExecutionResult>(
                    NodeExecutionResult.Suspended.ForPolling(
                        suspendToken: $"mixed-{item}-{pollCount}",
                        retryAfter: TimeSpan.FromMilliseconds(50),
                        maxWaitTime: TimeSpan.FromSeconds(10),
                        message: $"Mixed poll {item}: {pollCount}"
                    )
                );
            }

            return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
                output: new Dictionary<string, object> { ["result"] = $"polled-{item}", ["pollCount"] = pollCount },
                duration: TimeSpan.FromMilliseconds(10),
                metadata: new NodeExecutionMetadata()
            ));
        }
    }
}
