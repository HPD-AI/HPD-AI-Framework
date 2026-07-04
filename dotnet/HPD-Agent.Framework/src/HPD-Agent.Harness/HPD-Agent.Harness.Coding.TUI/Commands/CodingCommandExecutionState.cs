namespace HPD.Agent.ToolHarness.Coding.TUI.Commands;

internal enum CodingCommandDisplayState
{
    Running,
    Backgrounded,
    Completed,
    Failed,
    Cancelled,
    TimedOut,
    Exited
}

internal sealed class CodingCommandExecutionState
{
    public CodingCommandExecutionState(ExecuteCommandEvent evt)
    {
        CommandId = evt.CommandId;
        ToolCallId = evt.ToolCallId;
        ApplyBase(evt);
    }

    public CodingCommandExecutionState(
        string commandId,
        string toolCallId,
        string functionName,
        string command,
        string baseCommand,
        ExecuteCommandCategory category,
        string workingDirectory)
    {
        CommandId = commandId;
        ToolCallId = toolCallId;
        FunctionName = functionName;
        Command = command;
        DisplayCommand = CodingCommandDisplayFormatter.Format(command);
        BaseCommand = baseCommand;
        Category = category;
        WorkingDirectory = workingDirectory;
    }

    public string CommandId { get; }

    public string ToolCallId { get; private set; }

    public string FunctionName { get; private set; } = "";

    public string Command { get; private set; } = "";

    public string DisplayCommand { get; private set; } = "";

    public string BaseCommand { get; private set; } = "";

    public ExecuteCommandCategory Category { get; private set; }

    public string WorkingDirectory { get; private set; } = "";

    public string? Shell { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? BackgroundedAt { get; set; }

    public int? ProcessId { get; set; }

    public int? TimeoutMilliseconds { get; set; }

    public int? ExitCode { get; set; }

    public ExecuteCommandCompletionKind? CompletionKind { get; set; }

    public long? DurationMilliseconds { get; set; }

    public bool IsBackground { get; set; }

    public bool AutoBackgroundEligible { get; set; }

    public string? BackgroundHandleId { get; set; }

    public bool OutputObserved { get; set; }

    public bool OutputTruncated { get; set; }

    public bool OutputEventsSuppressed { get; set; }

    public bool DrainTimedOut { get; set; }

    public bool BinaryOutputObserved { get; set; }

    public long StdoutBytes { get; set; }

    public long StderrBytes { get; set; }

    public long CombinedOutputBytes { get; set; }

    public long CombinedBytesDiscarded { get; set; }

    public CodingCommandDisplayState DisplayState { get; set; } = CodingCommandDisplayState.Running;

    public string? LastTranscriptSnapshotKey { get; set; }

    public CodingCommandOutputBuffer Output { get; } = new();

    public CodingCommandArtifacts Artifacts { get; } = new();

    public bool IsActive => DisplayState is CodingCommandDisplayState.Running or CodingCommandDisplayState.Backgrounded;

    public void ApplyBase(ExecuteCommandEvent evt)
    {
        ToolCallId = evt.ToolCallId;
        FunctionName = evt.FunctionName;
        Command = evt.Command;
        DisplayCommand = CodingCommandDisplayFormatter.Format(evt.Command);
        BaseCommand = evt.BaseCommand;
        Category = evt.Category;
        WorkingDirectory = evt.WorkingDirectory;
    }
}
