using HPD.Agent.Tests.Infrastructure;
using HPD.MultiAgent;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.SubAgents;

public sealed class GeneratedAgentCapabilityInvocationTests
{
    [Fact]
    public async Task ParentModelTurn_InvokesGeneratedShapeSubAgentAndMultiAgentTools()
    {
        var parentClient = new FakeChatClient();
        parentClient.EnqueueToolCall(
            "GeneratedReviewer",
            "call-subagent",
            new Dictionary<string, object?> { ["input"] = "review this fixture" });
        parentClient.EnqueueToolCall(
            "GeneratedWorkflow",
            "call-multiagent",
            new Dictionary<string, object?> { ["input"] = "coordinate this fixture" });
        parentClient.EnqueueTextResponse("generated workflow child completed");
        parentClient.EnqueueTextResponse("generated capabilities completed");

        var subAgentFunction = CreateGeneratedShapeSubAgentFunction();
        var multiAgentFunction = CreateGeneratedShapeMultiAgentFunction();
        var config = DefaultConfig();
        config.EnsureChatClientConfig().DefaultMicrosoftChatOptions = new ChatOptions
        {
            Tools = [subAgentFunction, multiAgentFunction],
        };

        var agent = await new AgentBuilder(config, new TestProviderRegistry(parentClient))
            .BuildAsync(CancellationToken.None);
        var events = new System.Collections.Concurrent.ConcurrentQueue<AgentEvent>();

        using var subscription = agent.SubscribeAny(evt =>
        {
            events.Enqueue(evt);
            return ValueTask.CompletedTask;
        });

        await agent.RunAsync(new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, "Use the generated capabilities.")] }, CancellationToken.None);

        var toolResults = events.OfType<ToolCallResultEvent>().ToArray();
        Assert.Contains(toolResults, result =>
            result.CallId == "call-subagent" &&
            result.Name == "GeneratedReviewer" &&
            result.Result.Text == "generated subagent saw: review this fixture");
        Assert.Contains(toolResults, result =>
            result.CallId == "call-multiagent" &&
            result.Name == "GeneratedWorkflow" &&
            result.Result.Text == "generated workflow child completed");
        Assert.Contains(events.OfType<TextDeltaEvent>(), delta =>
            delta.Text.Contains("generated capabilities completed", StringComparison.Ordinal));
    }

    private static AIFunction CreateGeneratedShapeSubAgentFunction() =>
        HPDAIFunctionFactory.Create(
            static (arguments, _, cancellationToken) =>
            {
                var input = ReadArgument(arguments, "input");
                return Task.FromResult<object?>($"generated subagent saw: {input}");
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = "GeneratedReviewer",
                Description = "Generated-shape thread-native subagent wrapper",
                RequiresPermission = true,
                AdditionalProperties = new Dictionary<string, object>
                {
                    ["IsSubAgent"] = true,
                    ["ExecutionModel"] = "ThreadNative",
                    ["ParentToolHarness"] = "GeneratedAgentHarness",
                },
            });

    private static AIFunction CreateGeneratedShapeMultiAgentFunction() =>
        HPDAIFunctionFactory.Create(
            static async (arguments, functionContext, cancellationToken) =>
            {
                var input = ReadArgument(arguments, "input");
                var childClient = new FakeChatClient();
                childClient.EnqueueTextResponse("generated workflow child completed");
                var childAgent = TestAgentFactory.Create(null, childClient);
                var workflow = await AgentWorkflow.Create()
                    .WithName("GeneratedWorkflow")
                    .AddAgent("child", childAgent)
                    .From("START").To("child")
                    .From("child").To("END")
                    .BuildAsync();

                var text = new System.Text.StringBuilder();
                await foreach (var evt in workflow.ExecuteStreamingAsync(
                                   input,
                                   functionContext?.GetParentEventCoordinator(),
                                   functionContext?.GetParentAgentMetadata(),
                                   functionContext?.GetParentChatClient(),
                                   cancellationToken))
                {
                    if (evt is TextDeltaEvent delta)
                        text.Append(delta.Text);
                }

                return text.ToString();
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = "GeneratedWorkflow",
                Description = "Generated-shape multi-agent workflow wrapper",
                RequiresPermission = true,
                AdditionalProperties = new Dictionary<string, object>
                {
                    ["CapabilityType"] = "MultiAgent",
                    ["IsMultiAgent"] = true,
                    ["IsContainer"] = false,
                    ["ParentToolHarness"] = "GeneratedAgentHarness",
                    ["StreamEvents"] = true,
                    ["TimeoutSeconds"] = 300,
                },
            });

    private static string ReadArgument(AIFunctionArguments arguments, string name)
    {
        foreach (var (key, value) in arguments)
        {
            if (string.Equals(key, name, StringComparison.Ordinal))
                return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return string.Empty;
    }

    private static AgentConfig DefaultConfig() => new()
    {
        Name = "GeneratedCapabilityParent",
        MaxAgenticIterations = 10,
        Clients = new AgentClientConfig
        {
            Chat = new ClientProviderConfig
            {
                ProviderKey = "test",
                ModelName = "test-model",
            },
        },
        AgenticLoop = new AgenticLoopConfig
        {
            MaxTurnDuration = TimeSpan.FromSeconds(20),
        },
    };
}
