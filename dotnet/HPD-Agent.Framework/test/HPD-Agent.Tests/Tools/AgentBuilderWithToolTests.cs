using HPD.Agent.Tests.Infrastructure;
using HPD.Agent.Tests.TestToolHarnesses;
using HPD.Agent.Middleware;
using HPD.MultiAgent;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Tools;

public class AgentBuilderWithToolTests
{
    [Fact]
    public void ContainerProjection_RetainsContainerAndOnlySelectedQualifiedChildren()
    {
        Assert.True(ReflectionToolFactory.TryCreateToolHarnessFactory(
            typeof(ReflectionSupportToolHarness), out var factory, out _));
        var functions = factory.CreateFunctions(new ReflectionSupportToolHarness(), null, null);

        var projected = ContainerFunctionProjection.Project(
            functions,
            function => function.Name == "lookup_order");

        Assert.Contains(projected, function => function.Name == "lookup_order");
        var container = Assert.Single(projected, function => function.Name == nameof(ReflectionSupportToolHarness));
        Assert.Equal(new[] { "lookup_order" }, (string[])container.AdditionalProperties["ChildFunctions"]!);
        Assert.Contains("Support tools for orders and returns.", container.Description);
        Assert.Contains("lookup_order", container.Description);
    }

    [Fact]
    public void ContainerProjection_RemovesContainerWhenNoChildrenSurvive()
    {
        Assert.True(ReflectionToolFactory.TryCreateToolHarnessFactory(
            typeof(ReflectionSupportToolHarness), out var factory, out _));
        var functions = factory.CreateFunctions(new ReflectionSupportToolHarness(), null, null);

        var projected = ContainerFunctionProjection.Project(functions, _ => false);

        Assert.Empty(projected);
    }

    [Fact]
    public void SubAgentAvailabilityProjection_PreservesDynamicallyAddedTools()
    {
        AIFunction staticFunction = AIFunctionFactory.Create(
            () => "static",
            "static_tool");
        AIFunction dynamicFunction = AIFunctionFactory.Create(
            () => "dynamic",
            "dynamic_client_tool");
        var middleware = new SubAgentAvailabilityMiddleware(
            [staticFunction]);

        IList<AITool> projected =
            middleware.ProjectAvailableTools(
                [staticFunction, dynamicFunction],
                currentDepth: 0,
                maximumDepth: 4);

        Assert.Contains(
            projected,
            tool => tool.Name == "static_tool");
        Assert.Contains(
            projected,
            tool => tool.Name == "dynamic_client_tool");
    }

    [Fact]
    public void ConfigFunctionSelection_AppliesWhenHarnessWasAlreadyAddedByBuilder()
    {
        var config = new AgentConfig
        {
            ToolHarnesses =
            [
                new ToolHarnessReference
                {
                    Name = nameof(NamedWeatherToolHarness),
                    Functions = ["get_forecast"]
                }
            ]
        };
        var builder = new AgentBuilder(config)
            .WithToolHarness<NamedWeatherToolHarness>();

        typeof(AgentBuilder)
            .GetMethod("ResolveConfigToolHarnesses", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(builder, null);

        Assert.Equal(new[] { "get_forecast" }, builder._toolFunctionFilters[nameof(NamedWeatherToolHarness)]);
        Assert.Single(builder._selectedToolHarnessFactories.Where(factory => factory.Name == nameof(NamedWeatherToolHarness)));
    }

    [Fact]
    public void WithTool_Generic_RegistersSingleGeneratedFunction()
    {
        var builder = new AgentBuilder()
            .WithTool<NamedWeatherToolHarness>("get_weather");

        Assert.True(builder._toolFunctionFilters.TryGetValue(nameof(NamedWeatherToolHarness), out var filters));
        Assert.Equal(new[] { "get_weather" }, filters);
        Assert.Contains(builder._selectedToolHarnessFactories, f => f.Name == nameof(NamedWeatherToolHarness));
    }

    [Fact]
    public void WithTool_Generic_AcceptsQualifiedReferenceForSameToolHarness()
    {
        var builder = new AgentBuilder()
            .WithTool<NamedWeatherToolHarness>("NamedWeatherToolHarness.get_weather");

        Assert.True(builder._toolFunctionFilters.TryGetValue(nameof(NamedWeatherToolHarness), out var filters));
        Assert.Equal(new[] { "get_weather" }, filters);
    }

    [Fact]
    public void WithTool_QualifiedReference_RegistersSingleGeneratedFunction()
    {
        var builder = new AgentBuilder()
            .WithTool("NamedWeatherToolHarness.get_weather");

        Assert.True(builder._toolFunctionFilters.TryGetValue(nameof(NamedWeatherToolHarness), out var filters));
        Assert.Equal(new[] { "get_weather" }, filters);
    }

    [Fact]
    public void WithTool_MultipleCallsForSameToolHarness_MergesFunctionFilters()
    {
        var builder = new AgentBuilder()
            .WithTool<NamedWeatherToolHarness>("get_weather")
            .WithTool<NamedWeatherToolHarness>("get_forecast");

        Assert.True(builder._toolFunctionFilters.TryGetValue(nameof(NamedWeatherToolHarness), out var filters));
        Assert.Equal(new[] { "get_weather", "get_forecast" }, filters);
        Assert.Single(builder._selectedToolHarnessFactories.Where(f => f.Name == nameof(NamedWeatherToolHarness)));
    }

    [Fact]
    public void WithTool_UnknownFunction_ThrowsHelpfulError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new AgentBuilder().WithTool<NamedWeatherToolHarness>("missing_tool"));

