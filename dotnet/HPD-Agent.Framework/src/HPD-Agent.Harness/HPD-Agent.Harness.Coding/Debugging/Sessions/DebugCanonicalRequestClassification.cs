using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal enum DebugRequestExposure
{
    Semantic,
    InternalLifecycle,
    HostOnlyTypedExtension,
    Deferred,
    Unsupported
}

internal enum DebugRequestImplementationStatus { Implemented, Phase6Pending, Deferred, Unsupported }
internal enum DebugRequestTestStatus { ConformanceCovered }

internal enum DebugSemanticFailureReason
{
    CapabilityUnavailable,
    InvalidSessionState,
    ReferenceExpired,
    ReferenceOwnerMismatch,
    InvalidArguments,
    PermissionDenied,
    AdapterRequestFailed,
    RequestTimedOut,
    RequestCancelled,
    OutputTooLarge,
    ContentStoreUnavailable,
    UnsupportedCanonicalRequest,
    HostExtensionUnavailable
}

internal sealed class DebugSemanticException(
    DebugSemanticFailureReason reason,
    string message,
    Exception? innerException = null) : InvalidOperationException(message, innerException)
{
    public DebugSemanticFailureReason Reason { get; } = reason;
}

internal sealed record DebugCanonicalRequestClassification(
    string Command,
    DebugRequestExposure Exposure,
    DebugRequestImplementationStatus Status,
    string SemanticOwner,
    string? RequiredCapability,
    DebugTreeGrant RequiredGrant,
    string StatePrecondition,
    string ReferenceLifetime,
    string ResultLimit,
    DebugRequestTestStatus TestStatus);

/// <summary>Executable Phase 6 ownership and support policy for every canonical client request.</summary>
internal static class DebugCanonicalRequestCatalog
{
    private static readonly HashSet<string> InternalLifecycle = new(StringComparer.Ordinal)
    {
        "attach", "cancel", "configurationDone", "disconnect", "initialize", "launch",
        "restart", "setBreakpoints", "setDataBreakpoints", "setExceptionBreakpoints",
        "setFunctionBreakpoints", "setInstructionBreakpoints", "terminate"
    };

    private static readonly HashSet<string> SemanticRequests = new(StringComparer.Ordinal)
    {
        "breakpointLocations", "completions", "continue", "dataBreakpointInfo", "disassemble",
        "evaluate", "exceptionInfo", "goto", "gotoTargets", "loadedSources", "locations",
        "modules", "next", "pause", "readMemory", "restartFrame", "reverseContinue", "scopes",
        "setExpression", "setVariable", "source", "stackTrace", "stepBack", "stepIn",
        "stepInTargets", "stepOut", "terminateThreads", "threads", "variables", "writeMemory"
    };

    // Deliberately independent from the ownership sets above: additions to the pinned schema must
    // be classified and assigned an explicit conformance obligation.
    private static readonly HashSet<string> ConformanceCoveredRequests = new(StringComparer.Ordinal)
    {
        "attach", "breakpointLocations", "cancel", "completions", "configurationDone", "continue",
        "dataBreakpointInfo", "disassemble", "disconnect", "evaluate", "exceptionInfo", "goto",
        "gotoTargets", "initialize", "launch", "loadedSources", "locations", "modules", "next",
        "pause", "readMemory", "restart", "restartFrame", "reverseContinue", "scopes",
        "setBreakpoints", "setDataBreakpoints", "setExceptionBreakpoints", "setExpression",
        "setFunctionBreakpoints", "setInstructionBreakpoints", "setVariable", "source", "stackTrace",
        "stepBack", "stepIn", "stepInTargets", "stepOut", "terminate", "terminateThreads", "threads",
        "variables", "writeMemory"
    };

