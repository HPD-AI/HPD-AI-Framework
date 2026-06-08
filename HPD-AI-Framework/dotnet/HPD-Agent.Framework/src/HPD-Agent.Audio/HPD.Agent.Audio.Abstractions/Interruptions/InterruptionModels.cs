namespace HPD.Agent.Audio.Interruptions;

using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;

public sealed record InterruptionCandidate
{
    public required AudioSessionId SessionId { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public string? Reason { get; init; }
}

public sealed record InterruptionTarget
{
    public OutputFlowId? OutputFlowId { get; init; }

    public ResponseId? ResponseId { get; init; }
}

public sealed record InterruptionRequest
{
    public required InterruptionCandidate Candidate { get; init; }

    public required IReadOnlyList<InterruptionTarget> Targets { get; init; }
}

public sealed record InterruptionAdmission
{
    public required bool Accepted { get; init; }

    public string? Reason { get; init; }
}

public sealed record InterruptionResult
{
    public required InterruptionAdmission Admission { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }
}

public sealed record InterruptionRepairRecord
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public required string OriginalGeneratedText { get; init; }

    public required string CommittedText { get; init; }

    public required string ExcludedText { get; init; }

    public required OutputPlaybackBoundary PlaybackBoundary { get; init; }

    public required InterruptionRepairQuality RepairQuality { get; init; }

    public required ProviderRepairStatus ProviderRepairStatus { get; init; }
}

public enum InterruptionRepairQuality
{
    Exact = 0,
    Approximate = 1,
    LocalOnly = 2,
    Failed = 3
}

public enum ProviderRepairStatus
{
    NotAttempted = 0,
    Succeeded = 1,
    Failed = 2,
    Unsupported = 3
}

public interface IInterruptionProtocol
{
    ValueTask<InterruptionResult> InterruptAsync(
        InterruptionRequest request,
        CancellationToken cancellationToken = default);
}