        Assert.Contains("Function 'missing_tool' was not found on toolharness 'NamedWeatherToolHarness'", ex.Message);
        Assert.Contains("get_weather", ex.Message);
        Assert.Contains("get_forecast", ex.Message);
    }

    [Fact]
    public void WithTool_GenericQualifiedReferenceForDifferentToolHarness_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new AgentBuilder().WithTool<NamedWeatherToolHarness>("OtherToolHarness.get_weather"));

        Assert.Contains("targets toolharness 'OtherToolHarness'", ex.Message);
    }

    [Fact]
    public void ReflectionToolFactory_CreatesFunctionFromAIFunctionAttributes()
    {
        Assert.True(ReflectionToolFactory.TryCreateToolHarnessFactory(
            typeof(ReflectionWeatherToolHarness),
            out var factory,
            out var error));

        Assert.Null(error);
        Assert.Equal(nameof(ReflectionWeatherToolHarness), factory.Name);
        Assert.Equal(new[] { "get_weather" }, factory.FunctionNames);

        var function = Assert.Single(factory.CreateFunctions(
            new ReflectionWeatherToolHarness(),
            null,
            null));

        Assert.Equal("get_weather", function.Name);
        Assert.Equal("Gets the current weather for a city.", function.Description);
        Assert.Equal("The city to get weather for.", function.JsonSchema.GetProperty("properties").GetProperty("city").GetProperty("description").GetString());
    }

    [Fact]
    public void WithToolHarness_Generic_UsesReflectionFallbackWhenGeneratedRegistryIsMissing()
    {
        var builder = new AgentBuilder()
            .WithToolHarness<ReflectionWeatherToolHarness>();

        Assert.Contains(builder._selectedToolHarnessFactories, f => f.Name == nameof(ReflectionWeatherToolHarness));
        Assert.DoesNotContain(nameof(ReflectionWeatherToolHarness), builder._toolFunctionFilters.Keys);
    }

    [Fact]
    public void ReflectionToolFactory_CapturesCollapseMetadata()
    {
        Assert.True(ReflectionToolFactory.TryCreateToolHarnessFactory(
            typeof(ReflectionSupportToolHarness),
            out var factory,
            out var error));

        Assert.Null(error);
        Assert.True(factory.HasDescription);
        Assert.Equal("Support tools for orders and returns.", factory.Description);
        Assert.Equal("Support tools are active.", factory.FunctionResult);
        Assert.Equal("Use support tool results when available.", factory.SystemPrompt);
        Assert.Equal(new[] { "lookup_order" }, factory.FunctionNames);

        var functions = factory.CreateFunctions(new ReflectionSupportToolHarness(), null, null);
        var container = Assert.Single(functions, function => function.Name == nameof(ReflectionSupportToolHarness));
        Assert.True((bool)container.AdditionalProperties!["IsContainer"]!);
        Assert.True((bool)container.AdditionalProperties["IsToolHarnessContainer"]!);
        Assert.Equal(new[] { "lookup_order" }, (string[])container.AdditionalProperties["ChildFunctions"]!);
        var middlewareFactory = Assert.Single(factory.CollapseMiddlewareFactories!);
        Assert.IsType<ReflectionScopedMiddleware>(middlewareFactory());
    }

    [Fact]
    public void GeneratedFactory_CreatesSkillCapabilityWithoutDiscoveryLeakage()
    {
        var factory = GetAdvancedFactory();
        var functions = factory.CreateFunctions(new ReflectionAdvancedToolHarness(), null, null);
        var skill = Assert.Single(functions, f => f.Name == "order_support");

        Assert.Equal("Order support flow", skill.Description);
        var metadata = Assert.IsType<HPDCapabilityMetadata>(
            skill.AdditionalProperties![HPDCapabilityMetadata.AdditionalPropertiesKey]);
        Assert.Equal(HPDCapabilityKind.SkillActivation, metadata.Kind);
        Assert.Equal(2, metadata.Reveals.Length);
    }

    [Fact]
    public void GeneratedFactory_CreatesSubAgentCapability()
    {
        var factory = GetAdvancedFactory();
        var functions = factory.CreateFunctions(new ReflectionAdvancedToolHarness(), null, null);
        var subAgent = Assert.Single(functions, f => f.Name == "support_escalation");

        Assert.Equal("Escalates support questions to a specialist.", subAgent.Description);
        Assert.True((bool)subAgent.AdditionalProperties!["IsSubAgent"]!);
    }

    [Fact]
    public async Task GeneratedSubAgent_PublishesTypedInvocationLifecycle()
    {
        var client = new FakeChatClient();
        client.EnqueueToolCall(
            "support_escalation",
            "call-subagent",
            new Dictionary<string, object?>
            {
                ["taskName"] = "order_escalation",
                ["input"] = "help with this order"
            });
        client.EnqueueTextResponse("child handled escalation");
        client.EnqueueTextResponse("parent saw escalation");

        var config = new AgentConfig
        {
            Name = "Support Parent",
            MaxAgenticIterations = 5,
            Clients = new AgentClientConfig
            {
                Chat = new ClientProviderConfig
                {
                    ProviderKey = "test",
                    ModelName = "test-model"
                }
            },
            AgenticLoop = new AgenticLoopConfig
            {
                MaxTurnDuration = TimeSpan.FromSeconds(20)
            }
        };

        var agent = await new AgentBuilder(config, new TestProviderRegistry(client))
            .WithAgentStore(new InMemoryAgentStore())
            .WithToolHarness<ReflectionAdvancedToolHarness>()
            .BuildAsync(CancellationToken.None);
        await agent.CreateSessionAsync("generated-lifecycle");
        await agent.RunAsync(new UserMessagesInputEvent
        {
            Messages = [new ChatMessage(ChatRole.User, "Please escalate this.")],
            SessionId = "generated-lifecycle",
            ThreadId = "main"
        }, CancellationToken.None);

        var parentEvents = await agent.Config.SessionStore!.CollectThreadEventsAsync("generated-lifecycle", "main");
        var invocation = Assert.Single(parentEvents!.OfType<SubAgentInvocationStartedEvent>());
        Assert.Equal("test/support-escalation", invocation.ChildAgentId);
        Assert.Equal("support_escalation", invocation.RoleName);
        Assert.Equal("order_escalation", invocation.TaskName);
        Assert.Equal(SubAgentContextPolicy.Isolated, invocation.ContextPolicy);
        Assert.Equal(AgentInvocationMode.Synchronous, invocation.Mode);
    }

    [Fact]
    public void GeneratedFactory_CreatesMultiAgentCapability()
    {
        var factory = GetAdvancedFactory();
        var functions = factory.CreateFunctions(new ReflectionAdvancedToolHarness(), null, null);
        var workflow = Assert.Single(functions, f => f.Name == "support_workflow");

        Assert.Equal("Runs a support workflow.", workflow.Description);
        Assert.True((bool)workflow.AdditionalProperties!["IsMultiAgent"]!);
        Assert.False((bool)workflow.AdditionalProperties!["IsContainer"]!);
    }

    [Fact]
    public void GeneratedFactory_PreservesMixedCapabilitiesWithoutNameCollisions()
    {
        var functions = GetAdvancedFactory()
            .CreateFunctions(new ReflectionAdvancedToolHarness(), null, null);

        Assert.Single(functions, function => function.Name == "advanced_lookup_order");
        Assert.Single(functions, function => function.Name == "advanced_get_return_policy");
        Assert.Single(functions, function => function.Name == "order_support");
        Assert.Single(functions, function => function.Name == "support_escalation");
        Assert.Single(functions, function => function.Name == "support_workflow");
        Assert.Equal(functions.Count, functions.Select(function => function.Name).Distinct(StringComparer.Ordinal).Count());

        var kinds = functions
            .Select(function => function.AdditionalProperties?.TryGetValue(
                HPDCapabilityMetadata.AdditionalPropertiesKey, out var value) == true
                    ? value as HPDCapabilityMetadata
                    : null)
            .Where(metadata => metadata is not null)
            .Select(metadata => metadata!.Kind)
            .ToHashSet();

        Assert.Contains(HPDCapabilityKind.Function, kinds);
        Assert.Contains(HPDCapabilityKind.SkillActivation, kinds);
        Assert.Contains(HPDCapabilityKind.SubAgent, kinds);
        Assert.Contains(HPDCapabilityKind.MultiAgent, kinds);
    }

    private static ToolHarnessFactory GetAdvancedFactory()
    {
        var builder = new AgentBuilder().WithToolHarness<ReflectionAdvancedToolHarness>();
        return Assert.Single(
            builder._selectedToolHarnessFactories,
            factory => factory.Name == nameof(ReflectionAdvancedToolHarness));
    }
}

