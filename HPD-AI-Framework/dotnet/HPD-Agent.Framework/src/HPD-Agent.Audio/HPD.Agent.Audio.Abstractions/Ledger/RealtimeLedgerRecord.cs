namespace HPD.Agent.Audio.Ledger;

public abstract record RealtimeLedgerRecord
{
    public required LedgerRecordId Id { get; init; }

    public required AudioSessionId SessionId { get; init; }

    public required LedgerRecordFamily Family { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }

    public AudioCorrelation Correlation { get; init; } = AudioCorrelation.Empty;
}

public enum LedgerRecordFamily
{
    InputContent = 0,
    Transcript = 1,
    UserTurn = 2,
    AssistantOutput = 3,
    BranchProjection = 4,
    Policy = 5,
    Route = 6,
    InterruptionRepair = 7,
    TtsSynthesis = 8,
    OutputArtifact = 9,
    OutputPlayback = 10
}
