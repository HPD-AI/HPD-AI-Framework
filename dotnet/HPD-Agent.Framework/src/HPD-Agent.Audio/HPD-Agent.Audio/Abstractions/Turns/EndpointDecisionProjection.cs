namespace HPD.Agent.Audio.Turns;

public sealed record EndpointDecisionProjection
{
    public required EndpointDecisionProjectionKind Kind { get; init; }

    public required DateTimeOffset DecidedAt { get; init; }

    public AudioTurnId? TurnId { get; init; }

    public string? Reason { get; init; }

    public EndpointCommitProjection? Commit { get; init; }
}

public enum EndpointDecisionProjectionKind
{
    ContinueListening = 0,
    CommitUserTurn = 1,
    RejectCandidate = 2,
    StartInterruptionCandidate = 3,
    CancelInterruptionCandidate = 4
}

public sealed record EndpointCommitProjection
{
    public required AudioTurnId TurnId { get; init; }

    public required string Text { get; init; }

    public required EndpointCommitProjectionReason Reason { get; init; }

    public required IReadOnlyList<EndpointEvidenceId> EvidenceIds { get; init; }
}

public enum EndpointCommitProjectionReason
{
    InputMediaTranscript = 0,
    EndOfTurn = 1,
    ManualCommit = 2,
    ProviderCommit = 3
}

public sealed record EndpointSnapshotProjection
{
    public required AudioSessionId SessionId { get; init; }

    public AudioTurnId? CurrentTurnId { get; init; }

    public IReadOnlyList<EndpointEvidenceProjection> Evidence { get; init; } = [];
}
