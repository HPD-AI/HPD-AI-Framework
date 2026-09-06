using System.Text.Json;

namespace HPD.Agent.Goals;

/// <summary>A journal-consistent snapshot, including other middleware state that must be preserved.</summary>
internal sealed record GoalJournalSnapshot(GoalPersistentState Goal, IReadOnlyDictionary<string, string> MiddlewareState,
    ThreadJournalCursor Cursor);

internal static class GoalPersistence
{
    internal static string StateKey => typeof(GoalPersistentState).FullName!;

    internal static GoalPersistentState Read(IReadOnlyDictionary<string, string> state)
    {
        if (!state.TryGetValue(StateKey, out var json)) return new();
        var result = JsonSerializer.Deserialize(json, GoalJsonContext.Default.GoalPersistentState)
            ?? throw new InvalidOperationException("goal_state_invalid");
        if (result.Current is { } goal) GoalTransitions.Validate(goal);
        if (result.PendingExecution is { } pending)
        {
            GoalTransitions.Validate(pending.GoalSnapshot);
            if (string.IsNullOrWhiteSpace(pending.ExecutionId) || string.IsNullOrWhiteSpace(pending.MessageTurnId))
                throw new InvalidOperationException("goal_attribution_invalid");
        }
        if (result.AccountingCheckpoint is { } checkpoint && (checkpoint.Generation <= 0 || checkpoint.SequenceNumber <= 0))
            throw new InvalidOperationException("goal_accounting_checkpoint_invalid");
        return result;
    }

    internal static async ValueTask<GoalJournalSnapshot> ReadAsync(ISessionStore store, ThreadKey key,
        CancellationToken cancellationToken)
    {
        var head = await store.GetThreadEventHeadAsync(key, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("goal_thread_missing");
        IReadOnlyDictionary<string, string> state = new Dictionary<string, string>();
        await foreach (var batch in store.ReadThreadEventsAsync(key,
            new(ThreadJournalCursor.Start(head.Generation), head.ThreadSequenceNumber), cancellationToken).ConfigureAwait(false))
        {
            foreach (var evt in batch.Events)
                if (evt is ThreadMiddlewareStateCommittedEvent committed) state = committed.State;
        }
        return new(Read(state), state, head.Cursor);
    }

    internal static async ValueTask<ThreadEventAppendResult> CommitAsync(IAgentEventPublisher publisher,
        ThreadKey key, GoalJournalSnapshot snapshot, GoalPersistentState updated,
        AgentEvent lifecycle, CancellationToken cancellationToken)
        => await CommitAsync(publisher, key, snapshot, updated, [lifecycle], cancellationToken).ConfigureAwait(false);

    internal static async ValueTask<ThreadEventAppendResult> CommitAsync(IAgentEventPublisher publisher,
        ThreadKey key, GoalJournalSnapshot snapshot, GoalPersistentState updated,
        IReadOnlyList<AgentEvent> lifecycle, CancellationToken cancellationToken)
    {
        if (updated.Current is { } goal) GoalTransitions.Validate(goal);
        var state = new Dictionary<string, string>(snapshot.MiddlewareState, StringComparer.Ordinal)
        {
            [StateKey] = JsonSerializer.Serialize(updated, GoalJsonContext.Default.GoalPersistentState)
        };
        // The cursor protects the entire state map from concurrent overwrite. The store commits
        // both facts atomically; a publication failure must be recovered by reading its journal.
        return await publisher.CommitAndPublishAsync(key,
            [new ThreadMiddlewareStateCommittedEvent(state), .. lifecycle],
            new(snapshot.Cursor), cancellationToken).ConfigureAwait(false);
    }
}
