using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.Sandbox;
using HPD.Environment.Contracts;

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
        RuntimeWorkspaceBinding workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var configuration = new DebugInitialConfiguration
        {
            SourceBreakpoints = input?.SourceBreakpoints?
                .Select(item => new DebugSourceBreakpoint(
                    DebugWorkspaceAccess.ToEffective(
                        workspace,
                        DebugWorkspaceAccess.Resolve(
                            workspace,
                            item.Path)),
                    item.Line,
                    item.Column,
                    Normalize(item.Condition),
                    Normalize(item.HitCondition),
                    Normalize(item.LogMessage)))
                .ToArray() ?? [],
            FunctionBreakpoints = input?.FunctionBreakpoints?
                .Select(item => new DebugFunctionBreakpoint(
                    item.Name,
                    Normalize(item.Condition),
                    Normalize(item.HitCondition)))
                .ToArray() ?? [],
            ExceptionFilters = input?.ExceptionBreakpoints?
                .Select(item => new DebugExceptionFilter(
                    item.FilterId,
                    Normalize(item.Condition)))
                .ToArray() ?? [],
            StopOnEntry = stopOnEntry,
            BreakpointPolicy = input?.BreakpointPolicy ??
                DebugInitialBreakpointPolicy.AllowPending
        };
        DebugExceptionBreakpointValidator.ValidateStructure(
            configuration.ExceptionFilters);
        return configuration;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
