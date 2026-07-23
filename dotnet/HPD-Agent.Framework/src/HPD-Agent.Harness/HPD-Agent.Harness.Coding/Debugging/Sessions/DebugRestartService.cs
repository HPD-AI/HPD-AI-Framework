namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal sealed record DebugRestartResult(bool InPlace, DebugSessionStartResult? Replacement);

internal sealed class DebugRestartService(
    DebugSessionManager manager,
    DebugSemanticService semantics,
    DebugSessionStartOrchestrator starts)
{
    public Task<DebugRestartResult> RestartAsync(
        DebugTreeLookupScope owner,
        string treeId,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        var tree = manager.ResolveTree(owner, treeId);
        var session = tree.SelectSession(sessionId);
        if (session.SessionId != tree.RootSessionId)
            throw new InvalidOperationException(
                "A child session without in-place restart support cannot be reconstructed as a root debug tree.");
        var template = tree.RestartTemplate
            ?? throw new InvalidOperationException("The debug tree does not retain a restart template.");
        tree.Authorization.ValidateCurrent(tree.RuntimeBinding, session.LaunchPlan);
        var desired = tree.Breakpoints.Snapshot;
        var replacement = template with
        {
            Runtime = tree.RuntimeBinding,
            LaunchPlan = session.LaunchPlan,
            IsAttach = session.IsAttach,
            InitialConfiguration = new()
            {
                SourceBreakpoints = desired.Source,
                FunctionBreakpoints = desired.Function,
                ExceptionFilters = desired.Exception,
                InstructionBreakpoints = desired.Instruction,
                DataBreakpoints = desired.Data,
                StopOnEntry = template.InitialConfiguration.StopOnEntry
            }
        };
        return RestartAsync(owner, treeId, session.SessionId, replacement, cancellationToken);
    }

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
