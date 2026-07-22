using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

/// <summary>Merges a DAP capabilities event without confusing an absent property with false or empty.</summary>
internal static class DebugCapabilityMerger
{
    private static readonly (string Name, Func<Capabilities, bool?> Read)[] BooleanCapabilities =
    [
        ("configurationDone", x => x.SupportsConfigurationDoneRequest), ("functionBreakpoints", x => x.SupportsFunctionBreakpoints),
        ("conditionalBreakpoints", x => x.SupportsConditionalBreakpoints), ("hitConditionalBreakpoints", x => x.SupportsHitConditionalBreakpoints),
        ("evaluateForHovers", x => x.SupportsEvaluateForHovers), ("stepBack", x => x.SupportsStepBack),
        ("setVariable", x => x.SupportsSetVariable), ("restartFrame", x => x.SupportsRestartFrame),
        ("gotoTargets", x => x.SupportsGotoTargetsRequest), ("stepInTargets", x => x.SupportsStepInTargetsRequest),
        ("completions", x => x.SupportsCompletionsRequest), ("modules", x => x.SupportsModulesRequest),
        ("restart", x => x.SupportsRestartRequest), ("exceptionOptions", x => x.SupportsExceptionOptions),
        ("valueFormatting", x => x.SupportsValueFormattingOptions), ("exceptionInfo", x => x.SupportsExceptionInfoRequest),
        ("terminateDebuggee", x => x.SupportTerminateDebuggee), ("suspendDebuggee", x => x.SupportSuspendDebuggee),
        ("delayedStackTrace", x => x.SupportsDelayedStackTraceLoading), ("loadedSources", x => x.SupportsLoadedSourcesRequest),
        ("logPoints", x => x.SupportsLogPoints), ("terminateThreads", x => x.SupportsTerminateThreadsRequest),
        ("setExpression", x => x.SupportsSetExpression), ("terminate", x => x.SupportsTerminateRequest),
        ("dataBreakpoints", x => x.SupportsDataBreakpoints), ("readMemory", x => x.SupportsReadMemoryRequest),
        ("writeMemory", x => x.SupportsWriteMemoryRequest), ("disassemble", x => x.SupportsDisassembleRequest),
        ("cancel", x => x.SupportsCancelRequest), ("breakpointLocations", x => x.SupportsBreakpointLocationsRequest),
        ("clipboardContext", x => x.SupportsClipboardContext), ("steppingGranularity", x => x.SupportsSteppingGranularity),
        ("instructionBreakpoints", x => x.SupportsInstructionBreakpoints), ("exceptionFilterOptions", x => x.SupportsExceptionFilterOptions),
        ("singleThreadExecution", x => x.SupportsSingleThreadExecutionRequests), ("dataBreakpointBytes", x => x.SupportsDataBreakpointBytes),
        ("ansiStyling", x => x.SupportsANSIStyling)
    ];

