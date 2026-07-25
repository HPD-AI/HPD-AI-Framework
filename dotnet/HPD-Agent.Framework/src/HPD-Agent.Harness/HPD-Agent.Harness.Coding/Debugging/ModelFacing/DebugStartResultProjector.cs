using HPD.Agent.Middleware;
using HPD.Agent.ToolHarness.Coding;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

/// <summary>Bounded model-facing notice emitted for a successful debugger launch.</summary>
public sealed record DebugLaunchNoticeMetadata(string Code, string Guidance);

/// <summary>Projects one activated execution into bounded XML and typed result metadata.</summary>
internal sealed class DebugStartResultProjector(DebugResultFormatter formatter)
{
    public string Project(
        string action,
        DebugExecutionPlan plan,
        DebugSessionStartResult result,
        FunctionExecutionContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(context);
        var (adapterId, adapterMethod) = AdapterIdentity(plan);
        context.ResultMetadata.Set(
            CodingToolMetadataKeys.DebugOperation,
            new DebugOperationMetadata(
                action,
                result.DebugTreeId,
                result.DebugSessionId,
                true));
        context.ResultMetadata.Set(
            CodingToolMetadataKeys.DebugExecutionActivation,
            new DebugExecutionActivationMetadata(
                plan.SemanticStartKind,
                adapterMethod,
                adapterId,
                result.OwnedResourceCount));
        var attributes = new List<KeyValuePair<string, object?>>
        {
            new("debugTreeId", result.DebugTreeId),
            new("debugSessionId", result.DebugSessionId),
            new("adapter", adapterId),
            new("planner", plan.PlannerId),
            new("semanticStart", plan.SemanticStartKind),
            new("adapterMethod", adapterMethod),
            new("status", result.Status),
            new("requestedBreakpoints", result.Breakpoints.Requested),
            new("acknowledgedBreakpoints", result.Breakpoints.Acknowledged),
            new("resolvedBreakpoints", result.Breakpoints.Verified),
            new("pendingBreakpoints", result.Breakpoints.Pending),
            new("backgroundHandleId", result.Handle.HandleId)
        };
        var details = new List<string>();
        if (NeedsStoppingStrategyNotice(action, plan.InitialConfiguration))
        {
            const string guidance =
                "The target may terminate before inspection. For inspection tasks, use stopOnEntry or initial breakpoints.";
            var notice = new DebugLaunchNoticeMetadata(
                "no_initial_stop_strategy",
                guidance);
            context.ResultMetadata.Set(
                CodingToolMetadataKeys.DebugLaunchNotices,
                new[] { notice });
            attributes.Add(new("warning", notice.Code));
            details.Add(guidance);
        }
        return formatter.Success(
            action,
            attributes,
            details);
    }

    internal static bool NeedsStoppingStrategyNotice(
        string action,
        DebugInitialConfiguration configuration)
        => string.Equals(action, "launch", StringComparison.Ordinal) &&
           !configuration.StopOnEntry &&
           configuration.SourceBreakpoints.Count == 0 &&
           configuration.FunctionBreakpoints.Count == 0 &&
           configuration.ExceptionFilters.Count == 0;

    private static (string AdapterId, DebugAdapterStartMethod Method)
        AdapterIdentity(DebugExecutionPlan plan)
        => plan switch
        {
            DirectAdapterDebugExecutionPlan direct =>
                (direct.Adapter.AdapterId, direct.Adapter.Method),
            HostedAttachDebugExecutionPlan hosted =>
                (hosted.Attach.AdapterId, DebugAdapterStartMethod.Attach),
            PreparedAdapterDebugExecutionPlan prepared =>
                (prepared.Launch.AdapterId, DebugAdapterStartMethod.Launch),
            _ => ("unknown", DebugAdapterStartMethod.Launch)
        };
}
