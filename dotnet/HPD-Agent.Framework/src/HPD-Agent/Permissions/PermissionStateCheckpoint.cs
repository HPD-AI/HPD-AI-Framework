using System.Runtime.CompilerServices;
using HPD.Agent.Middleware;

namespace HPD.Agent.Permissions;

/// <summary>
/// Permission-owned bookkeeping around the existing middleware-state save path.
/// Weak entries track execution progress and invalid object references, never consent.
/// </summary>
internal static class PermissionStateCheckpoint
{
    private sealed class PendingSave;

    private static readonly ConditionalWeakTable<AgentContext, PendingSave> Pending = new();
    private static readonly ConditionalWeakTable<Session, PendingSave> FailedSessions = new();

    internal static void MarkPending(AgentContext context) =>
        Pending.GetValue(context, static _ => new PendingSave());

    internal static bool IsPending(AgentContext context) => Pending.TryGetValue(context, out _);

    internal static void EnsureSaved(AgentContext context)
    {
        if (IsPending(context) || context.Session is { } session && FailedSessions.TryGetValue(session, out _))
            throw new InvalidOperationException("Permission approval state must be saved before execution.");
    }

    // Admission and this awaited checkpoint are serial within an execution.
    internal static async Task SaveAsync(
        AgentContext context,
        IReadOnlyDictionary<string, MiddlewareStateFactory> factories,
        CancellationToken cancellationToken)
    {
        if (context.Session is { } current && FailedSessions.TryGetValue(current, out _))
            throw new InvalidOperationException("Reload the session after a failed permission checkpoint.");
        if (!IsPending(context)) return;
        var session = context.Session;
        if (session?.Store is null)
            throw new InvalidOperationException("Permission persistence became unavailable before execution.");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.State.MiddlewareState.SaveToSession(session, factories, PermissionDecisionIndex.StateKey);
            await session.SaveAsync(cancellationToken).ConfigureAwait(false);
            Pending.Remove(context);
        }
        catch
        {
            FailedSessions.GetValue(session, static _ => new PendingSave());
            throw;
        }
    }
}
