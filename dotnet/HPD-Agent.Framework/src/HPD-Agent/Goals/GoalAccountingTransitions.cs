namespace HPD.Agent.Goals;

internal sealed record GoalAccountingResult(GoalPersistentState State, GoalData? AccountedGoal, bool Applied);

internal static class GoalAccountingTransitions
{
    internal static GoalPersistentState Begin(GoalPersistentState state, string executionId,
        string messageTurnId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageTurnId);
        if (state.PendingExecution is { } pending)
        {
            if (pending.ExecutionId == executionId && pending.MessageTurnId == messageTurnId) return state;
            throw new InvalidOperationException("goal_accounting_recovery_required");
        }
        if (state.Current is not { Status: GoalStatus.Active } goal) return state;
        GoalTransitions.Validate(goal);
        return state with { PendingExecution = new(goal, executionId, messageTurnId, now) };
    }

    internal static GoalPersistentState PreserveAttribution(GoalPersistentState state)
        => state.PendingExecution is { } pending && state.Current is { } goal && pending.GoalSnapshot.GoalId == goal.GoalId
            ? state with { PendingExecution = pending with { GoalSnapshot = goal } } : state;

    internal static GoalAccountingResult Close(GoalPersistentState state, string executionId,
        string messageTurnId, ThreadJournalCursor terminal, GoalUsageProjection usage, TimeSpan elapsed,
        DateTimeOffset now)
    {
        if (terminal.Generation <= 0 || terminal.SequenceNumber <= 0 || usage.Tokens < 0 ||
            !Enum.IsDefined(usage.Quality) || elapsed < TimeSpan.Zero)
            throw new InvalidOperationException("goal_accounting_invalid");
        if (state.AccountingCheckpoint is { } checkpoint)
        {
            if (terminal.Generation < checkpoint.Generation ||
                (terminal.Generation == checkpoint.Generation && terminal.SequenceNumber <= checkpoint.SequenceNumber))
                return new(state, null, false);
        }
        var pending = state.PendingExecution;
        if (pending is null || pending.ExecutionId != executionId || pending.MessageTurnId != messageTurnId)
            return new(state, null, false);
        var goal = state.Current?.GoalId == pending.GoalSnapshot.GoalId ? state.Current : pending.GoalSnapshot;
        GoalTransitions.Validate(goal);
        var previous = goal.Accounting;
        var quality = previous.ExecutionCount == 0 ? usage.Quality
            : previous.UsageQuality == GoalUsageQuality.Exact && usage.Quality == GoalUsageQuality.Exact ? GoalUsageQuality.Exact
            : previous.UsageQuality == GoalUsageQuality.Unavailable && usage.Quality == GoalUsageQuality.Unavailable
                ? GoalUsageQuality.Unavailable : GoalUsageQuality.Partial;
        var accounted = goal with
        {
            Revision = checked(goal.Revision + 1), Continuation = null, UpdatedAt = now,
            Accounting = previous with
            {
                TokensUsed = checked(previous.TokensUsed + usage.Tokens), UsageQuality = quality,
                ExecutionTime = previous.ExecutionTime + elapsed,
                ExecutionCount = checked(previous.ExecutionCount + (previous.LastAccountedExecutionId == executionId ? 0 : 1)),
                LastAccountedExecutionId = executionId, LastAccountedMessageTurnId = messageTurnId
            }
        };
        return new(state with
        {
            Current = state.Current?.GoalId == accounted.GoalId ? accounted : state.Current,
            PendingExecution = null, AccountingCheckpoint = terminal
        }, accounted, true);
    }
}
