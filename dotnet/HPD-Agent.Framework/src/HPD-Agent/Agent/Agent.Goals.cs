using System.Collections.Concurrent;
using HPD.Agent.Goals;

namespace HPD.Agent;

public sealed partial class Agent
{
    // Only threads explicitly restored into this agent participate in startup recovery.
    // This is a set of known scopes, not a second Goal state store or work scheduler.
    private readonly ConcurrentDictionary<ThreadKey, byte> _restoredGoalThreads = new();

    /// <summary>
    /// Restores an existing thread into this agent's runtime scope. Does not start the runtime.
    /// Active Goals are reconciled when the runtime is running or subsequently starts;
    /// paused and terminal Goals do not resume automatically.
    /// </summary>
    /// <param name="sessionId">Existing session identity.</param>
    /// <param name="threadId">Existing thread identity.</param>
    /// <param name="cancellationToken">Cancellation for loading the persisted thread.</param>
    public async Task RestoreThreadAsync(string sessionId, string threadId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        _ = await LoadSessionAndThreadAsync(sessionId, threadId, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ReconcileRestoredGoalAsync(Thread thread)
    {
        if (_middlewarePipeline.Middlewares.OfType<GoalMiddleware>().SingleOrDefault() is not { } goals) return;
        _restoredGoalThreads.TryAdd(new(thread.SessionId, thread.Id), 0);
        if (!IsRunning || Config?.SessionStore is not { } store) return;
        await goals.ReconcileAsync(store, new AgentEventPublisher(store, GetActiveEventCoordinator()), thread,
            _runtimeContext?.RunConfig, async input =>
            {
                try
                {
                    _ = await SubmitRuntimeInputAsync(input, CancellationToken.None).ConfigureAwait(false);
                    return true;
                }
                catch (InvalidOperationException) when (!IsRunning) { return false; }
            }).ConfigureAwait(false);
    }

    private async ValueTask ReconcileRestoredGoalsOnStartAsync(CancellationToken cancellationToken)
    {
        if (Config?.SessionStore is not { } store) return;
        foreach (var key in _restoredGoalThreads.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var thread = await store.ProjectThreadAsync(key.SessionId, key.ThreadId,
                ThreadProjectionPurpose.ModelContext, cancellationToken).ConfigureAwait(false);
            if (thread is not null) await ReconcileRestoredGoalAsync(thread).ConfigureAwait(false);
        }
    }
}