    private static readonly IReadOnlyDictionary<string, string> Capabilities =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["breakpointLocations"] = "supportsBreakpointLocationsRequest",
            ["completions"] = "supportsCompletionsRequest",
            ["dataBreakpointInfo"] = "supportsDataBreakpoints",
            ["disassemble"] = "supportsDisassembleRequest",
            ["exceptionInfo"] = "supportsExceptionInfoRequest",
            ["gotoTargets"] = "supportsGotoTargetsRequest",
            ["loadedSources"] = "supportsLoadedSourcesRequest",
            ["modules"] = "supportsModulesRequest",
            ["readMemory"] = "supportsReadMemoryRequest",
            ["restartFrame"] = "supportsRestartFrame",
            ["reverseContinue"] = "supportsStepBack",
            ["setExpression"] = "supportsSetExpression",
            ["setVariable"] = "supportsSetVariable",
            ["stepBack"] = "supportsStepBack",
            ["stepInTargets"] = "supportsStepInTargetsRequest",
            ["terminateThreads"] = "supportsTerminateThreadsRequest",
            ["writeMemory"] = "supportsWriteMemoryRequest"
        };

    public static IReadOnlyDictionary<string, DebugCanonicalRequestClassification> All { get; } = Build();
    internal static IReadOnlyCollection<string> ExplicitlyDeclaredCommands { get; } =
        InternalLifecycle.Concat(SemanticRequests).Order(StringComparer.Ordinal).ToArray();

    public static DebugCanonicalRequestClassification Get(string command)
        => All.TryGetValue(command, out var classification) ? classification
            : throw new DebugSemanticException(DebugSemanticFailureReason.UnsupportedCanonicalRequest,
                $"Canonical DAP request '{command}' has no HPD classification.");

    private static IReadOnlyDictionary<string, DebugCanonicalRequestClassification> Build()
    {
        var requests = DebugProtocolFeatureInventory.All.Where(x => x.Kind == DapFeatureKind.Request).ToArray();
        var canonicalNames = requests.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        var declaredNames = InternalLifecycle.Concat(SemanticRequests).ToHashSet(StringComparer.Ordinal);
        var missing = canonicalNames.Except(declaredNames).Order(StringComparer.Ordinal).ToArray();
        var stale = declaredNames.Except(canonicalNames).Order(StringComparer.Ordinal).ToArray();
        var untested = canonicalNames.Except(ConformanceCoveredRequests).Order(StringComparer.Ordinal).ToArray();
        var staleTests = ConformanceCoveredRequests.Except(canonicalNames).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length > 0 || stale.Length > 0 || untested.Length > 0 || staleTests.Length > 0 ||
            InternalLifecycle.Overlaps(SemanticRequests))
            throw new InvalidOperationException(
                $"Canonical DAP request classification is out of date. Missing=[{string.Join(',', missing)}]; " +
                $"stale=[{string.Join(',', stale)}]; untested=[{string.Join(',', untested)}]; " +
                $"staleTests=[{string.Join(',', staleTests)}].");
        var result = new Dictionary<string, DebugCanonicalRequestClassification>(StringComparer.Ordinal);
        foreach (var request in requests)
        {
            var internalRequest = InternalLifecycle.Contains(request.Name);
            var grant = GrantFor(request.Name);
            result.Add(request.Name, new(
                request.Name,
                internalRequest ? DebugRequestExposure.InternalLifecycle : DebugRequestExposure.Semantic,
                DebugRequestImplementationStatus.Implemented,
                internalRequest ? "session-orchestrator" : OwnerFor(request.Name),
                Capabilities.GetValueOrDefault(request.Name),
                grant,
                StateFor(request.Name),
                LifetimeFor(request.Name),
                LimitFor(request.Name),
                DebugRequestTestStatus.ConformanceCovered));
        }
        return result;
    }

    private static DebugTreeGrant GrantFor(string command) => command switch
    {
        "continue" or "goto" or "next" or "pause" or "restart" or "restartFrame" or
        "reverseContinue" or "stepBack" or "stepIn" or "stepOut" or "terminate" or
        "terminateThreads" => DebugTreeGrant.RoutineExecutionControl,
        "evaluate" => DebugTreeGrant.Evaluate,
        "setExpression" or "setVariable" => DebugTreeGrant.MutateVariables,
        "writeMemory" => DebugTreeGrant.WriteMemory,
        "setBreakpoints" => DebugTreeGrant.SourceBreakpoints,
        "setFunctionBreakpoints" => DebugTreeGrant.FunctionBreakpoints,
        "setExceptionBreakpoints" => DebugTreeGrant.ExceptionBreakpoints,
        "setInstructionBreakpoints" => DebugTreeGrant.InstructionBreakpoints,
        "setDataBreakpoints" => DebugTreeGrant.DataBreakpoints,
        "attach" or "cancel" or "configurationDone" or "disconnect" or "initialize" or "launch" => DebugTreeGrant.None,
        _ => DebugTreeGrant.Inspect
    };

    private static string OwnerFor(string command) => command switch
    {
        "setBreakpoints" or "setDataBreakpoints" or "setExceptionBreakpoints" or
        "setFunctionBreakpoints" or "setInstructionBreakpoints" => "breakpoint-service",
        "readMemory" or "writeMemory" or "disassemble" => "native-debug-service",
        _ => "semantic-service"
    };

    private static string StateFor(string command) => command switch
    {
        "continue" or "evaluate" or "exceptionInfo" or "goto" or "next" or "restartFrame" or
        "reverseContinue" or "scopes" or "setExpression" or "setVariable" or "stackTrace" or
        "stepBack" or "stepIn" or "stepInTargets" or "stepOut" or "variables" => "owned stopped session/thread",
        "initialize" => "created session",
        "launch" or "attach" or "configurationDone" => "initializing/configuring session",
        _ => "owned live session"
    };

    private static string LifetimeFor(string command) => command switch
    {
        "scopes" or "variables" or "evaluate" or "exceptionInfo" or "restartFrame" or
        "setExpression" or "setVariable" or "stackTrace" or "stepInTargets" => "session/thread/suspension epoch",
        "goto" or "gotoTargets" or "locations" => "session/query/generation",
        "modules" or "loadedSources" or "disassemble" => "tree/session/query/generation",
        "readMemory" or "writeMemory" => "tree/session/reference generation",
        "source" or "breakpointLocations" => "tree/session/source generation",
        _ => "tree/session"
    };

    private static string LimitFor(string command) => command switch
    {
        "stackTrace" or "threads" or "scopes" => "100/100/64",
        "variables" or "modules" => "200",
        "disassemble" => "256 instructions",
        "readMemory" => "64 KiB",
        "writeMemory" => "4 KiB",
        "evaluate" => "64 KiB inline",
        _ => "bounded semantic result"
    };
}
