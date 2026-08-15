using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal enum GraphRuntimeCommandKindV1 : ushort { Activate = 1, Retire = 2 }
internal enum GraphRuntimePhaseV1 : ushort { Active = 1, Retired = 2 }
internal enum GraphRuntimeOutcomeV1 : ushort { Activated = 1, Retired = 2, Rejected = 3, Conflict = 4, GenerationReplaced = 5 }

internal abstract record GraphRuntimeCommandV1
{
    private GraphRuntimeCommandV1() { }
    internal abstract GraphRuntimeCommandKindV1 Kind { get; }
    internal abstract OperationId OperationId { get; }
    internal abstract JournalPositionV1 ExpectedPredecessor { get; }
    internal abstract Hash256 EffectRequestHash { get; }

    internal sealed record Activate : GraphRuntimeCommandV1
    {
        internal Activate(OperationId operation, JournalPositionV1 predecessor, JournalPositionV1 graphAuthorityFact,
            Hash256 topologyFingerprint, GraphGenerationId graphGeneration, JournalPositionV1 capacityGrantFact, Hash256 requestHash)
        {
            if (!operation.IsValid || !predecessor.IsValid || !graphAuthorityFact.IsValid || topologyFingerprint == default ||
                !graphGeneration.IsValid || !capacityGrantFact.IsValid || requestHash == default ||
                predecessor.Session != graphAuthorityFact.Session || predecessor.Session != capacityGrantFact.Session)
                throw new ArgumentException("Activate requires one valid session and complete graph authority proof.");
            OperationId = operation; ExpectedPredecessor = predecessor; GraphAuthorityFact = graphAuthorityFact;
            TopologyFingerprint = topologyFingerprint; GraphGeneration = graphGeneration; CapacityGrantFact = capacityGrantFact;
            EffectRequestHash = requestHash;
        }
        internal override GraphRuntimeCommandKindV1 Kind => GraphRuntimeCommandKindV1.Activate;
        internal override OperationId OperationId { get; }
        internal override JournalPositionV1 ExpectedPredecessor { get; }
        internal JournalPositionV1 GraphAuthorityFact { get; }
        internal Hash256 TopologyFingerprint { get; }
        internal GraphGenerationId GraphGeneration { get; }
        internal JournalPositionV1 CapacityGrantFact { get; }
        internal override Hash256 EffectRequestHash { get; }
    }

    internal sealed record Retire : GraphRuntimeCommandV1
    {
        internal Retire(OperationId operation, JournalPositionV1 predecessor, JournalPositionV1 activeRuntimeFact, Hash256 requestHash)
        {
            if (!operation.IsValid || !predecessor.IsValid || !activeRuntimeFact.IsValid || requestHash == default ||
                predecessor.Session != activeRuntimeFact.Session)
                throw new ArgumentException("Retire requires one valid session and an active runtime fact.");
            OperationId = operation; ExpectedPredecessor = predecessor; ActiveRuntimeFact = activeRuntimeFact; EffectRequestHash = requestHash;
        }
        internal override GraphRuntimeCommandKindV1 Kind => GraphRuntimeCommandKindV1.Retire;
        internal override OperationId OperationId { get; }
        internal override JournalPositionV1 ExpectedPredecessor { get; }
        internal JournalPositionV1 ActiveRuntimeFact { get; }
        internal override Hash256 EffectRequestHash { get; }
    }
}

internal sealed record GraphRuntimeRetirementV1
{
    internal GraphRuntimeRetirementV1(OperationId operationId, JournalPositionV1 retireCommandFact)
    { if (!operationId.IsValid || !retireCommandFact.IsValid) throw new ArgumentException("A valid retirement is required."); OperationId = operationId; RetireCommandFact = retireCommandFact; }
    internal OperationId OperationId { get; }
    internal JournalPositionV1 RetireCommandFact { get; }
}

