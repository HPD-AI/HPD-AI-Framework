using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPD.Graph.Abstractions.Discovery;
using HPD.Graph.Abstractions.Execution;
using HPD.Graph.Abstractions.Handlers;
using HPD.Graph.Abstractions.Invocation;
using HPD.Graph.Abstractions.Serialization;
using HPD.Graph.Core.Builders;
using HPD.Graph.Core.Context;
using HPD.Graph.Core.Discovery;
using HPD.Graph.Extensions;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Graph.Tests.V21;

public sealed class GeneratedHandlerInvokerRegistrationTests
{
    [Fact]
    public void AddGeneratedGraphContextHandlers_RegistersConcreteHandlerTypedHandlerAndInvoker()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<IGraphJsonTypeInfoResolverContributor>(
                new TestResolverContributor(GeneratedHandlerTestJsonContext.Default))
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

        if (result is NodeExecutionResult.Failure failure)
        {
            throw new InvalidOperationException("Generated config handler failed.", failure.Exception);
        }

        var success = result.Should().BeOfType<NodeExecutionResult.Success>().Subject;
        success.PortOutputs[0]["Result"].Should().Be("hahaha");
        success.PortOutputs[0]["Length"].Should().Be(6);
    }

    [Fact]
    public async Task CleanPartialHandler_RegistersAndExecutesWithoutUserDeclaredInterface()
    {
        using var provider = new ServiceCollection()
            .AddGeneratedGraphContextHandlers()
            .BuildServiceProvider();

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<CleanGeneratedHandler>()
            .Should().NotBeNull();
        scope.ServiceProvider.GetServices<IGraphNodeHandler<GraphContext>>()
            .Should().ContainSingle(handler => handler.HandlerName == "clean_generated");

        var registry = new GraphHandlerRegistry(scope.ServiceProvider.GetServices<IGraphNodeHandlerInvoker>());
        var invoker = registry.GetInvoker("clean_generated");

        var graph = new GraphBuilder()
            .WithName("clean-generated-handler-test")
            .AddHandlerNode("clean", "Clean", "clean_generated")
            .Build();
        var context = new GraphContext("exec-clean-generated", graph, scope.ServiceProvider);
        context.SetCurrentNode("clean");

        var inputs = new HandlerInputs();
        inputs.Add("text", "clean");

        var result = await invoker!.ExecuteAsync(context, inputs);

        if (result is NodeExecutionResult.Failure failure)
        {
            throw new InvalidOperationException("Clean generated handler failed.", failure.Exception);
        }

        var success = result.Should().BeOfType<NodeExecutionResult.Success>().Subject;
        success.PortOutputs[0]["Result"].Should().Be("CLEAN");
        success.PortOutputs[0]["Length"].Should().Be(5);

        scope.ServiceProvider.GetRequiredService<IGeneratedHandlerCatalog>()
            .GetHandlers()["clean_generated"]
            .ContextType.Should().Be(typeof(GraphContext).FullName);
    }

    [Fact]
    public async Task GeneratedInvoker_DeserializesNodeConfigWithSourceGeneratedJsonMetadata()
    {
        GeneratedHandlerTestJsonContext.Default.GetTypeInfo(typeof(GeneratedConfigHandler.Config))
            .Should().NotBeNull();

        using var provider = new ServiceCollection()
            .AddSingleton<IGraphJsonTypeInfoResolverContributor>(
                new TestResolverContributor(GeneratedHandlerTestJsonContext.Default))
            .AddGeneratedGraphContextHandlers()
            .BuildServiceProvider();

        const string requestId = "11111111-2222-3333-4444-555555555555";
        const string since = "2026-05-07T12:34:56.0000000+00:00";
        using var configJson = System.Text.Json.JsonDocument.Parse(
            $$"""
            {
              "Prefix": "cfg",
              "Count": 7,
              "OptionalCount": 3,
              "RequestId": "{{requestId}}",
              "Since": "{{since}}",
              "Tags": ["alpha", "beta"],
              "Scores": [2, 5],
              "Mode": "Fast",
              "Complex": {
                "Name": "deep",
                "Enabled": true
              }
            }
            """);
        using var scope = provider.CreateScope();
        var registry = new GraphHandlerRegistry(scope.ServiceProvider.GetServices<IGraphNodeHandlerInvoker>());
        var invoker = registry.GetInvoker("generated_config");

        var graph = new GraphBuilder()
            .WithName("generated-config-handler-test")
            .AddHandlerNode("configured", "Configured", "generated_config", node => node.WithConfig(configJson.RootElement))
            .Build();
        var context = new GraphContext("exec-generated-config", graph, scope.ServiceProvider);
        context.SetCurrentNode("configured");

        var inputs = new HandlerInputs();
        inputs.Add("text", "value");

        var result = await invoker!.ExecuteAsync(context, inputs);

        if (result is NodeExecutionResult.Failure failure)
        {
            throw new InvalidOperationException("Generated config handler failed.", failure.Exception);
        }

        var success = result.Should().BeOfType<NodeExecutionResult.Success>().Subject;
        var expected = $"cfg:value:7:3:{requestId}:{since}:alpha,beta:7:Fast:deep:True";
        success.PortOutputs[0]["Result"].Should().Be(expected);
        success.PortOutputs[0]["Length"].Should().Be(expected.Length);
    }

    [Fact]
    public async Task GeneratedInvoker_ThrowsClearlyForUnsupportedConfigWithoutJsonTypeInfo()
    {
        using var provider = new ServiceCollection()
            .AddGeneratedGraphContextHandlers()
            .BuildServiceProvider();

        using var configJson = System.Text.Json.JsonDocument.Parse("""{"Prefix":"cfg","Complex":{"Name":"deep","Enabled":true}}""");
        using var scope = provider.CreateScope();
        var registry = new GraphHandlerRegistry(scope.ServiceProvider.GetServices<IGraphNodeHandlerInvoker>());
        var invoker = registry.GetInvoker("generated_config");

        var graph = new GraphBuilder()
            .WithName("generated-config-handler-test")
            .AddHandlerNode("configured", "Configured", "generated_config", node => node.WithConfig(configJson.RootElement))
            .Build();
        var context = new GraphContext("exec-generated-config", graph, scope.ServiceProvider);
        context.SetCurrentNode("configured");

        var inputs = new HandlerInputs();
        inputs.Add("text", "value");

        var result = await invoker!.ExecuteAsync(context, inputs);

        result.Should().BeOfType<NodeExecutionResult.Failure>()
            .Which.Exception.Message.Should().Contain("Register an HPD.Graph.Abstractions.Serialization.IGraphJsonTypeInfoResolverContributor");
    }

    [Fact]
    public void SocketBridgeGenerator_DoesNotTemplateReflectionStyleConfigDeserialization()
    {
        var generatorFile = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "HPD-Graph.SourceGenerator",
            "SocketBridgeGenerator.cs"));

        File.Exists(generatorFile).Should().BeTrue($"source generator should exist at {generatorFile}");
        var source = File.ReadAllText(generatorFile);

        source.Should().NotContain("JsonSerializer.Deserialize<");
        source.Should().NotContain("DefaultJsonTypeInfoResolver");
        source.Should().NotContain("GetRawText()");
        source.Should().Contain("TryGetProperty");
        source.Should().Contain("GetGuid()");
        source.Should().Contain("GetDateTimeOffset()");
        source.Should().Contain("EnumerateArray()");
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

    private sealed class TestResolverContributor(IJsonTypeInfoResolver resolver) : IGraphJsonTypeInfoResolverContributor
    {
        public IJsonTypeInfoResolver Resolver { get; } = resolver;
    }
}

[JsonSourceGenerationOptions(System.Text.Json.JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(GeneratedConfigHandler.Config))]
internal sealed partial class GeneratedHandlerTestJsonContext : JsonSerializerContext;
