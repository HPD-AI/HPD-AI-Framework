using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal enum GraphReplacementJournalCommandKind : ushort { Prepare = 1, Commit = 2, SettleSource = 3 }
internal enum GraphReplacementJournalOutcome : ushort
{ Prepared = 1, Rejected = 2, Conflict = 3, GenerationReplaced = 4, Committed = 5, SourceSettled = 6 }

internal abstract record GraphReplacementJournalCommand
{
    private GraphReplacementJournalCommand() { }
    internal abstract GraphReplacementJournalCommandKind Kind { get; }
    internal abstract OperationId OperationId { get; }
    internal abstract JournalPositionV1 ExpectedPredecessor { get; }

    internal sealed record Prepare(OperationId Operation, JournalPositionV1 Predecessor,
        Hash256 SourceFingerprint, GraphTopologyPlan TargetTopology, JournalPositionV1 TargetGrantFact,
        ExpectedAuthorityVectorV1 CurrentAuthority, MonotonicStampV1 ObservedAt,
        MonotonicStampV1 OverlapDeadline) : GraphReplacementJournalCommand
    {
        internal override GraphReplacementJournalCommandKind Kind => GraphReplacementJournalCommandKind.Prepare;
        internal override OperationId OperationId => Operation;
        internal override JournalPositionV1 ExpectedPredecessor => Predecessor;
    }

    internal sealed record Commit(OperationId Operation, JournalPositionV1 Predecessor) : GraphReplacementJournalCommand
    {
        internal override GraphReplacementJournalCommandKind Kind => GraphReplacementJournalCommandKind.Commit;
        internal override OperationId OperationId => Operation;
        internal override JournalPositionV1 ExpectedPredecessor => Predecessor;
    }

    internal sealed record SettleSource(OperationId Operation, JournalPositionV1 Predecessor,
        JournalPositionV1 SourceSettlementFact) : GraphReplacementJournalCommand
    {
        internal override GraphReplacementJournalCommandKind Kind => GraphReplacementJournalCommandKind.SettleSource;
        internal override OperationId OperationId => Operation;
        internal override JournalPositionV1 ExpectedPredecessor => Predecessor;
    }
}

internal sealed record GraphTopologyInstalled(GraphTopologyPlan Topology,
    Hash256 TopologyFingerprint, JournalPositionV1 ActiveSourceGrantFact,
    ExpectedAuthorityVectorV1 CurrentAuthority);

internal sealed record GraphReplacementTargetArm(GraphTopologyPlan Topology, JournalPositionV1 GrantFact);
internal sealed record GraphReplacementIdentityArm(OperationId OperationId, JournalPositionV1 PrepareCommandFact);
internal sealed record GraphReplacementCommitArm(JournalPositionV1 CommitCommandFact,
    JournalPositionV1 GenerationChangedFact);
internal sealed record GraphReplacementSettlementArm(JournalPositionV1 SettleCommandFact,
    JournalPositionV1 SourceSettlementFact);

internal sealed record GraphReplacementSnapshot(GraphReplacementPhase Phase,
    GraphTopologyPlan SourceTopology, JournalPositionV1 SourceGrantFact,
    GraphReplacementTargetArm? Target, ExpectedAuthorityVectorV1 CurrentAuthority,
    JournalPositionV1 LastGraphFact, GraphReplacementIdentityArm? Replacement,
    GraphReplacementCommitArm? Commit, GraphReplacementSettlementArm? Settlement);

internal sealed record GraphReplacementFact(JournalPositionV1 CommandFact,
    JournalPositionV1 ExpectedPredecessor, JournalPositionV1 ActualPredecessor,
    GraphReplacementJournalOutcome Outcome, GraphReplacementSnapshot ResultingSnapshot,
    BoundedAscii? SafeCode);

internal class GraphOwnerPayload
{
    private readonly byte[] _body;
    internal GraphOwnerPayload(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority,
        ReadOnlySpan<byte> body)
    {
        if (!session.IsValid || expectedAuthority is null || expectedAuthority.Session != session)
            throw new ArgumentException("A graph payload requires one exact session authority vector.");
        if (body.Length is 0 or > GraphReplacementCodecs.MaximumBodyBytes)
            throw new ArgumentOutOfRangeException(nameof(body));
        Session = session; ExpectedAuthority = expectedAuthority; _body = body.ToArray();
    }
    internal SessionAuthorityStampV1 Session { get; }
    internal ExpectedAuthorityVectorV1 ExpectedAuthority { get; }
    internal ReadOnlyMemory<byte> Body => _body;
}

internal sealed class GraphMutationCommand : GraphOwnerPayload
{
    internal GraphMutationCommand(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority,
        ReadOnlySpan<byte> body) : base(session, expectedAuthority, body) { }
}

internal sealed class GraphTopologyInstalledFact : GraphOwnerPayload
{
    internal GraphTopologyInstalledFact(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority,
        ReadOnlySpan<byte> body) : base(session, expectedAuthority, body) { }
}