internal sealed record GraphRuntimeSnapshotV1
{
    internal GraphRuntimeSnapshotV1(GraphRuntimePhaseV1 phase, GraphGenerationId graphGeneration, Hash256 topologyFingerprint,
        JournalPositionV1 capacityGrantFact, ExpectedAuthorityVectorV1 currentAuthority, OperationId activationOperationId,
        JournalPositionV1 activationFact, JournalPositionV1 lastRuntimeFact, GraphRuntimeRetirementV1? retirement)
    {
        if (!Enum.IsDefined(phase) || !graphGeneration.IsValid || topologyFingerprint == default || !capacityGrantFact.IsValid ||
            currentAuthority is null || !GraphReplacementReducerV1.HasExactGraph(currentAuthority, graphGeneration) ||
            !activationOperationId.IsValid || !activationFact.IsValid || !lastRuntimeFact.IsValid ||
            capacityGrantFact.Session != currentAuthority.Session || activationFact.Session != currentAuthority.Session ||
            lastRuntimeFact.Session != currentAuthority.Session ||
            (phase == GraphRuntimePhaseV1.Active && (retirement is not null || activationFact != lastRuntimeFact)) ||
            (phase == GraphRuntimePhaseV1.Retired && (retirement is null || retirement.RetireCommandFact.Session != currentAuthority.Session ||
             activationFact.Sequence >= retirement.RetireCommandFact.Sequence || retirement.RetireCommandFact.Sequence >= lastRuntimeFact.Sequence)))
            throw new ArgumentException("The runtime snapshot invariants are not satisfied.");
        Phase = phase; GraphGeneration = graphGeneration; TopologyFingerprint = topologyFingerprint; CapacityGrantFact = capacityGrantFact;
        CurrentAuthority = currentAuthority; ActivationOperationId = activationOperationId; ActivationFact = activationFact;
        LastRuntimeFact = lastRuntimeFact; Retirement = retirement;
    }
    internal GraphRuntimePhaseV1 Phase { get; }
    internal GraphGenerationId GraphGeneration { get; }
    internal Hash256 TopologyFingerprint { get; }
    internal JournalPositionV1 CapacityGrantFact { get; }
    internal ExpectedAuthorityVectorV1 CurrentAuthority { get; }
    internal OperationId ActivationOperationId { get; }
    internal JournalPositionV1 ActivationFact { get; }
    internal JournalPositionV1 LastRuntimeFact { get; }
    internal GraphRuntimeRetirementV1? Retirement { get; }
}

internal sealed record GraphRuntimeFactV1
{
    internal GraphRuntimeFactV1(JournalPositionV1 commandFact, JournalPositionV1 expectedPredecessor, JournalPositionV1 actualPredecessor,
        GraphRuntimeOutcomeV1 outcome, GraphRuntimeSnapshotV1? snapshot, Hash256? receiptHash, BoundedAscii? safeCode)
    {
        var success = outcome is GraphRuntimeOutcomeV1.Activated or GraphRuntimeOutcomeV1.Retired;
        if (!commandFact.IsValid || !expectedPredecessor.IsValid || !actualPredecessor.IsValid || !Enum.IsDefined(outcome) ||
            commandFact.Session != expectedPredecessor.Session || commandFact.Session != actualPredecessor.Session ||
            expectedPredecessor.Sequence >= commandFact.Sequence || actualPredecessor.Sequence >= commandFact.Sequence ||
            (success && (snapshot is null || receiptHash is null || receiptHash.Value == default || safeCode is not null ||
             snapshot.CurrentAuthority.Session != commandFact.Session)) ||
            (outcome == GraphRuntimeOutcomeV1.Activated && snapshot?.Phase != GraphRuntimePhaseV1.Active) ||
            (outcome == GraphRuntimeOutcomeV1.Retired && (snapshot?.Phase != GraphRuntimePhaseV1.Retired ||
             snapshot.Retirement?.RetireCommandFact != commandFact)) ||
            (!success && (receiptHash is not null || safeCode is null || !safeCode.Value.IsValid)) ||
            (outcome == GraphRuntimeOutcomeV1.Conflict && safeCode?.ToString() != "runtime-predecessor-conflict") ||
            (outcome == GraphRuntimeOutcomeV1.GenerationReplaced && safeCode?.ToString() != "generation-replaced"))
            throw new ArgumentException("The runtime fact outcome invariants are not satisfied.");
        CommandFact = commandFact; ExpectedPredecessor = expectedPredecessor; ActualPredecessor = actualPredecessor;
        Outcome = outcome; ResultingSnapshot = snapshot; EffectReceiptHash = receiptHash; SafeCode = safeCode;
    }
    internal JournalPositionV1 CommandFact { get; }
    internal JournalPositionV1 ExpectedPredecessor { get; }
    internal JournalPositionV1 ActualPredecessor { get; }
    internal GraphRuntimeOutcomeV1 Outcome { get; }
    internal GraphRuntimeSnapshotV1? ResultingSnapshot { get; }
    internal Hash256? EffectReceiptHash { get; }
    internal BoundedAscii? SafeCode { get; }
}

internal sealed class GraphRuntimeOwnerPayloadV1
{
    private readonly byte[] _body;
    internal GraphRuntimeOwnerPayloadV1(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    {
        if (!session.IsValid || expectedAuthority is null || expectedAuthority.Session != session ||
            expectedAuthority.Axes.Count(entry => entry.AxisId == AuthorityAxisId.Graph && entry.Value is AuthorityAxisValueV1.Graph) != 1 ||
            body.Length is 0 or > 65536)
            throw new ArgumentException("A bounded graph-runtime payload and exactly one typed Graph axis are required.");
        Session = session; ExpectedAuthority = expectedAuthority; _body = body.ToArray();
    }
    internal SessionAuthorityStampV1 Session { get; }
    internal ExpectedAuthorityVectorV1 ExpectedAuthority { get; }
    internal ReadOnlyMemory<byte> Body => _body;
}