public class ReflectionWeatherToolHarness
{
    [AIFunction(Name = "get_weather", Description = "Gets the current weather for a city.")]
    public string ReflectWeather([AIDescription("The city to get weather for.")] string city)
    {
        return $"It is sunny and 72 F in {city}.";
    }
}

[Collapse(
    "Support tools for orders and returns.",
    FunctionResult = "Support tools are active.",
    SystemPrompt = "Use support tool results when available.",
    Middlewares = [typeof(ReflectionScopedMiddleware)]
)]
public class ReflectionSupportToolHarness
{
    [AIFunction(Name = "lookup_order")]
    [AIDescription("Looks up an order by order number.")]
    public string LookupOrder([AIDescription("The order number to look up.")] string orderNumber)
    {
        return $"Order {orderNumber} shipped.";
    }
}

public sealed class ReflectionScopedMiddleware : IToolHarnessMiddleware
{
}

public class ReflectionAdvancedToolHarness
{
    [AIFunction(Name = "advanced_lookup_order")]
    public string AdvancedLookupOrder(string orderNumber) => $"Order {orderNumber} shipped.";

    [AIFunction(Name = "advanced_get_return_policy")]
    public string AdvancedGetReturnPolicy(string category) => $"{category} items can be returned.";

    [Skill]
    public static Skill OrderSupportSkill() => Skill.Create(
        name: "order_support",
        description: "Order support flow",
        instructions: SkillInstructions.FromText(
            "Use advanced_lookup_order and advanced_get_return_policy together for order questions."),
        reinforcement: SkillInstructions.FromText(
            "When using this skill, answer only from tool results."),
        capabilities:
        [
            SkillCapabilities.Function<ReflectionAdvancedToolHarness>(nameof(AdvancedLookupOrder)),
            SkillCapabilities.Function<ReflectionAdvancedToolHarness>(nameof(AdvancedGetReturnPolicy))
        ]);

    [SubAgent]
    public static SubAgent EscalationAgent() => SubAgent.FromConfig(
        "test/support-escalation",
        "support_escalation",
        "Escalates support questions to a specialist.",
        new AgentConfig
        {
            Name = "Support Escalation",
            SystemInstructions = "You are a support escalation specialist."
        },
        SubAgentContextPolicy.Isolated);

    [MultiAgent("Runs a support workflow.", Name = "support_workflow", StreamEvents = false)]
    public static AgentWorkflowInstance SupportWorkflow()
    {
        throw new NotSupportedException("The reflection test only validates function creation.");
    }
}
