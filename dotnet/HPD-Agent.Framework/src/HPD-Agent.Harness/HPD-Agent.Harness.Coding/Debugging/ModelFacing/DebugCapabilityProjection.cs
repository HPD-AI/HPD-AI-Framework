using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

/// <summary>Bounded, model-safe projection of capabilities negotiated with one debug adapter.</summary>
public sealed record DebugCapabilitySummaryMetadata(
    IReadOnlyList<string> SupportedOptionalActions,
    IReadOnlyList<string> UnsupportedOptionalActions,
    IReadOnlyList<string> ExecutionOptions,
    IReadOnlyList<DebugExceptionFilterMetadata> ExceptionFilters);

/// <summary>Projects raw DAP capabilities into stable public debugger concepts.</summary>
internal static class DebugCapabilityProjection
{
    private static readonly (string Action, Func<Capabilities, bool?> Read)[] OptionalActions =
    [
        ("cancelProgress", value => value.SupportsCancelRequest),
        ("discoverDataBreakpoint", value => value.SupportsDataBreakpoints),
        ("disassemble", value => value.SupportsDisassembleRequest),
        ("getBreakpointLocations", value => value.SupportsBreakpointLocationsRequest),
        ("getCompletions", value => value.SupportsCompletionsRequest),
        ("getExceptionInfo", value => value.SupportsExceptionInfoRequest),
        ("getGotoTargets", value => value.SupportsGotoTargetsRequest),
        ("getLoadedSources", value => value.SupportsLoadedSourcesRequest),
        ("getModules", value => value.SupportsModulesRequest),
        ("getStepInTargets", value => value.SupportsStepInTargetsRequest),
        ("goto", value => value.SupportsGotoTargetsRequest),
        ("readMemory", value => value.SupportsReadMemoryRequest),
        ("restart", value => value.SupportsRestartRequest),
        ("restartFrame", value => value.SupportsRestartFrame),
        ("reverseContinue", value => value.SupportsStepBack),
        ("setDataBreakpoints", value => value.SupportsDataBreakpoints),
        ("setExpression", value => value.SupportsSetExpression),
        ("setFunctionBreakpoints", value => value.SupportsFunctionBreakpoints),
        ("setInstructionBreakpoints", value => value.SupportsInstructionBreakpoints),
        ("setVariable", value => value.SupportsSetVariable),
        ("stepBack", value => value.SupportsStepBack),
        ("terminate", value => value.SupportsTerminateRequest),
        ("terminateThreads", value => value.SupportsTerminateThreadsRequest),
        ("writeMemory", value => value.SupportsWriteMemoryRequest)
    ];

    private static readonly (string Option, Func<Capabilities, bool?> Read)[] ExecutionOptions =
    [
        ("conditionalBreakpoints", value => value.SupportsConditionalBreakpoints),
        ("dataBreakpointBytes", value => value.SupportsDataBreakpointBytes),
        ("exceptionFilterConditions", value => value.SupportsExceptionFilterOptions),
        ("hitConditionalBreakpoints", value => value.SupportsHitConditionalBreakpoints),
        ("logPoints", value => value.SupportsLogPoints),
        ("singleThreadExecution", value => value.SupportsSingleThreadExecutionRequests),
        ("steppingGranularity", value => value.SupportsSteppingGranularity),
        ("suspendDebuggee", value => value.SupportSuspendDebuggee),
        ("terminateDebuggee", value => value.SupportTerminateDebuggee)
    ];

    public static DebugCapabilitySummaryMetadata Project(Capabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        return new(
            OptionalActions.Where(item => item.Read(capabilities) == true).Select(item => item.Action).ToArray(),
            OptionalActions.Where(item => item.Read(capabilities) != true).Select(item => item.Action).ToArray(),
            ExecutionOptions.Where(item => item.Read(capabilities) == true).Select(item => item.Option).ToArray(),
            DebugExceptionBreakpointValidator.Describe(capabilities));
    }
}
