namespace HPD.Agent.Audio.Trace;

public abstract record RealtimeAudioTraceRecord
{
    public required TraceRecordId Id { get; init; }

    public required AudioSessionId SessionId { get; init; }

    public required RealtimeAudioTraceRecordFamily Family { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }

    public AudioCorrelation Correlation { get; init; } = AudioCorrelation.Empty;
}

public enum RealtimeAudioTraceRecordFamily
{
    InputContent = 0,
    Policy = 1,
    Route = 2,
    InteractionUpdate = 3,
    TurnDecision = 4,
    Ledger = 5,
    ThreadProjection = 6,
    Clock = 7,
    Error = 8,
    AssistantOutput = 9,
    InterruptionRepair = 10,
    TtsSynthesis = 11,
    OutputArtifact = 12,
    OutputPlayback = 13,
    StructEventSample = 14,
    Transport = 15
}
