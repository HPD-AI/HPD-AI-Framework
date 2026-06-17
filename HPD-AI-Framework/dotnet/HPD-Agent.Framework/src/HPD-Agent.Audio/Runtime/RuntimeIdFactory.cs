namespace HPD.Agent.Audio.Runtime;

public sealed class RuntimeIdFactory
{
    private long _threadProjection;
    private long _interactionSession;
    private long _ledgerRecord;
    private long _providerRoute;
    private long _providerRouteEpoch;
    private long _outputFlow;
    private long _outputSegment;
    private long _response;
    private long _traceRecord;
    private long _turn;
    private long _turnEvidence;
    private long _transportAdapter;

    public ThreadProjectionId NextThreadProjectionId() => new($"thread-projection-{Interlocked.Increment(ref _threadProjection):D4}");

    public InteractionSessionId NextInteractionSessionId() => new($"interaction-{Interlocked.Increment(ref _interactionSession):D4}");

    public LedgerRecordId NextLedgerRecordId() => new($"ledger-{Interlocked.Increment(ref _ledgerRecord):D4}");

    public ProviderRouteId NextProviderRouteId() => new($"route-{Interlocked.Increment(ref _providerRoute):D4}");

    public ProviderRouteEpochId NextProviderRouteEpochId() => new($"route-epoch-{Interlocked.Increment(ref _providerRouteEpoch):D4}");

    public OutputFlowId NextOutputFlowId() => new($"output-flow-{Interlocked.Increment(ref _outputFlow):D4}");

    public OutputSegmentId NextOutputSegmentId() => new($"output-segment-{Interlocked.Increment(ref _outputSegment):D4}");

    public ResponseId NextResponseId() => new($"response-{Interlocked.Increment(ref _response):D4}");

    public TraceRecordId NextTraceRecordId() => new($"trace-{Interlocked.Increment(ref _traceRecord):D4}");

    public AudioTurnId NextTurnId() => new($"turn-{Interlocked.Increment(ref _turn):D4}");

    public TurnEvidenceId NextTurnEvidenceId() => new($"turn-evidence-{Interlocked.Increment(ref _turnEvidence):D4}");

    public TransportAdapterId NextTransportAdapterId() => new($"transport-{Interlocked.Increment(ref _transportAdapter):D4}");
}
