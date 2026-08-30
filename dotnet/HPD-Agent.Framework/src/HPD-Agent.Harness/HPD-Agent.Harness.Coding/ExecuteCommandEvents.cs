using System.Text.Json.Serialization;
using HPD.Agent;
using HPD.Events;
using HPD.Agent.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter<ExecuteCommandCategory>))]
public enum ExecuteCommandCategory
{
    Unknown,
    Build,
    Test,
    Format,
    Lint,
    Git,
    PackageManager,
    Server,
    Search,
    Read,
    FileMutation,
    CodeGeneration
}

[JsonConverter(typeof(JsonStringEnumConverter<ExecuteCommandStreamKind>))]
public enum ExecuteCommandStreamKind
{
    Stdout,
    Stderr
}

[JsonConverter(typeof(JsonStringEnumConverter<ExecuteCommandCompletionKind>))]
public enum ExecuteCommandCompletionKind
{
    Completed,
    TimedOut,
    Cancelled,
    Stopped,
    FailedToStart,
    Faulted
}

[EventDurability(AgentEventDurability.Durable)]
public abstract record ExecuteCommandEvent : AgentEvent
{
    public override EventKind Kind { get; init; } = EventKind.Diagnostic;

    public required string ToolCallId { get; init; }

    public required string FunctionName { get; init; }

    public required string CommandId { get; init; }

    public required string Command { get; init; }

    public required string BaseCommand { get; init; }

    public required ExecuteCommandCategory Category { get; init; }

    public required string WorkingDirectory { get; init; }
}

public sealed record ExecuteCommandProcessStartedEvent : ExecuteCommandEvent
{
    public override EventKind Kind { get; init; } = EventKind.Lifecycle;

    public required string Shell { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required bool Background { get; init; }

    public required bool AutoBackgroundEligible { get; init; }

    public int? ProcessId { get; init; }

    public required int TimeoutMilliseconds { get; init; }
}

public sealed record ExecuteCommandOutputChunkEvent : ExecuteCommandEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;

    public override EventKind Kind { get; init; } = EventKind.Content;


    public required ExecuteCommandStreamKind Stream { get; init; }

    public required string Text { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public required long StreamBytesObserved { get; init; }

    public required long CombinedBytesObserved { get; init; }

    public bool Truncated { get; init; }

    public bool Suppressed { get; init; }

    public bool Binary { get; init; }
}

public sealed record ExecuteCommandProgressEvent : ExecuteCommandEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;

    public override EventKind Kind { get; init; } = EventKind.Diagnostic;


    public required long ElapsedMilliseconds { get; init; }

    public required long StdoutBytes { get; init; }

    public required long StderrBytes { get; init; }

    public required long CombinedOutputBytes { get; init; }

    public required long CombinedBytesDiscarded { get; init; }

    public required bool OutputObserved { get; init; }

    public required bool OutputEventsSuppressed { get; init; }
}

public sealed record ExecuteCommandProcessExitedEvent : ExecuteCommandEvent
{
    public override EventKind Kind { get; init; } = EventKind.Lifecycle;

    public int? ExitCode { get; init; }

    public required ExecuteCommandCompletionKind CompletionKind { get; init; }

    public required long DurationMilliseconds { get; init; }

    public required long StdoutBytes { get; init; }

    public required long StderrBytes { get; init; }

    public required long CombinedOutputBytes { get; init; }

    public required long StdoutBytesDiscarded { get; init; }

    public required long StderrBytesDiscarded { get; init; }

    public required long CombinedBytesDiscarded { get; init; }

    public required bool OutputTruncated { get; init; }

    public required bool OutputDrainTimedOut { get; init; }

    public required bool OutputEventsSuppressed { get; init; }

    public string? StdoutArtifactPath { get; init; }

    public string? StderrArtifactPath { get; init; }

    public string? CombinedOutputArtifactPath { get; init; }

    public string? StdoutContentId { get; init; }

    public string? StderrContentId { get; init; }

    public string? CombinedOutputContentId { get; init; }

    public string? StdoutLocalPath { get; init; }

    public string? StderrLocalPath { get; init; }

    public string? CombinedOutputLocalPath { get; init; }
}

public sealed record ExecuteCommandAutoBackgroundedEvent : ExecuteCommandEvent
{
    public override EventKind Kind { get; init; } = EventKind.Lifecycle;

    public required string OperationId { get; init; }

    public required DateTimeOffset BackgroundedAt { get; init; }

    public required long ElapsedMilliseconds { get; init; }
}
