namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal sealed record DebugRestartResult(bool InPlace, DebugSessionStartResult? Replacement);

internal sealed class DebugRestartService(
    DebugSessionManager manager,
    DebugSemanticService semantics,
    DebugSessionStartOrchestrator starts)
{
    public async Task<DebugRestartResult> RestartAsync(
        DebugTreeLookupScope owner,
        string treeId,
        string? sessionId,
        DebugSessionStartRequest replacementRequest,
        CancellationToken cancellationToken)
    {
        var session = manager.ResolveTree(owner, treeId).SelectSession(sessionId);
        if (await semantics.RestartInPlaceAsync(owner, treeId, session.SessionId, cancellationToken).ConfigureAwait(false))
            return new(true, null);

        var restartData = session.RestartData?.Clone();
        await semantics.DisconnectAsync(
            owner, treeId, session.SessionId,
            terminateDebuggee: session.IsAttach ? false : true,
            suspendDebuggee: false,
            cancellationToken).ConfigureAwait(false);
        var replacement = await starts.StartAsync(replacementRequest with { RestartData = restartData }, cancellationToken).ConfigureAwait(false);
        return new(false, replacement);
    }
}
