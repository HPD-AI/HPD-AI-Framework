using HPD.Events;

namespace HPD.Agent.Sandbox;

public abstract record SandboxedProcessRuntimeEvent : Event
{
    public required string ProcessId { get; init; }

    public override EventKind Kind { get; init; } = EventKind.Diagnostic;

    public override EventChannel Channel { get; init; } = EventChannel.Synchronous;
}

public sealed record SandboxedProcessStartedEvent : SandboxedProcessRuntimeEvent
{
    public override EventKind Kind { get; init; } = EventKind.Lifecycle;

    public int? SystemProcessId { get; init; }

    public required string FileName { get; init; }
}

public sealed record SandboxedProcessOutputEvent : SandboxedProcessRuntimeEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Streaming;

    public override EventKind Kind { get; init; } = EventKind.Content;

    public required SandboxedProcessStream Stream { get; init; }

    public required ReadOnlyMemory<byte> Bytes { get; init; }
}

public sealed record SandboxedProcessExitedEvent : SandboxedProcessRuntimeEvent
{
    public override EventKind Kind { get; init; } = EventKind.Lifecycle;

    public int? ExitCode { get; init; }

    public required SandboxedProcessCompletionKind CompletionKind { get; init; }

    public required TimeSpan Duration { get; init; }

    public required SandboxedProcessCapturedOutput Output { get; init; }

    public IReadOnlyList<SandboxedProcessViolation> Violations { get; init; } =
        Array.Empty<SandboxedProcessViolation>();
}

public sealed record SandboxedProcessFailedEvent : SandboxedProcessRuntimeEvent
{
    public override EventKind Kind { get; init; } = EventKind.Diagnostic;

    public required string Message { get; init; }
}

public enum SandboxedProcessStream
{
    Stdout,
    Stderr
}

public enum SandboxedProcessCompletionKind
{
    Completed,
    FailedToStart,
    TimedOut,
    Cancelled,
    Stopped,
    Killed,
    Faulted
}

public enum SandboxedProcessStopReason
{
    Requested,
    Timeout,
    Cancelled,
    RuntimeStopping,
    Disposed
}
