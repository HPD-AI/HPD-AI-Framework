using FluentAssertions;
using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Abstractions.Invocation;
using HPDAgent.Graph.Core.Builders;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Discovery;
using HPDAgent.Graph.Core.Orchestration;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Graph.Tests.V21;

public sealed class GraphOrchestratorInvokerTests
{
    [Fact]
    public async Task ExecuteAsync_UsesInvokerRegistryBeforeTypedHandlerResolution()
    {
        var typedHandler = new RecordingTypedHandler("work");
        var invoker = new RecordingInvoker("work");
        var services = new ServiceCollection()
            .AddSingleton<IGraphNodeHandler<GraphContext>>(typedHandler)
            .BuildServiceProvider();

        var context = CreateContext(services);
        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            handlerRegistry: new GraphHandlerRegistry([invoker]));

        await orchestrator.ExecuteAsync(context);

        invoker.CallCount.Should().Be(1);
        typedHandler.CallCount.Should().Be(0);
        context.IsNodeComplete("work").Should().BeTrue();
        context.Channels["node_output:work"].Get<Dictionary<string, object>>()["source"].Should().Be("invoker");
    }

    [Fact]
    public async Task ExecuteAsync_FallsBackToTypedHandlerWhenNoInvokerExists()
    {
        var typedHandler = new RecordingTypedHandler("work");
        var services = new ServiceCollection()
            .AddSingleton<IGraphNodeHandler<GraphContext>>(typedHandler)
            .BuildServiceProvider();

        var context = CreateContext(services);
        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            handlerRegistry: new GraphHandlerRegistry([new RecordingInvoker("other")]));

        await orchestrator.ExecuteAsync(context);

        typedHandler.CallCount.Should().Be(1);
        context.IsNodeComplete("work").Should().BeTrue();
        context.Channels["node_output:work"].Get<Dictionary<string, object>>()["source"].Should().Be("typed");
    }

    [Fact]
    public async Task ExecuteAsync_ReportsMissingHandlerWhenNeitherInvokerNorTypedHandlerExists()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var context = CreateContext(services);
        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            handlerRegistry: new GraphHandlerRegistry([]));

        var act = () => orchestrator.ExecuteAsync(context);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No handler found for node 'work' with handler name 'work'*");
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesInvokerContextCompatibilityErrors()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var context = CreateContext(services);
        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            handlerRegistry: new GraphHandlerRegistry([new RejectingInvoker("work")]));

        var act = () => orchestrator.ExecuteAsync(context);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*requires context type*");
    }

    private static GraphContext CreateContext(IServiceProvider services)
    {
        var graph = new GraphBuilder()
            .WithName("invoker-test")
            .AddHandlerNode("work", "Work", "work")
            .Build();

        return new GraphContext("exec", graph, services);
    }

    private sealed class RecordingTypedHandler(string handlerName) : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName { get; } = handlerName;
        public int CallCount { get; private set; }

        public Task<NodeExecutionResult> ExecuteAsync(
            GraphContext context,
            HandlerInputs inputs,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<NodeExecutionResult>(Success("typed"));
        }
    }

    private sealed class RecordingInvoker(string handlerName) : IGraphNodeHandlerInvoker
    {
        public string HandlerName { get; } = handlerName;
        public Type HandlerType => typeof(RecordingInvoker);
        public Type ContextType => typeof(GraphContext);
        public int CallCount { get; private set; }

        public ValueTask<NodeExecutionResult> ExecuteAsync(
            IGraphContext context,
            HandlerInputs inputs,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.FromResult<NodeExecutionResult>(Success("invoker"));
        }
    }

    private sealed class RejectingInvoker(string handlerName) : IGraphNodeHandlerInvoker
    {
        public string HandlerName { get; } = handlerName;
        public Type HandlerType => typeof(RejectingInvoker);
        public Type ContextType => typeof(OtherGraphContext);

        public ValueTask<NodeExecutionResult> ExecuteAsync(
            IGraphContext context,
            HandlerInputs inputs,
            CancellationToken cancellationToken = default)
        {
            if (context is not OtherGraphContext)
            {
                throw new InvalidOperationException(
                    $"Handler '{HandlerName}' requires context type '{typeof(OtherGraphContext).FullName}', but received '{context.GetType().FullName}'.");
            }

            return ValueTask.FromResult<NodeExecutionResult>(Success("unused"));
        }
    }

    private sealed class OtherGraphContext : GraphContext
    {
        public OtherGraphContext(string executionId, HPDAgent.Graph.Abstractions.Graph.Graph graph, IServiceProvider services)
            : base(executionId, graph, services)
        {
        }
    }

    private static NodeExecutionResult Success(string source)
    {
        return NodeExecutionResult.Success.Single(
            new Dictionary<string, object> { ["source"] = source },
            TimeSpan.Zero,
            new NodeExecutionMetadata());
    }
}
