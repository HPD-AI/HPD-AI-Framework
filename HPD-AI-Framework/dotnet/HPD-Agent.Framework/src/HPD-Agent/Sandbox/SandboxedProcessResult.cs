namespace HPD.Agent.Sandbox;

/// <summary>
/// Result from a sandboxed process execution.
/// </summary>
public sealed record SandboxedProcessResult
{
    public required string ProcessId { get; init; }

    public int? SystemProcessId { get; init; }

    public int? ExitCode { get; init; }

    public required SandboxedProcessCompletionKind CompletionKind { get; init; }

    public required SandboxedProcessCapturedOutput Output { get; init; }

    public bool TimedOut => CompletionKind == SandboxedProcessCompletionKind.TimedOut;

    public bool Cancelled => CompletionKind == SandboxedProcessCompletionKind.Cancelled;

    public IReadOnlyList<SandboxedProcessViolation> Violations { get; init; } =
        Array.Empty<SandboxedProcessViolation>();

    public IReadOnlyDictionary<string, object?> Metadata { get; init; } =
        new Dictionary<string, object?>();
}

public sealed record SandboxedProcessCapturedOutput
{
    public required SandboxedProcessStreamOutput Stdout { get; init; }

    public required SandboxedProcessStreamOutput Stderr { get; init; }

    public bool MergedStandardError { get; init; }

    public bool OutputDrainTimedOut { get; init; }

    public TimeSpan OutputDrainTimeout { get; init; }
}

public sealed record SandboxedProcessStreamOutput
{
    public byte[] CapturedBytes { get; init; } = [];

    public string Text { get; init; } = "";

    public long BytesObserved { get; init; }

    public long BytesCaptured { get; init; }

    public long BytesDiscarded { get; init; }

    public bool Truncated { get; init; }
}

/// <summary>
/// Process-scoped sandbox violation summary that can cross package boundaries.
/// </summary>
public sealed record SandboxedProcessViolation(
    string Type,
    string Message,
    string? Path = null);
