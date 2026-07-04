using HPD.Agent.Tests.Infrastructure;
using HPD.Agent.Tests.TestToolHarnesses;
using HPD.MultiAgent;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Tools;

public class AgentBuilderWithToolTests
{
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
    }

    [Fact]
    public void ReflectionToolFactory_CreatesSkillCapability()
    {
        Assert.True(ReflectionToolFactory.TryCreateToolHarnessFactory(
            typeof(ReflectionAdvancedToolHarness),
            out var factory,
            out var error));

        Assert.Null(error);

        var functions = factory.CreateFunctions(new ReflectionAdvancedToolHarness(), null, null);
        var skill = Assert.Single(functions, f => f.Name == "order_support");

        Assert.Equal("Order support flow. References 2 functions: ReflectionAdvancedToolHarness.advanced_lookup_order, ReflectionAdvancedToolHarness.advanced_get_return_policy", skill.Description);
        Assert.True((bool)skill.AdditionalProperties!["IsSkill"]!);
        Assert.True((bool)skill.AdditionalProperties!["IsContainer"]!);
    }

    [Fact]
    public void ReflectionToolFactory_CreatesSubAgentCapability()
    {
        Assert.True(ReflectionToolFactory.TryCreateToolHarnessFactory(
            typeof(ReflectionAdvancedToolHarness),
            out var factory,
            out var error));

        Assert.Null(error);

        var functions = factory.CreateFunctions(new ReflectionAdvancedToolHarness(), null, null);
        var subAgent = Assert.Single(functions, f => f.Name == "support_escalation");

        Assert.Equal("Escalates support questions to a specialist.", subAgent.Description);
        Assert.True((bool)subAgent.AdditionalProperties!["IsSubAgent"]!);
        Assert.False((bool)subAgent.AdditionalProperties!["IsContainer"]!);
    }

    [Fact]
    public async Task GeneratedSubAgent_AttachesHierarchicalAgentMetadata()
    {
        var client = new FakeChatClient();
        client.EnqueueToolCall(
            "support_escalation",
            "call-subagent",
            new Dictionary<string, object?> { ["input"] = "help with this order" });
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
            .WithToolHarness<ReflectionAdvancedToolHarness>()
            .BuildAsync(CancellationToken.None);
        var events = new List<AgentEvent>();

        using var subscription = agent.SubscribeAny(evt =>
        {
            events.Add(evt);
            return ValueTask.CompletedTask;
        });

        await agent.RunAsync(new UserMessagesInputEvent([new ChatMessage(ChatRole.User, "Please escalate this.")]), CancellationToken.None);

        var childText = Assert.Single(events.OfType<TextDeltaEvent>(),
            evt => evt.Text == "child handled escalation");

        Assert.NotNull(childText.Metadata);
        Assert.Equal("support_escalation", childText.Metadata.AgentName);
        Assert.Equal(agent.AgentId, childText.Metadata.ParentAgentId);
        Assert.Equal(1, childText.Metadata.Depth);
        Assert.Equal(["Support Parent", "support_escalation"], childText.Metadata.AgentChain);
    }

    [Fact]
    public async Task ReflectionFallbackSubAgent_AttachesHierarchicalAgentMetadata()
    {
        Assert.True(ReflectionToolFactory.TryCreateToolHarnessFactory(
            typeof(ReflectionAdvancedToolHarness),
            out var factory,
            out var error));

        Assert.Null(error);

        var subAgentFunction = Assert.Single(
            factory.CreateFunctions(new ReflectionAdvancedToolHarness(), null, null),
            f => f.Name == "support_escalation");
        var client = new FakeChatClient();
        client.EnqueueToolCall(
            "support_escalation",
            "call-subagent",
            new Dictionary<string, object?> { ["input"] = "help with this order" });
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
                    ModelName = "test-model",
                    DefaultMicrosoftChatOptions = new ChatOptions
                    {
                        Tools = [subAgentFunction]
                    }
                }
            },
            AgenticLoop = new AgenticLoopConfig
            {
                MaxTurnDuration = TimeSpan.FromSeconds(20)
            }
        };

        var agent = await new AgentBuilder(config, new TestProviderRegistry(client))
            .BuildAsync(CancellationToken.None);
        var events = new List<AgentEvent>();

        using var subscription = agent.SubscribeAny(evt =>
        {
            events.Add(evt);
            return ValueTask.CompletedTask;
        });

        await agent.RunAsync(new UserMessagesInputEvent([new ChatMessage(ChatRole.User, "Please escalate this.")]), CancellationToken.None);

        var childText = Assert.Single(events.OfType<TextDeltaEvent>(),
            evt => evt.Text == "child handled escalation");

        Assert.NotNull(childText.Metadata);
        Assert.Equal("support_escalation", childText.Metadata.AgentName);
        Assert.Equal(agent.AgentId, childText.Metadata.ParentAgentId);
        Assert.Equal(1, childText.Metadata.Depth);
        Assert.Equal(["Support Parent", "support_escalation"], childText.Metadata.AgentChain);
    }

    [Fact]
    public void ReflectionToolFactory_CreatesMultiAgentCapability()
    {
        Assert.True(ReflectionToolFactory.TryCreateToolHarnessFactory(
            typeof(ReflectionAdvancedToolHarness),
            out var factory,
            out var error));

        Assert.Null(error);

        var functions = factory.CreateFunctions(new ReflectionAdvancedToolHarness(), null, null);
        var workflow = Assert.Single(functions, f => f.Name == "support_workflow");

        Assert.Equal("Runs a support workflow.", workflow.Description);
        Assert.True((bool)workflow.AdditionalProperties!["IsMultiAgent"]!);
        Assert.False((bool)workflow.AdditionalProperties!["IsContainer"]!);
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
    SystemPrompt = "Use support tool results when available."
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

public class ReflectionAdvancedToolHarness
{
    [AIFunction(Name = "advanced_lookup_order")]
    public string AdvancedLookupOrder(string orderNumber) => $"Order {orderNumber} shipped.";

    [AIFunction(Name = "advanced_get_return_policy")]
    public string AdvancedGetReturnPolicy(string category) => $"{category} items can be returned.";

    [Skill]
    public static Skill OrderSupportSkill() => SkillFactory.Create(
        "order_support",
        "Order support flow",
        "Use advanced_lookup_order and advanced_get_return_policy together for order questions.",
        "When using this skill, answer only from tool results.",
        "ReflectionAdvancedToolHarness.advanced_lookup_order",
        "ReflectionAdvancedToolHarness.advanced_get_return_policy");

    [SubAgent]
    public static SubAgent EscalationAgent() => SubAgent.FromConfig(
        "support_escalation",
        "Escalates support questions to a specialist.",
        new AgentConfig
        {
            Name = "Support Escalation",
            SystemInstructions = "You are a support escalation specialist."
        },
        SubAgentExecutionPolicies.NewSession());

    [MultiAgent("Runs a support workflow.", Name = "support_workflow", StreamEvents = false)]
    public static AgentWorkflowInstance SupportWorkflow()
    {
        throw new NotSupportedException("The reflection test only validates function creation.");
    }
}
