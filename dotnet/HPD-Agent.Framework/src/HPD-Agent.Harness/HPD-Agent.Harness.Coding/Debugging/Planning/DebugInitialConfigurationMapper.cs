using HPD.Agent.ToolHarness.Coding;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

/// <summary>
/// Canonically maps model-facing initial debugger configuration for launch and attach.
/// Adapter-specific capability validation occurs after DAP initialization.
/// </summary>
internal static class DebugInitialConfigurationMapper
{
    public static DebugInitialConfiguration Map(
        DebugInitialConfigurationInput? input,
        bool stopOnEntry,
        AgentWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var configuration = new DebugInitialConfiguration
        {
            SourceBreakpoints = input?.SourceBreakpoints?
                .Select(item => new DebugSourceBreakpoint(
                    workspace.ResolvePath(item.Path),
                    item.Line,
                    item.Column,
                    item.Condition,
                    item.HitCondition,
                    item.LogMessage))
                .ToArray() ?? [],
            FunctionBreakpoints = input?.FunctionBreakpoints?
                .Select(item => new DebugFunctionBreakpoint(
                    item.Name,
                    item.Condition,
                    item.HitCondition))
                .ToArray() ?? [],
            ExceptionFilters = input?.ExceptionBreakpoints?
                .Select(item => new DebugExceptionFilter(
                    item.FilterId,
                    item.Condition))
                .ToArray() ?? [],
            StopOnEntry = stopOnEntry,
            BreakpointPolicy = input?.BreakpointPolicy ??
                DebugInitialBreakpointPolicy.AllowPending
        };
        DebugExceptionBreakpointValidator.ValidateStructure(
            configuration.ExceptionFilters);
        return configuration;
    }
}
