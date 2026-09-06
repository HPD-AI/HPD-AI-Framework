using System.ComponentModel;
using System.Text.Json;
using HPD.Agent.Middleware;

namespace HPD.Agent.Goals;

internal sealed record GoalToolMutation(GoalAction Action, string? GoalId, long? Revision);

/// <summary>The single model-facing Goal domain function.</summary>
public sealed class AgentGoalToolHarness
{
    internal const string MutationKey = "hpd.goal.mutation";

    /// <summary>Reads Goal state or submits a checked transition to the runtime.</summary>
    [AIFunction(Name = "goal", InvocationModePolicy = AgentInvocationModePolicy.SynchronousOnly)]
    [Description("Manage one persistent thread outcome. Create only for explicit cross-turn intent such as 'continue until finished'. Pause, resume, edit, and clear require explicit user intent. Completion and blocking are evidence proposals checked by policy. A final response does not itself complete the Goal. Never introduce a token budget or broaden action authority.")]
    public Task<string> GoalAsync(
        [Description("The Goal action and its action-specific arguments.")] GoalAction operation,
        FunctionExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        CheckAccess(operation, context.RunConfig.Goals?.ToolAccess ?? GoalToolAccess.All);
        var state = context.Analyze(s => s.MiddlewareState.GetState<GoalPersistentState>(GoalPersistence.StateKey)) ?? new();
        if (state.Current is { } current) GoalTransitions.Validate(current);
        if (operation is GetGoalAction)
            return Task.FromResult(JsonSerializer.Serialize(state, GoalJsonContext.Default.GoalPersistentState));
        var captured = JsonSerializer.Deserialize(JsonSerializer.Serialize(operation, GoalJsonContext.Default.GoalAction),
            GoalJsonContext.Default.GoalAction)!;
        context.ResultMetadata.Set(MutationKey, new GoalToolMutation(captured, state.Current?.GoalId, state.Current?.Revision));
        return Task.FromResult("Goal transition submitted for validation.");
    }

    internal static void CheckAccess(GoalAction action, GoalToolAccess access)
    {
        if (!Enum.IsDefined(access) || access == GoalToolAccess.Hidden ||
            (access == GoalToolAccess.ReadOnly && action is not GetGoalAction))
            throw new InvalidOperationException("goal_action_not_permitted");
    }
}
