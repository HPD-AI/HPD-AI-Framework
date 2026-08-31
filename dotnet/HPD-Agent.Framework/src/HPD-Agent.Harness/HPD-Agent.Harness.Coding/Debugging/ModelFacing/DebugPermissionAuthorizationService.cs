using HPD.Agent.Middleware;
namespace HPD.Agent.ToolHarness.Coding.Debugging;

public enum DebugPermissionClass
{
    Inspection,
    ExecutionControl,
    BreakpointMutation,
    Lifecycle,
    Launch,
    Attach,
    Evaluation,
    StateMutation,
    MemoryWrite
}

public sealed record DebugPermissionDecision(
    string FunctionCallId,
    string Action,
    DebugPermissionClass PermissionClass);

internal sealed class DebugPermissionAuthorizationService
{
    public DebugPermissionDecision DemandApproved(
        FunctionExecutionContext context,
        string action)
    {
        var grant = context.Permission.DemandApproved();
        var callId = context.InvocationSnapshot.FunctionCallId;
        if (!string.Equals(grant.FunctionCallId, callId, StringComparison.Ordinal) ||
            !string.Equals(grant.FunctionName, "Debug", StringComparison.Ordinal) ||
            !string.Equals(grant.Action, action, StringComparison.Ordinal))
            throw new UnauthorizedAccessException(
                "The debugger invocation has no matching invocation-bound permission grant.");
        var permissionClass = grant.Key.Scope switch
        {
            "debug/inspection" => DebugPermissionClass.Inspection,
            "debug/execution-control" => DebugPermissionClass.ExecutionControl,
            "debug/breakpoint-mutation" => DebugPermissionClass.BreakpointMutation,
            "debug/lifecycle" => DebugPermissionClass.Lifecycle,
            "debug/launch" => DebugPermissionClass.Launch,
            "debug/attach" => DebugPermissionClass.Attach,
            "debug/evaluation" => DebugPermissionClass.Evaluation,
            "debug/state-mutation" => DebugPermissionClass.StateMutation,
            "debug/memory-write" => DebugPermissionClass.MemoryWrite,
            _ => throw new UnauthorizedAccessException("The debugger grant has an unknown generated permission scope.")
        };
        return new DebugPermissionDecision(callId, action, permissionClass);
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
