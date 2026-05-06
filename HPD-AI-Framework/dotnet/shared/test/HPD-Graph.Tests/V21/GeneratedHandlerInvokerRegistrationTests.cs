using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Discovery;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Abstractions.Invocation;
using HPDAgent.Graph.Core.Builders;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Discovery;
using HPDAgent.Graph.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Graph.Tests.V21;

public sealed class GeneratedHandlerInvokerRegistrationTests
{
    [Fact]
    public void AddGeneratedGraphContextHandlers_RegistersConcreteHandlerTypedHandlerAndInvoker()
    {
        using var provider = new ServiceCollection()
            .AddGeneratedGraphContextHandlers()
            .BuildServiceProvider();

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<GeneratedTestHandler>()
            .Should().NotBeNull();
        scope.ServiceProvider.GetServices<IGraphNodeHandler<GraphContext>>()
            .Should().ContainSingle(handler => handler.HandlerName == "generated_test");
        scope.ServiceProvider.GetServices<IGraphNodeHandlerInvoker>()
            .Should().ContainSingle(invoker => invoker.HandlerName == "generated_test");
        scope.ServiceProvider.GetRequiredService<IGeneratedHandlerCatalog>()
            .Should().BeOfType<GeneratedHandlerCatalog>();
    }

    [Fact]
    public async Task GeneratedInvoker_ExecutesSocketBridgeHandler()
    {
        using var provider = new ServiceCollection()
            .AddGeneratedGraphContextHandlers()
            .BuildServiceProvider();

        using var scope = provider.CreateScope();
        var registry = new GraphHandlerRegistry(scope.ServiceProvider.GetServices<IGraphNodeHandlerInvoker>());
        var invoker = registry.GetInvoker("generated_test");

        var inputs = new HandlerInputs();
        inputs.Add("text", "ha");
        inputs.Add("multiplier", 3);

        var context = CreateContext(scope.ServiceProvider);
        var result = await invoker!.ExecuteAsync(context, inputs);

        var success = result.Should().BeOfType<NodeExecutionResult.Success>().Subject;
        success.PortOutputs[0]["Result"].Should().Be("hahaha");
        success.PortOutputs[0]["Length"].Should().Be(6);
    }

    [Fact]
    public void GeneratedHandlerCatalog_DescribesSocketInputsAndOutputs()
    {
        using var provider = new ServiceCollection()
            .AddGeneratedGraphContextHandlers()
            .BuildServiceProvider();

        var catalog = provider.GetRequiredService<IGeneratedHandlerCatalog>();
        var descriptor = catalog.GetHandlers()["generated_test"];

        descriptor.HandlerName.Should().Be("generated_test");
        descriptor.DisplayName.Should().Be("GeneratedTest");
        descriptor.Domain.Should().Be("graph");
        descriptor.HandlerType.Should().Be(typeof(GeneratedTestHandler).FullName);
        descriptor.ContextType.Should().Be(typeof(GraphContext).FullName);

        descriptor.Inputs.Should().BeEquivalentTo(
        [
            new SocketDescriptor
            {
                Name = "text",
                TypeName = "string",
                Direction = SocketDirection.Input,
                Required = true,
                Description = "Test input text"
            },
            new SocketDescriptor
            {
                Name = "multiplier",
                TypeName = "int?",
                Direction = SocketDirection.Input,
                Required = false,
                Description = "Optional multiplier"
            }
        ]);

        descriptor.Outputs.Should().BeEquivalentTo(
        [
            new SocketDescriptor
            {
                Name = "Result",
                TypeName = "string",
                Direction = SocketDirection.Output,
                Required = true,
                Description = "Concatenated result"
            },
            new SocketDescriptor
            {
                Name = "Length",
                TypeName = "int",
                Direction = SocketDirection.Output,
                Required = true,
                Description = "Result length"
            }
        ]);
    }

    private static GraphContext CreateContext(IServiceProvider services)
    {
        var graph = new GraphBuilder()
            .WithName("generated-handler-test")
            .AddHandlerNode("generated", "Generated", "generated_test")
            .Build();

        var context = new GraphContext("exec-generated", graph, services);
        context.SetCurrentNode("generated");
        return context;
    }
}
