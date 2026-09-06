using System.Collections.Immutable;

namespace HPD.Agent.Goals;

internal sealed record GoalActionResult(GoalPersistentState State, AgentEvent Event, string Feedback);

internal static class GoalActionTransition
{
    internal static GoalActionResult Apply(GoalPersistentState state, GoalToolMutation mutation,
        GoalConfig config, string executionId, DateTimeOffset now)
    {
        if (state.Current?.GoalId != mutation.GoalId || state.Current?.Revision != mutation.Revision)
            throw new InvalidOperationException("goal_revision_conflict");
        if (mutation.Action is CreateGoalAction create)
        {
            if (!config.AllowModelCreatedGoals) throw new InvalidOperationException("goal_model_creation_disabled");
            var created = GoalTransitions.Create(state, create.Objective, config, Guid.NewGuid().ToString("N"), now);
            return new(created, new GoalStartedEvent(created.Current!, "user_requested"), "Goal created. Work toward the complete objective.");
        }
        var goal = state.Current ?? throw new InvalidOperationException("goal_missing");
        GoalTransitions.Validate(goal);
        GoalData updated;
        AgentEvent evt;
        string feedback;
        switch (mutation.Action)
        {
            case PauseGoalAction:
                updated = GoalTransitions.ChangeStatus(goal, GoalStatus.Paused, now);
                evt = new GoalPausedEvent(updated, "user_requested");
                feedback = "Goal paused.";
                break;
            case ResumeGoalAction:
                updated = GoalTransitions.ChangeStatus(goal, GoalStatus.Active, now);
                evt = new GoalResumedEvent(updated, "user_requested");
                feedback = "Goal resumed. The blocker audit starts afresh.";
                break;
            case EditGoalAction edit:
                updated = GoalTransitions.Edit(goal, edit.Objective, config.MaximumObjectiveLength, now);
                evt = new GoalEditedEvent(updated, "user_requested");
                feedback = "Goal objective updated; previous continuation invalidated.";
                break;
            case ClearGoalAction:
                updated = goal with { Revision = checked(goal.Revision + 1), Continuation = null, CompletionProposal = null, UpdatedAt = now };
                var cleared = GoalAccountingTransitions.PreserveAttribution(state with { Current = updated }) with { Current = null };
                return new(cleared, new GoalClearedEvent(updated, "user_requested"), "Goal cleared without claiming completion.");
            case ProposeGoalCompletionAction completion:
                updated = GoalTransitions.Propose(goal, new(completion.Summary,
                    completion.Evidence?.ToImmutableArray() ?? [], now, executionId)
                    { RemainingWork = completion.RemainingWork?.ToImmutableArray() ?? [] }, now);
                evt = new GoalCompletionProposedEvent(updated, "verification_pending");
                feedback = "Completion proposed. Success remains pending policy verification and successful execution closure.";
                break;
            case ReportGoalBlockerAction blocker:
                updated = GoalTransitions.ReportBlocker(goal, blocker, executionId,
                    checked(goal.Accounting.ExecutionCount + (goal.Accounting.LastAccountedExecutionId == executionId ? 0 : 1)), now);
                evt = new GoalBlockerReportedEvent(updated, "verification_pending");
                feedback = $"Blocker recorded for {updated.Blocker!.ConsecutiveExecutions} consecutive executions. Policy will verify it; explore remaining alternatives.";
                break;
            default:
                throw new InvalidOperationException("goal_action_invalid");
        }
        return new(GoalAccountingTransitions.PreserveAttribution(state with { Current = updated }), evt, feedback);
    }
}