    public static Capabilities Merge(Capabilities current, Capabilities patch)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(patch);
        return new Capabilities
        {
            SupportsConfigurationDoneRequest = patch.SupportsConfigurationDoneRequest ?? current.SupportsConfigurationDoneRequest,
            SupportsFunctionBreakpoints = patch.SupportsFunctionBreakpoints ?? current.SupportsFunctionBreakpoints,
            SupportsConditionalBreakpoints = patch.SupportsConditionalBreakpoints ?? current.SupportsConditionalBreakpoints,
            SupportsHitConditionalBreakpoints = patch.SupportsHitConditionalBreakpoints ?? current.SupportsHitConditionalBreakpoints,
            SupportsEvaluateForHovers = patch.SupportsEvaluateForHovers ?? current.SupportsEvaluateForHovers,
            ExceptionBreakpointFilters = Copy(patch.ExceptionBreakpointFilters) ?? Copy(current.ExceptionBreakpointFilters),
            SupportsStepBack = patch.SupportsStepBack ?? current.SupportsStepBack,
            SupportsSetVariable = patch.SupportsSetVariable ?? current.SupportsSetVariable,
            SupportsRestartFrame = patch.SupportsRestartFrame ?? current.SupportsRestartFrame,
            SupportsGotoTargetsRequest = patch.SupportsGotoTargetsRequest ?? current.SupportsGotoTargetsRequest,
            SupportsStepInTargetsRequest = patch.SupportsStepInTargetsRequest ?? current.SupportsStepInTargetsRequest,
            SupportsCompletionsRequest = patch.SupportsCompletionsRequest ?? current.SupportsCompletionsRequest,
            CompletionTriggerCharacters = Copy(patch.CompletionTriggerCharacters) ?? Copy(current.CompletionTriggerCharacters),
            SupportsModulesRequest = patch.SupportsModulesRequest ?? current.SupportsModulesRequest,
            AdditionalModuleColumns = Copy(patch.AdditionalModuleColumns) ?? Copy(current.AdditionalModuleColumns),
            SupportedChecksumAlgorithms = Copy(patch.SupportedChecksumAlgorithms) ?? Copy(current.SupportedChecksumAlgorithms),
            SupportsRestartRequest = patch.SupportsRestartRequest ?? current.SupportsRestartRequest,
            SupportsExceptionOptions = patch.SupportsExceptionOptions ?? current.SupportsExceptionOptions,
            SupportsValueFormattingOptions = patch.SupportsValueFormattingOptions ?? current.SupportsValueFormattingOptions,
            SupportsExceptionInfoRequest = patch.SupportsExceptionInfoRequest ?? current.SupportsExceptionInfoRequest,
            SupportTerminateDebuggee = patch.SupportTerminateDebuggee ?? current.SupportTerminateDebuggee,
            SupportSuspendDebuggee = patch.SupportSuspendDebuggee ?? current.SupportSuspendDebuggee,
            SupportsDelayedStackTraceLoading = patch.SupportsDelayedStackTraceLoading ?? current.SupportsDelayedStackTraceLoading,
            SupportsLoadedSourcesRequest = patch.SupportsLoadedSourcesRequest ?? current.SupportsLoadedSourcesRequest,
            SupportsLogPoints = patch.SupportsLogPoints ?? current.SupportsLogPoints,
            SupportsTerminateThreadsRequest = patch.SupportsTerminateThreadsRequest ?? current.SupportsTerminateThreadsRequest,
            SupportsSetExpression = patch.SupportsSetExpression ?? current.SupportsSetExpression,
            SupportsTerminateRequest = patch.SupportsTerminateRequest ?? current.SupportsTerminateRequest,
            SupportsDataBreakpoints = patch.SupportsDataBreakpoints ?? current.SupportsDataBreakpoints,
            SupportsReadMemoryRequest = patch.SupportsReadMemoryRequest ?? current.SupportsReadMemoryRequest,
            SupportsWriteMemoryRequest = patch.SupportsWriteMemoryRequest ?? current.SupportsWriteMemoryRequest,
            SupportsDisassembleRequest = patch.SupportsDisassembleRequest ?? current.SupportsDisassembleRequest,
            SupportsCancelRequest = patch.SupportsCancelRequest ?? current.SupportsCancelRequest,
            SupportsBreakpointLocationsRequest = patch.SupportsBreakpointLocationsRequest ?? current.SupportsBreakpointLocationsRequest,
            SupportsClipboardContext = patch.SupportsClipboardContext ?? current.SupportsClipboardContext,
            SupportsSteppingGranularity = patch.SupportsSteppingGranularity ?? current.SupportsSteppingGranularity,
            SupportsInstructionBreakpoints = patch.SupportsInstructionBreakpoints ?? current.SupportsInstructionBreakpoints,
            SupportsExceptionFilterOptions = patch.SupportsExceptionFilterOptions ?? current.SupportsExceptionFilterOptions,
            SupportsSingleThreadExecutionRequests = patch.SupportsSingleThreadExecutionRequests ?? current.SupportsSingleThreadExecutionRequests,
            SupportsDataBreakpointBytes = patch.SupportsDataBreakpointBytes ?? current.SupportsDataBreakpointBytes,
            BreakpointModes = Copy(patch.BreakpointModes) ?? Copy(current.BreakpointModes),
            SupportsANSIStyling = patch.SupportsANSIStyling ?? current.SupportsANSIStyling
        };
    }

    private static List<T>? Copy<T>(List<T>? values) => values is null ? null : [.. values];

    public static (IReadOnlyList<string> Enabled, IReadOnlyList<string> Disabled) DescribeChanges(
        Capabilities before,
        Capabilities after)
    {
        var enabled = new List<string>();
        var disabled = new List<string>();
        foreach (var capability in BooleanCapabilities)
        {
            var oldValue = capability.Read(before) == true;
            var newValue = capability.Read(after) == true;
            if (oldValue == newValue) continue;
            (newValue ? enabled : disabled).Add(capability.Name);
        }
        return (enabled, disabled);
    }
}
