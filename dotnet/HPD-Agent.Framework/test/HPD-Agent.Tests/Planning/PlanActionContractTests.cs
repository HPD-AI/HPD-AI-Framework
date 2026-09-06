using System.Text.Json;
using HPD.Agent.Planning;
using Xunit;

namespace HPD.Agent.Tests.Planning;

public class PlanActionContractTests
{
    private static HPDAIFunctionFactory.HPDAIFunction Function()
    {
        var factory = HPD.Agent.Generated.ToolHarnessRegistry.All.Single(x => x.ToolHarnessType == typeof(AgentPlanToolHarness));
        return Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(Assert.Single(factory.CreateFunctions(new AgentPlanToolHarness(), null, null)));
    }

    [Fact]
    public void GeneratedContractExposesOneToolAndFiveActions()
    {
        var function = Function();
        Assert.Equal("plan", function.Name);
        Assert.Equal(new[] { "addNote", "addStep", "complete", "create", "updateStep" }, function.OperationContract!.Actions.Keys.Order().ToArray());
        Assert.Equal(5, function.JsonSchema.GetProperty("properties").GetProperty("operation").GetProperty("oneOf").GetArrayLength());
        Assert.DoesNotContain("create_plan", function.JsonSchema.GetRawText());
    }

    [Theory]
    [InlineData("{\"action\":\"create\",\"goal\":\"Verify\",\"steps\":[\"Inspect\",\"Test\"]}", typeof(CreatePlanAction))]
    [InlineData("{\"action\":\"updateStep\",\"stepId\":\"1\",\"status\":\"Completed\",\"notes\":\"Passed\"}", typeof(UpdatePlanStepAction))]
    [InlineData("{\"action\":\"addStep\",\"description\":\"Review\",\"afterStepId\":\"1\"}", typeof(AddPlanStepAction))]
    [InlineData("{\"action\":\"addNote\",\"note\":\"Discovered constraint\"}", typeof(AddPlanNoteAction))]
    [InlineData("{\"action\":\"complete\"}", typeof(CompletePlanAction))]
    public void EveryActionBindsAndRoundTrips(string json, Type type)
    {
        using var input = JsonDocument.Parse("{\"operation\":" + json + "}");
        var bound = Function().ArgumentBinder!(input.RootElement);
        Assert.Empty(bound.Errors);
        Assert.NotNull(bound.Value);
        var action = JsonSerializer.Deserialize(json, PlanActionJsonContext.Default.PlanAction)!;
        Assert.IsType(type, action);
        var encoded = JsonSerializer.Serialize(action, PlanActionJsonContext.Default.PlanAction);
        Assert.Equal(action.GetType(), JsonSerializer.Deserialize(encoded, PlanActionJsonContext.Default.PlanAction)!.GetType());
    }

    [Fact]
    public async Task UnifiedToolExecutesAllActionsAndPublishesTheSameDurablePlanEvents()
    {
        var client = new HPD.Agent.Tests.Infrastructure.FakeChatClient();
        foreach (var json in new[]
        {
            "{\"action\":\"create\",\"goal\":\"Verify migration\",\"steps\":[\"Inspect\"]}",
            "{\"action\":\"addStep\",\"description\":\"Test\"}",
            "{\"action\":\"addNote\",\"note\":\"Preserve behavior\"}",
            "{\"action\":\"complete\"}",
            "{\"action\":\"updateStep\",\"stepId\":\"1\",\"status\":\"Completed\"}",
            "{\"action\":\"updateStep\",\"stepId\":\"2\",\"status\":\"Completed\"}",
            "{\"action\":\"complete\"}"
        })
        {
            using var document = JsonDocument.Parse(json);
            client.EnqueueToolCall("plan", Guid.NewGuid().ToString("N"), new() { ["operation"] = document.RootElement.Clone() });
        }
        client.EnqueueTextResponse("Verified.");
        await using var agent = await new AgentBuilder(new AgentConfig { Name = "plan-test" })
            .WithEventComposition(HPD.Agent.Serialization.CoreAgentEventComposition.Instance)
            .WithChatClient(new IdentifiedClient(client)).WithPlanMode().BuildAsync();
        await agent.CreateSessionAsync("plan-session");
        await agent.RunAsync(new UserMessagesInputEvent { Messages = [new(Microsoft.Extensions.AI.ChatRole.User, "Verify migration")], SessionId = "plan-session", ThreadId = "main" });
        var events = (await agent.Config.SessionStore!.CollectThreadEventsAsync(new("plan-session", "main")))!;
        var plans = events.OfType<PlanUpdatedEvent>().ToArray();
        Assert.Equal(6, plans.Length);
        Assert.True(plans[^1].Plan.IsComplete);
        Assert.Equal(2, plans[^1].Plan.Steps.Count);
        Assert.Single(plans[^1].Plan.ContextNotes);
        Assert.All(plans, evt => Assert.True(evt.ThreadSequenceNumber > 0));
        Assert.All(client.CapturedRequestSnapshots, request =>
        {
            Assert.Contains("plan", request.ToolNames);
            Assert.DoesNotContain("create_plan", request.ToolNames);
        });
    }

    private sealed class IdentifiedClient(HPD.Agent.Tests.Infrastructure.FakeChatClient inner) : Microsoft.Extensions.AI.DelegatingChatClient(inner)
    {
        public override object? GetService(Type type, object? key = null)
            => type == typeof(HPD.Agent.Providers.ProviderClientExecutionIdentity)
                ? HPD.Agent.Providers.ProviderClientExecutionIdentity.CreateSafe("test", "test",
                    HPD.Agent.Providers.ProviderClientFamily.Chat, "fake", "test/chat", "test/final")
                : base.GetService(type, key);
    }

    [Theory]
    [InlineData("{\"action\":\"unknown\"}")]
    [InlineData("{\"action\":\"create\",\"goal\":\"Verify\"}")]
    [InlineData("{\"action\":\"updateStep\",\"stepId\":\"1\",\"status\":\"invalid\"}")]
    public void InvalidActionsAndMissingRequiredArgumentsAreRejected(string json)
    {
        using var input = JsonDocument.Parse("{\"operation\":" + json + "}");
        Assert.NotEmpty(Function().ArgumentBinder!(input.RootElement).Errors);
    }
}
