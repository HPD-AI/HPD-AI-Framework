using Microsoft.Extensions.AI;

namespace HPD.Agent.Goals;

/// <summary>Creates a user-authorized persistent Goal and runs its initial input.</summary>
public sealed record CreateGoalInputEvent : AgentInputEvent
{
    /// <summary>The complete user-authored outcome, including constraints and verification.</summary>
    public required string Objective { get; init; }
}

internal sealed record GoalContinuationInputEvent : AgentInputEvent
{
    public required string GoalId { get; init; }
    public required long ExpectedRevision { get; init; }
    public required long Generation { get; init; }
}

internal sealed class CreateGoalInputHandler : IAgentInputHandler<CreateGoalInputEvent>
{
    public async ValueTask<AgentInputResult> HandleAsync(CreateGoalInputEvent input,
        AgentInputHandlingContext context, CancellationToken cancellationToken)
    {
        if (context.Config.Goals is not { Enabled: true } config)
            throw new InvalidOperationException("goals_not_enabled");
        GoalTransitions.ValidateObjective(input.Objective, config.MaximumObjectiveLength);
        var turn = await context.RunMessagesAsync(input, Payload(input, [new(ChatRole.User, input.Objective)]),
            context.ActiveInput, context.EventCoordinator, cancellationToken).ConfigureAwait(false);
        return new AgentInputResult.Completed(turn, input.ThreadExecutionId);
    }

    internal static UserMessagesInputEvent Payload(AgentInputEvent input, IReadOnlyList<ChatMessage> messages) => new()
    {
        Messages = messages, SessionId = input.SessionId, ThreadId = input.ThreadId, AgentId = input.AgentId,
        ClientInputId = input.ClientInputId, ThreadExecutionId = input.ThreadExecutionId,
        RunConfig = input.RunConfig, SubAgentRunConfig = input.SubAgentRunConfig
    };
}

internal sealed class GoalContinuationInputHandler : IAgentInputHandler<GoalContinuationInputEvent>
{
    public async ValueTask<AgentInputResult> HandleAsync(GoalContinuationInputEvent input,
        AgentInputHandlingContext context, CancellationToken cancellationToken)
    {
        if (context.Config.Goals is not { Enabled: true }) throw new InvalidOperationException("goals_not_enabled");
        var store = context.Config.SessionStore ?? throw new InvalidOperationException("goal_store_required");
        var key = new ThreadKey(input.SessionId ?? throw new InvalidOperationException("goal_session_required"),
            input.ThreadId ?? throw new InvalidOperationException("goal_thread_required"));
        var publisher = new AgentEventPublisher(store, context.EventCoordinator);
        while (true)
        {
            var snapshot = await GoalPersistence.ReadAsync(store, key, cancellationToken).ConfigureAwait(false);
            var consumed = GoalTransitions.Consume(snapshot.Goal, input.GoalId, input.ExpectedRevision,
                input.Generation, DateTimeOffset.UtcNow);
            if (ReferenceEquals(consumed, snapshot.Goal))
                return new AgentInputResult.Completed(AgentTurnResult.Empty, input.ThreadExecutionId);
            try
            {
                await GoalPersistence.CommitAsync(publisher, key, snapshot, consumed,
                    new GoalContinuationStartedEvent(consumed.Current!, "reservation_consumed")
                    { ThreadExecutionId = input.ThreadExecutionId }, cancellationToken).ConfigureAwait(false);
                break;
            }
            catch (ThreadAppendConflictException) { }
        }
        var turn = await context.RunMessagesAsync(input, CreateGoalInputHandler.Payload(input, []),
            context.ActiveInput, context.EventCoordinator, cancellationToken).ConfigureAwait(false);
        return new AgentInputResult.Completed(turn, input.ThreadExecutionId);
    }
}
