namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal enum DebugTerminationScope
{
    Debuggee,
    Session,
    Tree
}

internal sealed record DebugTerminationResult(
    DebugTerminationScope Scope,
    bool Graceful,
    bool TreeDisposed,
    string? SafeReasonCode = null);

/// <summary>
/// Defines semantic lifetime boundaries above DAP terminate/disconnect and guarantees that a
/// tree-scoped stop cannot leave HPD-owned transports or adapter processes behind.
/// </summary>
internal sealed class DebugLifecycleService(
    DebugSessionManager manager,
    DebugSemanticService semantics)
{
    public async Task<DebugTerminationResult> TerminateAsync(
        DebugTreeLookupScope owner,
        string treeId,
        string? sessionId,
        DebugTerminationScope scope,
        bool terminateDebuggee,
        CancellationToken cancellationToken)
    {
        switch (scope)
        {
            case DebugTerminationScope.Debuggee:
                await semantics.TerminateAsync(
                    owner, treeId, sessionId, restart: false, cancellationToken).ConfigureAwait(false);
                return new(scope, Graceful: true, TreeDisposed: false);

            case DebugTerminationScope.Session:
                var tree = manager.ResolveTree(owner, treeId);
                var selected = tree.SelectSession(sessionId);
                var removesTree = selected.SessionId == tree.RootSessionId;
                await semantics.DisconnectAsync(
                    owner, treeId, selected.SessionId, terminateDebuggee,
                    suspendDebuggee: false, cancellationToken).ConfigureAwait(false);
                return new(scope, Graceful: true, TreeDisposed: removesTree);

            case DebugTerminationScope.Tree:
                return await TerminateTreeAsync(
                    owner, treeId, terminateDebuggee, cancellationToken).ConfigureAwait(false);

            default:
                throw new ArgumentOutOfRangeException(nameof(scope));
        }
    }

    private async Task<DebugTerminationResult> TerminateTreeAsync(
        DebugTreeLookupScope owner,
        string treeId,
        bool terminateDebuggee,
        CancellationToken cancellationToken)
    {
        var tree = manager.ResolveTree(owner, treeId);
        try
        {
            await semantics.DisconnectAsync(
                owner, treeId, tree.RootSessionId, terminateDebuggee,
                suspendDebuggee: false, cancellationToken).ConfigureAwait(false);
            return new(DebugTerminationScope.Tree, Graceful: true, TreeDisposed: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await manager.RemoveAndDisposeAsync(owner, treeId).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await manager.RemoveAndDisposeAsync(owner, treeId).ConfigureAwait(false);
            return new(DebugTerminationScope.Tree, Graceful: false, TreeDisposed: true,
                SafeReasonCode: "DEBUG_TREE_FORCED_DISPOSAL");
        }
    }
}
