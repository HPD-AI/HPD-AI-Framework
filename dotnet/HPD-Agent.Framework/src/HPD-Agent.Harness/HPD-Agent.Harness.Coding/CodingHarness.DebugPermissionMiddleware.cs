using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Middleware;

namespace HPDOS.ToolHarnesses.Middleware;

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

[MiddlewareState]
public sealed record DebugPermissionStateData
{
    public IReadOnlyDictionary<string, DebugPermissionDecision> DecisionsByCallId { get; init; }
        = new Dictionary<string, DebugPermissionDecision>(StringComparer.Ordinal);

    internal DebugPermissionStateData WithDecision(
        string callId,
        string action,
        DebugPermissionClass permissionClass)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        var decisions = new Dictionary<string, DebugPermissionDecision>(
            DecisionsByCallId, StringComparer.Ordinal)
        {
            [callId] = new(callId, action, permissionClass)
        };
        return this with { DecisionsByCallId = decisions };
    }
}

/// <summary>
/// Classifies the concrete Debug request after normal HPD function permission mediation and
/// records a narrow invocation-local decision consumed exactly once by the dispatcher.
/// </summary>
public sealed class DebugPermissionMiddleware : IToolHarnessMiddleware
{
    public Task BeforeIterationAsync(
        BeforeIterationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.UpdateMiddlewareState<DebugPermissionStateData>(
            _ => new DebugPermissionStateData());
        return Task.CompletedTask;
    }

    public Task BeforeParallelBatchAsync(
        BeforeParallelBatchContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var call in context.ParallelFunctions)
        {
            if (!string.Equals(call.FunctionName, "Debug", StringComparison.Ordinal) ||
                !TryGetAction(call.Arguments, out var action))
                continue;
            context.UpdateMiddlewareState<DebugPermissionStateData>(state =>
                state.WithDecision(call.CallId, action, Classify(action)));
        }
        return Task.CompletedTask;
    }

    public Task BeforeFunctionAsync(BeforeFunctionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(context.Function?.Name, "Debug", StringComparison.Ordinal))
            return Task.CompletedTask;
        if (!TryGetAction(context.Arguments, out var action))
            return Task.CompletedTask;

        context.UpdateMiddlewareState<DebugPermissionStateData>(state =>
            state.WithDecision(context.FunctionCallId, action, Classify(action)));
        return Task.CompletedTask;
    }

    internal static DebugPermissionClass Classify(string action) => action switch
    {
        "listSessions" or "getStatus" or "getHealth" or "snapshot" or "inspectStop" or
        "getBreakpoints" or "getBreakpointLocations" or "getThreads" or "getStackTrace" or
        "getScopes" or "getVariables" or "getExceptionInfo" or "getModules" or
        "getLoadedSources" or "getSource" or "getStepInTargets" or "getGotoTargets" or
        "getCompletions" or "resolveLocation" or "readMemory" or "getOutput" or
        "persistOutput" or "cancelProgress" or "disassemble" =>
            DebugPermissionClass.Inspection,
        "continue" or "pause" or "stepOver" or "stepIn" or "stepOut" or "stepBack" or
        "reverseContinue" or "restartFrame" or "goto" or "terminateThreads" =>
            DebugPermissionClass.ExecutionControl,
        "setSourceBreakpoints" or "setFunctionBreakpoints" or "setExceptionBreakpoints" or
        "setInstructionBreakpoints" or "discoverDataBreakpoint" or "setDataBreakpoints" =>
            DebugPermissionClass.BreakpointMutation,
        "disconnect" or "terminate" or "restart" => DebugPermissionClass.Lifecycle,
        "launch" => DebugPermissionClass.Launch,
        "attach" => DebugPermissionClass.Attach,
        "evaluate" => DebugPermissionClass.Evaluation,
        "setVariable" or "setExpression" => DebugPermissionClass.StateMutation,
        "writeMemory" => DebugPermissionClass.MemoryWrite,
        _ => throw new InvalidOperationException("The Debug action has no permission classification.")
    };

    private static bool TryGetAction(IReadOnlyDictionary<string, object?> arguments, out string action)
    {
        action = string.Empty;
        if (!arguments.TryGetValue("request", out var request) || request is null)
            return false;
        if (request is DebugOperation operation)
        {
            action = HPD.Agent.ToolHarness.Coding.Debugging.DebugOperationDispatcher.Action(operation);
            return true;
        }
        if (request is JsonElement element &&
            element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("action", out var actionElement) &&
            actionElement.ValueKind == JsonValueKind.String)
        {
            action = actionElement.GetString() ?? string.Empty;
            return action.Length > 0;
        }
        if (request is IReadOnlyDictionary<string, object?> dictionary &&
            dictionary.TryGetValue("action", out var rawAction) &&
            rawAction is string text)
        {
            action = text;
            return action.Length > 0;
        }
        return false;
    }
}
