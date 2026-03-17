using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Graph.Tests.Serialization;

/// <summary>
/// Integration tests for deep cloning isolation in parallel execution.
/// - Layer-parallel execution isolates state
/// - State isolation prevents corruption in parallel scenarios
/// </summary>
public class CloningIsolationIntegrationTests
{
    /// <summary>
    /// Test: Layer-Parallel Isolation
    /// Verifies that nodes in the same layer executing in parallel don't interfere.
    /// </summary>
    [Fact]
    public async Task LayerParallel_IsolatesSharedUpstreamOutputs()
    {
        // Arrange - Create handlers and services
        var producerHandler = new MutableListProducerHandler();
        var consumerBHandler = new MutatingConsumerHandler("B");
        var consumerCHandler = new MutatingConsumerHandler("C");

        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(producerHandler);
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(consumerBHandler);
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(consumerCHandler);
        });

        // Graph: A → [B, C] (B and C execute in parallel)
        var graph = new TestGraphBuilder()
            .AddStartNode("start")
            .AddHandlerNode("a", "MutableListProducer")
            .AddHandlerNode("b", "ConsumerB")
            .AddHandlerNode("c", "ConsumerC")
            .AddEndNode("end")
            .AddEdge("start", "a")
            .AddEdge("a", "b")
            .AddEdge("a", "c")
            .AddEdge("b", "end")
            .AddEdge("c", "end")
            .Build();

        var context = new GraphContext(
            Guid.NewGuid().ToString(),
            graph,
            services
        );

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context, CancellationToken.None);

        // Assert - Original list unchanged
        var aOutputs = context.Channels["node_output:a"].Get<Dictionary<string, object>>();
        aOutputs.Should().NotBeNull();

        // After cloning, List<int> becomes List<object> due to JSON round-trip
        var originalList = aOutputs!["mutable_list"] as List<object>;
        originalList.Should().NotBeNull();
        originalList.Should().HaveCount(3);
        originalList[0].Should().Be(1);
        originalList[1].Should().Be(2);
        originalList[2].Should().Be(3);

        // B and C should have independent copies
        var bOutputs = context.Channels["node_output:b"].Get<Dictionary<string, object>>();
        bOutputs.Should().NotBeNull();

        var bResult = bOutputs!["result"] as List<object>;
        bResult.Should().NotBeNull();
        // B added 100 to its independent copy
        bResult![^1].Should().Be(100);

        var cOutputs = context.Channels["node_output:c"].Get<Dictionary<string, object>>();
        cOutputs.Should().NotBeNull();

        var cResult = cOutputs!["result"] as List<object>;
        cResult.Should().NotBeNull();
        // C added 200 to its independent copy
        cResult![^1].Should().Be(200);

        // Verify they didn't interfere with each other
        bResult.Should().NotContain(200);
        cResult.Should().NotContain(100);
    }

    // Test Handlers

    private class MutableListProducerHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "MutableListProducer";

        public Task<NodeExecutionResult> ExecuteAsync(
            GraphContext context,
            HandlerInputs inputs,
            CancellationToken cancellationToken = default)
        {
            var output = new Dictionary<string, object>
            {
                ["mutable_list"] = new List<int> { 1, 2, 3 }
            };

            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Success.Single(output, TimeSpan.Zero, new NodeExecutionMetadata())
            );
        }
    }

    private class MutatingConsumerHandler : IGraphNodeHandler<GraphContext>
    {
        private readonly string _consumerId;

        public MutatingConsumerHandler(string consumerId)
        {
            _consumerId = consumerId;
        }

        public string HandlerName => $"Consumer{_consumerId}";

        public Task<NodeExecutionResult> ExecuteAsync(
            GraphContext context,
            HandlerInputs inputs,
            CancellationToken cancellationToken = default)
        {
            // Get upstream output from node "a"
            var upstreamOutputs = context.Channels["node_output:a"].Get<Dictionary<string, object>>();
            var input = upstreamOutputs?["mutable_list"] as List<int> ?? new List<int>();

            // Create a mutable copy and add consumer-specific value
            var result = new List<object>(input.Cast<object>());
            result.Add(_consumerId == "B" ? 100 : 200);

            var output = new Dictionary<string, object>
            {
                ["result"] = result
            };

            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Success.Single(output, TimeSpan.Zero, new NodeExecutionMetadata())
            );
        }
    }
}
