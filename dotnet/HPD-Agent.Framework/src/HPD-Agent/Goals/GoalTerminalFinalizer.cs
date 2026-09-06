namespace HPD.Agent.Goals;

internal enum GoalExecutionClosure { Succeeded, Cancelled, RuntimeStopped, Interrupted, Failed, UsageLimited }

internal sealed record GoalTerminalResult(GoalPersistentState State, IReadOnlyList<AgentEvent> Events);

internal static class GoalTerminalFinalizer
{
    internal static async ValueTask<GoalTerminalResult> FinalizeAsync(GoalPersistentState state,
        string executionId, string messageTurnId, ThreadJournalCursor terminal,
        MessageTurnUsageSummary usage, TimeSpan elapsed, GoalExecutionClosure closure,
        GoalPolicyContext facts, EffectiveGoalPolicies policies, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var accounted = GoalAccountingTransitions.Close(state, executionId, messageTurnId, terminal,
            policies.Accounting.Project(usage), elapsed, now);
        if (!accounted.Applied) return new(state, []);
        var previousExecutionId = state.Current?.Accounting.LastAccountedExecutionId;
        state = accounted.State;
        var historical = accounted.AccountedGoal!;
        if (state.Current?.GoalId == historical.GoalId && historical.Status == GoalStatus.Active)
        {
            historical = historical with { ConsecutiveNoProgressExecutions = facts.HasProgress ? 0
                : previousExecutionId == executionId ? historical.ConsecutiveNoProgressExecutions
                : checked(historical.ConsecutiveNoProgressExecutions + 1) };
            state = state with { Current = historical };
        }
        var events = new List<AgentEvent> { new GoalProgressAccountedEvent(historical, "closed_provider_usage") };
        // Clearing or replacing a Goal retains historical usage but cannot make the new
        // objective inherit the old turn's completion, failure, or continuation decision.
        if (state.Current?.GoalId != historical.GoalId || state.Current.Status != GoalStatus.Active)
            return new(state, events);
        var goal = state.Current;
        if (closure != GoalExecutionClosure.Succeeded)
        {
            if (closure is GoalExecutionClosure.RuntimeStopped or GoalExecutionClosure.Interrupted)
            {
                goal = goal with { CompletionProposal = null, Continuation = null,
                    Revision = checked(goal.Revision + 1), UpdatedAt = now };
                events.Add(new GoalContinuationSkippedEvent(goal, closure == GoalExecutionClosure.RuntimeStopped ? "runtime_stopping" : "execution_interrupted"));
            }
            else
            {
                var status = closure == GoalExecutionClosure.Cancelled ? GoalStatus.Paused
                    : closure == GoalExecutionClosure.UsageLimited ? GoalStatus.UsageLimited : GoalStatus.Faulted;
                goal = GoalTransitions.ChangeStatus(goal, status, now);
                events.Add(status switch
                {
                    GoalStatus.Paused => new GoalPausedEvent(goal, "execution_cancelled"),
                    GoalStatus.UsageLimited => new GoalUsageLimitedEvent(goal, "external_usage_limit"),
                    _ => new GoalFaultedEvent(goal, "execution_failed")
                });
            }
            return new(state with { Current = goal }, events);
        }

        facts = facts with { Goal = goal, ExecutionId = executionId, Usage = usage,
            ConsecutiveNoProgressExecutions = goal.ConsecutiveNoProgressExecutions };
        if (goal.CompletionProposal is not null)
        {
            var completion = await policies.Completion.EvaluateAsync(facts, cancellationToken).ConfigureAwait(false);
            if (completion.Disposition is GoalPolicyDisposition.Completed or GoalPolicyDisposition.AwaitingInput)
            {
                var acceptedProposal = goal.CompletionProposal;
                goal = GoalTransitions.ChangeStatus(goal, completion.Disposition == GoalPolicyDisposition.Completed
                    ? GoalStatus.Completed : GoalStatus.AwaitingInput, now);
                events.Add(completion.Disposition == GoalPolicyDisposition.Completed
                    ? new GoalCompletedEvent(goal, completion.Reason) { AcceptedProposal = acceptedProposal } : new GoalAwaitingInputEvent(goal, completion.Reason));
                return new(state with { Current = goal }, events);
            }
            if (completion.Disposition is not (GoalPolicyDisposition.Rejected or GoalPolicyDisposition.Continue))
                throw new InvalidOperationException("goal_completion_policy_invalid");
            goal = goal with { CompletionProposal = null, Revision = checked(goal.Revision + 1), UpdatedAt = now };
            events.Add(new GoalCompletionRejectedEvent(goal, completion.Reason));
        }

        facts = facts with { Goal = goal };
        if (goal.Blocker?.LastExecutionId == executionId)
        {
            var blocker = await policies.Blocker.EvaluateAsync(facts, cancellationToken).ConfigureAwait(false);
            if (blocker.Disposition is GoalPolicyDisposition.Blocked or GoalPolicyDisposition.AwaitingInput)
            {
                goal = GoalTransitions.ChangeStatus(goal, blocker.Disposition == GoalPolicyDisposition.Blocked
                    ? GoalStatus.Blocked : GoalStatus.AwaitingInput, now);
                events.Add(blocker.Disposition == GoalPolicyDisposition.Blocked
                    ? new GoalBlockedEvent(goal, blocker.Reason) : new GoalAwaitingInputEvent(goal, blocker.Reason));
                return new(state with { Current = goal }, events);
            }
            if (blocker.Disposition is not (GoalPolicyDisposition.Continue or GoalPolicyDisposition.Rejected))
                throw new InvalidOperationException("goal_blocker_policy_invalid");
            events.Add(new GoalBlockerRejectedEvent(goal, blocker.Reason));
        }

        var continuation = await policies.Continuation.EvaluateAsync(facts, cancellationToken).ConfigureAwait(false);
        if (continuation.Disposition == GoalPolicyDisposition.Continue)
        {
            // Runtime startup is an invariant even when a custom policy approves work.
            if (facts.RuntimeRunning)
            {
                goal = GoalTransitions.Reserve(goal, executionId, now);
                events.Add(new GoalContinuationScheduledEvent(goal, continuation.Reason));
            }
            else events.Add(new GoalContinuationSkippedEvent(goal, "runtime_not_started"));
        }
        else if (continuation.Disposition is GoalPolicyDisposition.Paused or GoalPolicyDisposition.AwaitingInput)
        {
            goal = GoalTransitions.ChangeStatus(goal, continuation.Disposition == GoalPolicyDisposition.Paused
                ? GoalStatus.Paused : GoalStatus.AwaitingInput, now);
            events.Add(continuation.Disposition == GoalPolicyDisposition.Paused
                ? new GoalPausedEvent(goal, continuation.Reason) : new GoalAwaitingInputEvent(goal, continuation.Reason));
        }
        else if (continuation.Disposition == GoalPolicyDisposition.Rejected)
            events.Add(new GoalContinuationSkippedEvent(goal, continuation.Reason));
        else throw new InvalidOperationException("goal_continuation_policy_invalid");
        return new(state with { Current = goal }, events);
    }
}
