namespace HPD.Agent.Audio.Turns;

public sealed record TurnDecision
{
    public required TurnDecisionKind Kind { get; init; }

    public required DateTimeOffset DecidedAt { get; init; }

    public AudioTurnId? TurnId { get; init; }

    public string? Reason { get; init; }

    public TurnCommit? Commit { get; init; }
}

public enum TurnDecisionKind
{
    ContinueListening = 0,
    CommitUserTurn = 1,
    RejectCandidate = 2,
    StartInterruptionCandidate = 3,
    CancelInterruptionCandidate = 4
}

public sealed record TurnCommit
{
    public required AudioTurnId TurnId { get; init; }

    public required string Text { get; init; }

    public required TurnCommitReason Reason { get; init; }

    public required IReadOnlyList<TurnEvidenceId> EvidenceIds { get; init; }
}

public enum TurnCommitReason
{
    InputMediaTranscript = 0,
    EndOfTurn = 1,
    ManualCommit = 2,
    ProviderCommit = 3
}

public sealed record TurnSnapshot
{
    public required AudioSessionId SessionId { get; init; }

    public AudioTurnId? CurrentTurnId { get; init; }

    public IReadOnlyList<TurnEvidence> Evidence { get; init; } = [];
}
