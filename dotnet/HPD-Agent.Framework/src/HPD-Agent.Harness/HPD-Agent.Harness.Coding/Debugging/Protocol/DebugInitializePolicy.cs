using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;

namespace HPD.Agent.ToolHarness.Coding.Debugging.Protocol;

public sealed record DebugInitializeFeatures
{
    public bool RunInTerminalHandler { get; init; }
    public bool ProgressHandling { get; init; }
    public bool InvalidatedEventHandling { get; init; }
    public bool MemoryOperations { get; init; }
    public bool MemoryEventHandling { get; init; }
    public bool ShellArgumentAuthorization { get; init; }
    public bool StartDebuggingHandler { get; init; }
    public bool AnsiRendering { get; init; }
    public bool VariableTypeRendering { get; init; } = true;
    public bool VariablePaging { get; init; } = true;
}

public sealed class DebugInitializePolicy
{
    public InitializeRequestArguments Create(
        string adapterId,
        DebugInitializeFeatures features,
        string? locale = null,
        string pathFormat = "path")
    {
        if (string.IsNullOrWhiteSpace(adapterId)) throw new ArgumentException("An adapter ID is required.", nameof(adapterId));
        if (pathFormat is not ("path" or "uri")) throw new ArgumentOutOfRangeException(nameof(pathFormat));
        return new()
        {
            ClientID = "hpd-agent",
            ClientName = "HPD Agent",
            AdapterID = adapterId,
            Locale = locale,
            LinesStartAt1 = true,
            ColumnsStartAt1 = true,
            PathFormat = pathFormat,
            SupportsVariableType = features.VariableTypeRendering,
            SupportsVariablePaging = features.VariablePaging,
            SupportsRunInTerminalRequest = features.RunInTerminalHandler,
            SupportsMemoryReferences = features.MemoryOperations,
            SupportsProgressReporting = features.ProgressHandling,
            SupportsInvalidatedEvent = features.InvalidatedEventHandling,
            SupportsMemoryEvent = features.MemoryOperations && features.MemoryEventHandling,
            SupportsArgsCanBeInterpretedByShell = features.RunInTerminalHandler && features.ShellArgumentAuthorization,
            SupportsStartDebuggingRequest = features.StartDebuggingHandler,
            SupportsANSIStyling = features.AnsiRendering
        };
    }
}
