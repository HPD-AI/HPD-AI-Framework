using HPD.Agent.TUI.Models;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands;

public sealed record CodingCommandCell(
    string CommandId,
    string ToolCallId,
    string FunctionName,
    string Command,
    string DisplayCommand,
    string BaseCommand,
    ExecuteCommandCategory Category,
    string WorkingDirectory,
    string? Shell,
    CodingCommandTranscriptState State,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? BackgroundedAt,
    int? ProcessId,
    int? TimeoutMilliseconds,
    int? ExitCode,
    string? CompletionKind,
    TimeSpan? Duration,
    bool IsBackground,
    bool AutoBackgroundEligible,
    string? BackgroundTaskId,
    bool OutputObserved,
    bool OutputTruncated,
    bool OutputEventsSuppressed,
    bool DrainTimedOut,
    bool BinaryOutputObserved,
    long StdoutBytes,
    long StderrBytes,
    long CombinedOutputBytes,
    long CombinedBytesDiscarded,
    IReadOnlyList<CodingCommandOutputLine> Output,
    CodingCommandOutputWindow OutputWindow,
    IReadOnlyList<CodingCommandArtifactInfo> Artifacts,
    string Summary) : TranscriptCell;

public enum CodingCommandTranscriptState
{
    Running,
    Backgrounded,
    Completed,
    Failed,
    Cancelled,
    TimedOut,
    Exited
}

public enum CodingCommandOutputStream
{
    Stdout,
    Stderr,
    Combined
}

public sealed record CodingCommandOutputLine(
    CodingCommandOutputStream Stream,
    string Text);

public sealed record CodingCommandOutputWindow(
    int HeadLineCount,
    int OmittedLineCount,
    bool Truncated,
    bool Suppressed,
    bool Binary);

public sealed record CodingCommandArtifactInfo(
    string Kind,
    string? Path,
    string? ContentId,
    long? ByteLength);

