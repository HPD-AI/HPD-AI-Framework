namespace HPD.Agent.ToolHarness.Coding.TUI.Debugging;

internal enum DebugPresentationFamily
{
    Lifecycle,
    Breakpoint,
    Execution,
    Inspection,
    Mutation,
    Administrative
}

internal sealed record DebugActionPresentationPolicy(
    string Action,
    DebugPresentationFamily Family,
    bool FoldOnSuccess = false)
{
    private static readonly IReadOnlyDictionary<string, DebugActionPresentationPolicy> Policies =
        CreatePolicies();

    public static IReadOnlyCollection<DebugActionPresentationPolicy> All { get; } =
        Policies.Values.ToArray();

    public static bool TryGet(string action, out DebugActionPresentationPolicy policy)
        => Policies.TryGetValue(action, out policy!);

    private static IReadOnlyDictionary<string, DebugActionPresentationPolicy> CreatePolicies()
    {
        var policies = new[]
        {
            Lifecycle("launch"),
            Lifecycle("attach"),
            Administrative("listSessions", fold: true),
            Administrative("getStatus", fold: true),
            Administrative("getHealth", fold: true),
            Administrative("snapshot", fold: true),
            Inspection("inspectStop", fold: true),
            Lifecycle("disconnect"),
            Lifecycle("terminate"),
            Lifecycle("restart"),
            Breakpoint("setSourceBreakpoints"),
            Breakpoint("setFunctionBreakpoints"),
            Breakpoint("setExceptionBreakpoints"),
            Breakpoint("setInstructionBreakpoints"),
            Breakpoint("discoverDataBreakpoint", fold: true),
            Breakpoint("setDataBreakpoints"),
            Breakpoint("getBreakpoints", fold: true),
            Breakpoint("getBreakpointLocations", fold: true),
            Execution("continue"),
            Execution("pause"),
            Execution("stepOver"),
            Execution("stepIn"),
            Execution("stepOut"),
            Execution("stepBack"),
            Execution("reverseContinue"),
            Execution("restartFrame"),
            Execution("goto"),
            Execution("terminateThreads"),
            Inspection("getThreads", fold: true),
            Inspection("getStackTrace", fold: true),
            Inspection("getScopes", fold: true),
            Inspection("getVariables", fold: true),
            Inspection("evaluate"),
            Inspection("getExceptionInfo"),
            Inspection("getModules", fold: true),
            Inspection("getLoadedSources", fold: true),
            Inspection("getSource", fold: true),
            Inspection("getStepInTargets", fold: true),
            Inspection("getGotoTargets", fold: true),
            Inspection("getCompletions", fold: true),
            Inspection("resolveLocation", fold: true),
            Mutation("setVariable"),
            Mutation("setExpression"),
            Inspection("readMemory"),
            Mutation("writeMemory"),
            Inspection("disassemble"),
            Inspection("getOutput", fold: true),
            Administrative("persistOutput", fold: true),
            Administrative("cancelProgress", fold: true)
        };
        return policies.ToDictionary(policy => policy.Action, StringComparer.Ordinal);
    }

    private static DebugActionPresentationPolicy Lifecycle(string action)
        => new(action, DebugPresentationFamily.Lifecycle);

    private static DebugActionPresentationPolicy Breakpoint(string action, bool fold = false)
        => new(action, DebugPresentationFamily.Breakpoint, fold);

    private static DebugActionPresentationPolicy Execution(string action)
        => new(action, DebugPresentationFamily.Execution);

    private static DebugActionPresentationPolicy Inspection(string action, bool fold = false)
        => new(action, DebugPresentationFamily.Inspection, fold);

    private static DebugActionPresentationPolicy Mutation(string action)
        => new(action, DebugPresentationFamily.Mutation);

    private static DebugActionPresentationPolicy Administrative(string action, bool fold)
        => new(action, DebugPresentationFamily.Administrative, fold);
}
