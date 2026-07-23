using HPD.Agent.Middleware;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal sealed class DebugPermissionAuthorizationService
{
    public DebugPermissionDecision DemandApproved(
        FunctionExecutionContext context,
        string action)
    {
        var callId = context.InvocationSnapshot.FunctionCallId;
        var state = context.Analyze(snapshot =>
            snapshot.MiddlewareState.GetState<DebugPermissionStateData>(
                typeof(DebugPermissionStateData).FullName!));
        if (state is null ||
            !state.DecisionsByCallId.TryGetValue(callId, out var decision) ||
            !string.Equals(decision.FunctionCallId, callId, StringComparison.Ordinal) ||
            !string.Equals(decision.Action, action, StringComparison.Ordinal))
            throw new UnauthorizedAccessException(
                "The debugger invocation has no matching middleware permission decision.");
        return decision;
    }

    public DebugPrivilegedOperationAuthorization CreatePrivileged(
        DebugPermissionDecision decision,
        DebugRuntimeServices services,
        DebugTreeLookupScope owner,
        string treeId,
        string? sessionId,
        DebugPrivilegedOperation operation)
    {
        var expectedClass = operation switch
        {
            DebugPrivilegedOperation.PrivilegedEvaluate => DebugPermissionClass.Evaluation,
            DebugPrivilegedOperation.SetVariable or DebugPrivilegedOperation.SetExpression =>
                DebugPermissionClass.StateMutation,
            DebugPrivilegedOperation.WriteMemory => DebugPermissionClass.MemoryWrite,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        if (decision.PermissionClass != expectedClass)
            throw new UnauthorizedAccessException("The debugger permission decision does not authorize this privileged operation.");
        var tree = services.Manager.ResolveTree(owner, treeId);
        var session = tree.SelectSession(sessionId);
        return DebugPrivilegedOperationAuthorization.Create(tree, session, operation);
    }
}
