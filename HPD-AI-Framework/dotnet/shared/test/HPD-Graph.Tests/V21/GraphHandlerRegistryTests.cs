using FluentAssertions;
using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Abstractions.Invocation;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Discovery;
using RuntimeGraph = HPDAgent.Graph.Abstractions.Graph.Graph;

namespace HPD.Graph.Tests.V21;

public sealed class GraphHandlerRegistryTests
{
    [Fact]
    public void GetInvoker_ReturnsRegisteredInvokerByName()
    {
        var invoker = new TestInvoker("handler_a");
        var registry = new GraphHandlerRegistry([invoker]);

        registry.GetInvoker("handler_a").Should().BeSameAs(invoker);
    }

    [Fact]
    public void GetInvoker_ReturnsNullForMissingHandler()
    {
        var registry = new GraphHandlerRegistry([new TestInvoker("handler_a")]);

        registry.GetInvoker("missing").Should().BeNull();
    }

    [Fact]
    public void GetInvoker_RejectsEmptyHandlerName()
    {
        var registry = new GraphHandlerRegistry([]);

        var act = () => registry.GetInvoker("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetAllInvokers_ReturnsAllRegisteredInvokers()
    {
        var a = new TestInvoker("a");
        var b = new TestInvoker("b");
        var registry = new GraphHandlerRegistry([a, b]);

        registry.GetAllInvokers().Should().BeEquivalentTo([a, b]);
    }

    [Fact]
    public async Task Invoker_CanReturnNodeExecutionResult()
    {
        var invoker = new TestInvoker("handler");
        var inputs = new HandlerInputs();

        var result = await invoker.ExecuteAsync(CreateContext(), inputs);

        result.Should().BeOfType<NodeExecutionResult.Success>();
    }

    [Fact]
    public void DuplicateHandlerNames_ThrowDuringRegistryConstruction()
    {
        var act = () => new GraphHandlerRegistry([new TestInvoker("same"), new TestInvoker("same")]);

        act.Should().Throw<ArgumentException>();
    }

    private sealed class TestInvoker(string handlerName) : IGraphNodeHandlerInvoker
    {
        public string HandlerName { get; } = handlerName;
        public Type HandlerType => typeof(TestInvoker);
        public Type ContextType => typeof(GraphContext);

        public ValueTask<NodeExecutionResult> ExecuteAsync(
            IGraphContext context,
            HandlerInputs inputs,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Success.Single(
                    new Dictionary<string, object> { ["ok"] = true },
                    TimeSpan.Zero,
                    new NodeExecutionMetadata()));
        }
    }

    private static GraphContext CreateContext()
    {
        return new GraphContext(
            executionId: "test",
            graph: new RuntimeGraph
            {
                Id = "g",
                Name = "G",
                EntryNodeId = "START",
                ExitNodeId = "END",
                Nodes = [],
                Edges = []
            },
            services: new EmptyServiceProvider());
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

}
