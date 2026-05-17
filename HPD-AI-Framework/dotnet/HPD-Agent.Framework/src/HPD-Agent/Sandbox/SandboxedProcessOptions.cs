namespace HPD.Agent.Sandbox;

using HPD.Events;

/// <summary>
/// Execution options for a sandboxed process.
/// </summary>
public sealed record SandboxedProcessOptions
{
    public string? StandardInput { get; init; }

    public TimeSpan? Timeout { get; init; }

    public bool CaptureStandardOutput { get; init; } = true;

    public bool CaptureStandardError { get; init; } = true;

    public bool MergeStandardError { get; init; }

    public bool AllowBackgroundExecution { get; init; }

    public bool KillProcessTreeOnCancel { get; init; } = true;

    public bool RequirePty { get; init; }

    public int? MaxCapturedBytesPerStream { get; init; }

    public TimeSpan OutputDrainTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public IEventCoordinator? EventCoordinator { get; init; }
}
