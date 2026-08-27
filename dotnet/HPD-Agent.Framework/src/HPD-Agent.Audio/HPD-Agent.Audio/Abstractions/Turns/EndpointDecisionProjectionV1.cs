namespace HPD.Agent.Audio.Turns;

public sealed record EndpointDecisionProjectionV1
{
    public required EndpointDecisionProjectionKindV1 Kind { get; init; }

    public required DateTimeOffset DecidedAt { get; init; }

    public AudioTurnId? TurnId { get; init; }

    public string? Reason { get; init; }

    public EndpointCommitProjectionV1? Commit { get; init; }
}

public enum EndpointDecisionProjectionKindV1
{
    ContinueListening = 0,
    CommitUserTurn = 1,
    RejectCandidate = 2,
    StartInterruptionCandidate = 3,
    CancelInterruptionCandidate = 4
}

public sealed record EndpointCommitProjectionV1
{
    public required AudioTurnId TurnId { get; init; }

    public required string Text { get; init; }

    public required EndpointCommitProjectionReasonV1 Reason { get; init; }

    public required IReadOnlyList<EndpointEvidenceIdV1> EvidenceIds { get; init; }
}

public enum EndpointCommitProjectionReasonV1
{
    InputMediaTranscript = 0,
    EndOfTurn = 1,
    ManualCommit = 2,
    ProviderCommit = 3
}

public sealed record EndpointSnapshotProjectionV1
{
    public required AudioSessionId SessionId { get; init; }

    public AudioTurnId? CurrentTurnId { get; init; }

    public IReadOnlyList<EndpointEvidenceProjectionV1> Evidence { get; init; } = [];
}
