using HPD.Agent.Goals;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;
using HPD.Events.Core;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests;

public class GoalInputTests
{
    [Fact]
    public async Task CreationUsesOneUserMessageAndPreservesExactSemanticSource()
    {
        using var coordinator = new EventCoordinator();
        var input = new CreateGoalInputEvent
        {
            Objective = "Verify every requirement", SessionId = "s1", ThreadId = "t1",
            ThreadExecutionId = "e1", RunConfig = new() { Goals = new() { ToolAccess = GoalToolAccess.ReadOnly } }
        };
        var called = false;
        var context = new AgentInputHandlingContext
        {
            AgentName = "test", Config = new() { Goals = new() }, EventCoordinator = coordinator,
            TryResolveClientToolOperation = _ => false,
            RunMessagesAsync = (source, messages, _, _, _) =>
            {
                called = true;
                Assert.Same(input, source);
                var message = Assert.Single(messages.Messages);
                Assert.Equal(ChatRole.User, message.Role);
                Assert.Equal(input.Objective, message.Text);
                Assert.Same(input.RunConfig, messages.RunConfig);
                Assert.Equal("e1", messages.ThreadExecutionId);
                return Task.FromResult(AgentTurnResult.Empty);
            }
        };
        await new CreateGoalInputHandler().HandleAsync(input, context, default);
        Assert.True(called);
        Assert.Equal(AgentInputRoutingClass.Work, AgentInputDispatcher.GetBuiltInRegistration(typeof(CreateGoalInputEvent)).RoutingClass);
        Assert.Equal(AgentInputRoutingClass.Work, AgentInputDispatcher.GetBuiltInRegistration(typeof(GoalContinuationInputEvent)).RoutingClass);
    }

    [Fact]
    public void CodecRoundTripsCreationAndInternalContinuationWithRunOverrides()
    {
        var codec = new AgentInputCodec(ProviderComposition.Create([]));
        var create = new CreateGoalInputEvent
        {
            Objective = "Outcome", SessionId = "s1", ThreadId = "t1",
            RunConfig = new() { Goals = new() { ToolAccess = GoalToolAccess.ReadOnly } }
        };
        var decoded = Assert.IsType<CreateGoalInputEvent>(codec.Deserialize(codec.Serialize(create)));
        Assert.Equal(create.Objective, decoded.Objective);
        Assert.Equal(GoalToolAccess.ReadOnly, decoded.RunConfig!.Goals!.ToolAccess);
        var continuation = new GoalContinuationInputEvent { GoalId = "g1", ExpectedRevision = 3, Generation = 2 };
        var internalDecoded = Assert.IsType<GoalContinuationInputEvent>(codec.Deserialize(codec.Serialize(continuation)));
        Assert.Equal(continuation, internalDecoded);
        Assert.False(typeof(GoalContinuationInputEvent).IsPublic);
        Assert.IsType<CreateGoalInputEvent>(codec.DeserializePublic(codec.Serialize(create)));
        Assert.Throws<System.Text.Json.JsonException>(() => codec.DeserializePublic(codec.Serialize(continuation)));
    }
}
