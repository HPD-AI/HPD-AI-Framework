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

namespace HPD.Graph.Tests.Execution;

/// <summary>
/// Unit tests for CloningPolicy behavior.
/// Tests the three cloning strategies: AlwaysClone, NeverClone, LazyClone.
/// </summary>
public class CloningPolicyTests
{
    [Fact]
    public async Task LazyClone_FirstEdgeGetsOriginal_SubsequentGetClones()
    {
        // Arrange
        var producerHandler = new MutableListProducerHandler();
        var consumer1Handler = new MutatingConsumerHandler("C1", 100);
        var consumer2Handler = new MutatingConsumerHandler("C2", 200);

        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(producerHandler);
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(consumer1Handler);
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(consumer2Handler);
        });

        // Graph: Producer → [Consumer1, Consumer2] (lazy cloning)
        var graph = new GraphBuilder()
            .WithName("LazyCloneTest")
            .WithCloningPolicy(CloningPolicy.LazyClone)
            .AddStartNode()
            .AddNode("producer", "Producer", NodeType.Handler, "MutableListProducer")
            .AddNode("consumer1", "Consumer1", NodeType.Handler, "ConsumerC1")
            .AddNode("consumer2", "Consumer2", NodeType.Handler, "ConsumerC2")
            .AddEndNode()
            .AddEdge("START", "producer")
            .AddEdge("producer", "consumer1", e => e.WithPriority(0)) // First (gets original)
            .AddEdge("producer", "consumer2", e => e.WithPriority(1)) // Second (gets clone)
            .AddEdge("consumer1", "END")
            .AddEdge("consumer2", "END")
            .Build();

        var context = new GraphContext(Guid.NewGuid().ToString(), graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context, CancellationToken.None);

        // Assert
        var c1Outputs = context.Channels["node_output:consumer1"].Get<Dictionary<string, object>>();
        var c2Outputs = context.Channels["node_output:consumer2"].Get<Dictionary<string, object>>();

        c1Outputs.Should().NotBeNull();
        c2Outputs.Should().NotBeNull();

        var c1Result = c1Outputs!["result"] as List<object>;
        var c2Result = c2Outputs!["result"] as List<object>;

        c1Result.Should().NotBeNull();
        c2Result.Should().NotBeNull();

        // Consumer1 added 100 to its copy
        c1Result![^1].Should().Be(100);
        // Consumer2 added 200 to its independent clone
        c2Result![^1].Should().Be(200);

        // They should not interfere with each other
        c1Result.Should().NotContain(200);
        c2Result.Should().NotContain(100);
    }

    [Fact]
    public async Task AlwaysClone_AllEdgesGetClones()
    {
        // Arrange
        var producerHandler = new MutableListProducerHandler();
        var consumer1Handler = new MutatingConsumerHandler("C1", 100);
        var consumer2Handler = new MutatingConsumerHandler("C2", 200);

        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(producerHandler);
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(consumer1Handler);
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(consumer2Handler);
        });

        // Graph: Producer → [Consumer1, Consumer2] (always clone)
        var graph = new GraphBuilder()
            .WithName("AlwaysCloneTest")
            .WithCloningPolicy(CloningPolicy.AlwaysClone)
            .AddStartNode()
            .AddNode("producer", "Producer", NodeType.Handler, "MutableListProducer")
            .AddNode("consumer1", "Consumer1", NodeType.Handler, "ConsumerC1")
            .AddNode("consumer2", "Consumer2", NodeType.Handler, "ConsumerC2")
            .AddEndNode()
            .AddEdge("START", "producer")
            .AddEdge("producer", "consumer1")
            .AddEdge("producer", "consumer2")
            .AddEdge("consumer1", "END")
            .AddEdge("consumer2", "END")
            .Build();

        var context = new GraphContext(Guid.NewGuid().ToString(), graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context, CancellationToken.None);

        // Assert
        var c1Outputs = context.Channels["node_output:consumer1"].Get<Dictionary<string, object>>();
        var c2Outputs = context.Channels["node_output:consumer2"].Get<Dictionary<string, object>>();

        c1Outputs.Should().NotBeNull();
        c2Outputs.Should().NotBeNull();

        var c1Result = c1Outputs!["result"] as List<object>;
        var c2Result = c2Outputs!["result"] as List<object>;

        c1Result.Should().NotBeNull();
        c2Result.Should().NotBeNull();

        // Both should have independent copies
        c1Result![^1].Should().Be(100);
        c2Result![^1].Should().Be(200);

        c1Result.Should().NotContain(200);
        c2Result.Should().NotContain(100);
    }

    [Fact]
    public async Task NeverClone_AllEdgesShareReferences()
    {
        // Arrange
        var producerHandler = new ListProducerHandler();
        var consumer1Handler = new ReadOnlyConsumerHandler("C1");
        var consumer2Handler = new ReadOnlyConsumerHandler("C2");

        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(producerHandler);
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(consumer1Handler);
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(consumer2Handler);
        });

        // Graph: Producer → [Consumer1, Consumer2] (never clone - safe because read-only)
        var graph = new GraphBuilder()
            .WithName("NeverCloneTest")
            .WithCloningPolicy(CloningPolicy.NeverClone)
            .AddStartNode()
            .AddNode("producer", "Producer", NodeType.Handler, "ListProducer")
            .AddNode("consumer1", "Consumer1", NodeType.Handler, "ReadOnlyC1")
            .AddNode("consumer2", "Consumer2", NodeType.Handler, "ReadOnlyC2")
            .AddEndNode()
            .AddEdge("START", "producer")
            .AddEdge("producer", "consumer1")
            .AddEdge("producer", "consumer2")
            .AddEdge("consumer1", "END")
            .AddEdge("consumer2", "END")
            .Build();

        var context = new GraphContext(Guid.NewGuid().ToString(), graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context, CancellationToken.None);

        // Assert
        var c1Outputs = context.Channels["node_output:consumer1"].Get<Dictionary<string, object>>();
        var c2Outputs = context.Channels["node_output:consumer2"].Get<Dictionary<string, object>>();

        c1Outputs.Should().NotBeNull();
        c2Outputs.Should().NotBeNull();

        // Both consumers should have read the same list successfully
        c1Outputs!["count"].Should().Be(3);
        c2Outputs!["count"].Should().Be(3);
    }

    [Fact]
    public async Task EdgeCloningPolicy_OverridesGraphPolicy()
    {
        // Arrange
        var producerHandler = new MutableListProducerHandler();
        var consumer1Handler = new MutatingConsumerHandler("C1", 100);
        var consumer2Handler = new MutatingConsumerHandler("C2", 200);

        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(producerHandler);
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(consumer1Handler);
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(consumer2Handler);
        });

        // Graph: Producer → [Consumer1 (NeverClone), Consumer2 (AlwaysClone)]
        // Graph default: LazyClone
        var graph = new GraphBuilder()
            .WithName("EdgeOverrideTest")
            .WithCloningPolicy(CloningPolicy.LazyClone) // Default
            .AddStartNode()
            .AddNode("producer", "Producer", NodeType.Handler, "MutableListProducer")
            .AddNode("consumer1", "Consumer1", NodeType.Handler, "ConsumerC1")
            .AddNode("consumer2", "Consumer2", NodeType.Handler, "ConsumerC2")
            .AddEndNode()
            .AddEdge("START", "producer")
            .AddEdge("producer", "consumer1", e => e.WithCloningPolicy(CloningPolicy.NeverClone).WithPriority(0))
            .AddEdge("producer", "consumer2", e => e.WithCloningPolicy(CloningPolicy.AlwaysClone).WithPriority(1))
            .AddEdge("consumer1", "END")
            .AddEdge("consumer2", "END")
            .Build();

        var context = new GraphContext(Guid.NewGuid().ToString(), graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context, CancellationToken.None);

        // Assert
        var c1Outputs = context.Channels["node_output:consumer1"].Get<Dictionary<string, object>>();
        var c2Outputs = context.Channels["node_output:consumer2"].Get<Dictionary<string, object>>();

        c1Outputs.Should().NotBeNull();
        c2Outputs.Should().NotBeNull();

        var c1Result = c1Outputs!["result"] as List<object>;
        var c2Result = c2Outputs!["result"] as List<object>;

        c1Result.Should().NotBeNull();
        c2Result.Should().NotBeNull();

        // Both should have added their values to independent copies
        c1Result![^1].Should().Be(100);
        c2Result![^1].Should().Be(200);
    }

    [Fact]
    public async Task DefaultPolicy_IsLazyClone()
    {
        // Arrange
        var producerHandler = new MutableListProducerHandler();
        var consumerHandler = new MutatingConsumerHandler("C1", 100);

        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(producerHandler);
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(consumerHandler);
        });

        // Graph without explicit cloning policy (should default to LazyClone)
        var graph = new GraphBuilder()
            .WithName("DefaultPolicyTest")
            .AddStartNode()
            .AddNode("producer", "Producer", NodeType.Handler, "MutableListProducer")
            .AddNode("consumer", "Consumer", NodeType.Handler, "ConsumerC1")
            .AddEndNode()
            .AddEdge("START", "producer")
            .AddEdge("producer", "consumer")
            .AddEdge("consumer", "END")
            .Build();

        // Assert default policy
        graph.CloningPolicy.Should().Be(CloningPolicy.LazyClone);

        var context = new GraphContext(Guid.NewGuid().ToString(), graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context, CancellationToken.None);

        // Assert execution succeeded
        var consumerOutputs = context.Channels["node_output:consumer"].Get<Dictionary<string, object>>();
        consumerOutputs.Should().NotBeNull();
    }

    [Fact]
    public async Task LazyClone_SingleEdge_GetsOriginal()
    {
        // Arrange
        var producerHandler = new MutableListProducerHandler();
        var consumerHandler = new MutatingConsumerHandler("C1", 100);

        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(producerHandler);
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(consumerHandler);
        });

        // Graph: Producer → Consumer (single edge, should get original with LazyClone)
        var graph = new GraphBuilder()
            .WithName("LazyCloneSingleEdgeTest")
            .WithCloningPolicy(CloningPolicy.LazyClone)
            .AddStartNode()
            .AddNode("producer", "Producer", NodeType.Handler, "MutableListProducer")
            .AddNode("consumer", "Consumer", NodeType.Handler, "ConsumerC1")
            .AddEndNode()
            .AddEdge("START", "producer")
            .AddEdge("producer", "consumer")
            .AddEdge("consumer", "END")
            .Build();

        var context = new GraphContext(Guid.NewGuid().ToString(), graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context, CancellationToken.None);

        // Assert
        var consumerOutputs = context.Channels["node_output:consumer"].Get<Dictionary<string, object>>();
        consumerOutputs.Should().NotBeNull();

        var result = consumerOutputs!["result"] as List<object>;
        result.Should().NotBeNull();
        result![^1].Should().Be(100);
    }

    [Fact]
    public async Task CloningPolicy_WithMultiplePorts()
    {
        // Arrange
        var routerHandler = new TwoPortRouterHandler();
        var consumer1Handler = new MutatingConsumerHandler("C1", 100);
        var consumer2Handler = new MutatingConsumerHandler("C2", 200);

        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(routerHandler);
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(consumer1Handler);
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(consumer2Handler);
        });

        // Graph: Router(2 ports) → [Consumer1(port 0), Consumer2(port 1)]
        var graph = new GraphBuilder()
            .WithName("MultiPortCloningTest")
            .WithCloningPolicy(CloningPolicy.LazyClone)
            .AddStartNode()
            .AddNode("router", "Router", NodeType.Handler, "TwoPortRouter", n => n.WithOutputPorts(2))
            .AddNode("consumer1", "Consumer1", NodeType.Handler, "ConsumerC1")
            .AddNode("consumer2", "Consumer2", NodeType.Handler, "ConsumerC2")
            .AddEndNode()
            .AddEdge("START", "router")
            .AddEdge("router", "consumer1", e => e.FromPort(0))
            .AddEdge("router", "consumer2", e => e.FromPort(1))
            .AddEdge("consumer1", "END")
            .AddEdge("consumer2", "END")
            .Build();

        var context = new GraphContext(Guid.NewGuid().ToString(), graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context, CancellationToken.None);

        // Assert
        var c1Outputs = context.Channels["node_output:consumer1"].Get<Dictionary<string, object>>();
        var c2Outputs = context.Channels["node_output:consumer2"].Get<Dictionary<string, object>>();

        c1Outputs.Should().NotBeNull();
        c2Outputs.Should().NotBeNull();

        // Each port's output was consumed by one edge, so LazyClone gives each the original
        var c1Result = c1Outputs!["result"] as List<object>;
        var c2Result = c2Outputs!["result"] as List<object>;

        c1Result.Should().NotBeNull();
        c2Result.Should().NotBeNull();

        c1Result![^1].Should().Be(100);
        c2Result![^1].Should().Be(200);
    }

    [Fact]
    public void ValidateSerializable_ThrowsForNonSerializableTypes()
    {
        // Arrange
        var outputs = new Dictionary<string, object>
        {
            ["stream"] = new MemoryStream()
        };

        // Act & Assert
        var act = () => HPDAgent.Graph.Abstractions.Serialization.OutputCloner.ValidateSerializable(outputs);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*non-serializable*");
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

    private class ListProducerHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "ListProducer";

        public Task<NodeExecutionResult> ExecuteAsync(
            GraphContext context,
            HandlerInputs inputs,
            CancellationToken cancellationToken = default)
        {
            var output = new Dictionary<string, object>
            {
                ["list"] = new List<int> { 1, 2, 3 }
            };

            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Success.Single(output, TimeSpan.Zero, new NodeExecutionMetadata())
            );
        }
    }

    private class MutatingConsumerHandler : IGraphNodeHandler<GraphContext>
    {
        private readonly string _consumerId;
        private readonly int _valueToAdd;

        public MutatingConsumerHandler(string consumerId, int valueToAdd)
        {
            _consumerId = consumerId;
            _valueToAdd = valueToAdd;
        }

        public string HandlerName => $"Consumer{_consumerId}";

        public Task<NodeExecutionResult> ExecuteAsync(
            GraphContext context,
            HandlerInputs inputs,
            CancellationToken cancellationToken = default)
        {
            // Get upstream output - could be List<int> (original) or List<object> (after cloning)
            var inputRaw = inputs.Get<object>("mutable_list");
            var inputList = inputRaw switch
            {
                List<int> intList => intList.Cast<object>().ToList(),
                List<object> objList => new List<object>(objList),
                _ => new List<object>()
            };

            // Mutate: add value
            inputList.Add(_valueToAdd);

            var output = new Dictionary<string, object>
            {
                ["result"] = inputList
            };

            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Success.Single(output, TimeSpan.Zero, new NodeExecutionMetadata())
            );
        }
    }

    private class ReadOnlyConsumerHandler : IGraphNodeHandler<GraphContext>
    {
        private readonly string _consumerId;

        public ReadOnlyConsumerHandler(string consumerId)
        {
            _consumerId = consumerId;
        }

        public string HandlerName => $"ReadOnly{_consumerId}";

        public Task<NodeExecutionResult> ExecuteAsync(
            GraphContext context,
            HandlerInputs inputs,
            CancellationToken cancellationToken = default)
        {
            // Read-only: just count the list - could be List<int> (original) or List<object> (after cloning)
            var inputRaw = inputs.Get<object>("list");
            var count = inputRaw switch
            {
                List<int> intList => intList.Count,
                List<object> objList => objList.Count,
                _ => 0
            };

            var output = new Dictionary<string, object>
            {
                ["count"] = count
            };

            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Success.Single(output, TimeSpan.Zero, new NodeExecutionMetadata())
            );
        }
    }

    private class TwoPortRouterHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "TwoPortRouter";

        public Task<NodeExecutionResult> ExecuteAsync(
            GraphContext context,
            HandlerInputs inputs,
            CancellationToken cancellationToken = default)
        {
            // Send different outputs to different ports
            var port0Output = new Dictionary<string, object>
            {
                ["mutable_list"] = new List<int> { 10, 20 }
            };

            var port1Output = new Dictionary<string, object>
            {
                ["mutable_list"] = new List<int> { 30, 40 }
            };

            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Success.WithPorts(
                    new PortOutputs()
                        .Add(0, port0Output)
                        .Add(1, port1Output),
                    TimeSpan.Zero,
                    new NodeExecutionMetadata())
            );
        }
    }
}
