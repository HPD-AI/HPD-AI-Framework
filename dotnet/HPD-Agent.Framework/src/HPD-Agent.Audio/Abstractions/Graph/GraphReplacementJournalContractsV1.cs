using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal enum GraphReplacementJournalCommandKindV1 : ushort { Prepare = 1, Commit = 2, SettleSource = 3 }
internal enum GraphReplacementJournalOutcomeV1 : ushort
{ Prepared = 1, Rejected = 2, Conflict = 3, GenerationReplaced = 4, Committed = 5, SourceSettled = 6 }

internal abstract record GraphReplacementJournalCommandV1
{
    private GraphReplacementJournalCommandV1() { }
    internal abstract GraphReplacementJournalCommandKindV1 Kind { get; }
    internal abstract OperationId OperationId { get; }
    internal abstract JournalPositionV1 ExpectedPredecessor { get; }

    internal sealed record Prepare(OperationId Operation, JournalPositionV1 Predecessor,
        Hash256 SourceFingerprint, GraphTopologyPlanV1 TargetTopology, JournalPositionV1 TargetGrantFact,
        ExpectedAuthorityVectorV1 CurrentAuthority, MonotonicStampV1 ObservedAt,
        MonotonicStampV1 OverlapDeadline) : GraphReplacementJournalCommandV1
    {
        internal override GraphReplacementJournalCommandKindV1 Kind => GraphReplacementJournalCommandKindV1.Prepare;
        internal override OperationId OperationId => Operation;
        internal override JournalPositionV1 ExpectedPredecessor => Predecessor;
    }

    internal sealed record Commit(OperationId Operation, JournalPositionV1 Predecessor) : GraphReplacementJournalCommandV1
    {
        internal override GraphReplacementJournalCommandKindV1 Kind => GraphReplacementJournalCommandKindV1.Commit;
        internal override OperationId OperationId => Operation;
        internal override JournalPositionV1 ExpectedPredecessor => Predecessor;
    }

    internal sealed record SettleSource(OperationId Operation, JournalPositionV1 Predecessor,
        JournalPositionV1 SourceSettlementFact) : GraphReplacementJournalCommandV1
    {
        internal override GraphReplacementJournalCommandKindV1 Kind => GraphReplacementJournalCommandKindV1.SettleSource;
        internal override OperationId OperationId => Operation;
        internal override JournalPositionV1 ExpectedPredecessor => Predecessor;
    }
}

internal sealed record GraphTopologyInstalledV1(GraphTopologyPlanV1 Topology,
    Hash256 TopologyFingerprint, JournalPositionV1 ActiveSourceGrantFact,
    ExpectedAuthorityVectorV1 CurrentAuthority);

internal sealed record GraphReplacementTargetArmV1(GraphTopologyPlanV1 Topology, JournalPositionV1 GrantFact);
internal sealed record GraphReplacementIdentityArmV1(OperationId OperationId, JournalPositionV1 PrepareCommandFact);
internal sealed record GraphReplacementCommitArmV1(JournalPositionV1 CommitCommandFact,
    JournalPositionV1 GenerationChangedFact);
internal sealed record GraphReplacementSettlementArmV1(JournalPositionV1 SettleCommandFact,
    JournalPositionV1 SourceSettlementFact);

internal sealed record GraphReplacementSnapshotV1(GraphReplacementPhaseV1 Phase,
    GraphTopologyPlanV1 SourceTopology, JournalPositionV1 SourceGrantFact,
    GraphReplacementTargetArmV1? Target, ExpectedAuthorityVectorV1 CurrentAuthority,
    JournalPositionV1 LastGraphFact, GraphReplacementIdentityArmV1? Replacement,
    GraphReplacementCommitArmV1? Commit, GraphReplacementSettlementArmV1? Settlement);

internal sealed record GraphReplacementFactV1(JournalPositionV1 CommandFact,
    JournalPositionV1 ExpectedPredecessor, JournalPositionV1 ActualPredecessor,
    GraphReplacementJournalOutcomeV1 Outcome, GraphReplacementSnapshotV1 ResultingSnapshot,
    BoundedAscii? SafeCode);

internal class GraphOwnerPayloadV1
{
    private readonly byte[] _body;
    internal GraphOwnerPayloadV1(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority,
        ReadOnlySpan<byte> body)
    {
        if (!session.IsValid || expectedAuthority is null || expectedAuthority.Session != session)
            throw new ArgumentException("A graph payload requires one exact session authority vector.");
        if (body.Length is 0 or > GraphReplacementCodecsV1.MaximumBodyBytes)
            throw new ArgumentOutOfRangeException(nameof(body));
        Session = session; ExpectedAuthority = expectedAuthority; _body = body.ToArray();
    }
    internal SessionAuthorityStampV1 Session { get; }
    internal ExpectedAuthorityVectorV1 ExpectedAuthority { get; }
    internal ReadOnlyMemory<byte> Body => _body;
}

internal sealed class GraphMutationCommandV1 : GraphOwnerPayloadV1
{
    internal GraphMutationCommandV1(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority,
        ReadOnlySpan<byte> body) : base(session, expectedAuthority, body) { }
}

internal sealed class GraphTopologyInstalledFactV1 : GraphOwnerPayloadV1
{
    internal GraphTopologyInstalledFactV1(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority,
        ReadOnlySpan<byte> body) : base(session, expectedAuthority, body) { }
}
