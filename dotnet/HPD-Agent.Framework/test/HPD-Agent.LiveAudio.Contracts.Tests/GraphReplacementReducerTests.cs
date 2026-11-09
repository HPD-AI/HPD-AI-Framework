using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphReplacementReducerTests
{
    [Fact]
    public void ResultMayFollowInterleavedJournalFactsButNeverMoveBackward()
    {
        var fixture = new Fixture();
        var prepared = Assert.IsType<GraphReplacementReductionResult.Applied>(
            GraphReplacementReducer.Apply(fixture.State, fixture.Prepare(), fixture.Position(15)));
        Assert.Equal(15, prepared.State.LastFact.Sequence);

        var committed = Assert.IsType<GraphReplacementReductionResult.Applied>(
            GraphReplacementReducer.Apply(prepared.State,
                new GraphReplacementCommand.Commit(fixture.Operation, fixture.Position(15)), fixture.Position(21)));
        Assert.Equal(21, committed.State.LastFact.Sequence);
        Assert.IsType<GraphReplacementReductionResult.Rejected>(GraphReplacementReducer.Apply(
            committed.State, new GraphReplacementCommand.SettleSource(fixture.Operation,
                fixture.Position(21), fixture.Settlement()), fixture.Position(20)));
        Assert.IsType<GraphReplacementReductionResult.Rejected>(GraphReplacementReducer.Apply(
            committed.State, new GraphReplacementCommand.SettleSource(fixture.Operation,
                fixture.Position(21), fixture.Settlement()), fixture.Position(21)));

        var terminal = GraphReplacementState.Create(fixture.Source,
            fixture.Grant(fixture.SourceGrantId, CapacityGrantStateV1.Active), fixture.Authority,
            fixture.Position(long.MaxValue));
        Assert.IsType<GraphReplacementReductionResult.Rejected>(GraphReplacementReducer.Apply(
            terminal, fixture.Prepare(expected: fixture.Position(long.MaxValue)), fixture.Position(long.MaxValue)));
    }

    [Fact]
    public void Prepare_commit_and_settle_change_only_the_intended_state()
    {
        var fixture = new Fixture();
        var prepared = Assert.IsType<GraphReplacementReductionResult.Applied>(
            GraphReplacementReducer.Apply(fixture.State, fixture.Prepare(), fixture.Position(11))).State;
        Assert.Equal(GraphReplacementPhase.Prepared, prepared.Phase);
        Assert.Equal(fixture.SourceGeneration, Graph(prepared.Authority));

        var committed = Assert.IsType<GraphReplacementReductionResult.Applied>(
            GraphReplacementReducer.Apply(prepared, new GraphReplacementCommand.Commit(
                fixture.Operation, fixture.Position(11)), fixture.Position(12))).State;
        Assert.Equal(GraphReplacementPhase.Committed, committed.Phase);
        Assert.Equal(fixture.TargetGeneration, Graph(committed.Authority));

        var settled = Assert.IsType<GraphReplacementReductionResult.Applied>(
            GraphReplacementReducer.Apply(committed, new GraphReplacementCommand.SettleSource(
                fixture.Operation, fixture.Position(12), fixture.Settlement()), fixture.Position(13))).State;
        Assert.Equal(GraphReplacementPhase.SourceSettled, settled.Phase);
        Assert.Equal(fixture.TargetGeneration, Graph(settled.Authority));
    }

    [Fact]
    public void Exact_retries_are_idempotent_and_changed_identity_conflicts()
    {
        var fixture = new Fixture();
        var command = fixture.Prepare();
        var prepared = Assert.IsType<GraphReplacementReductionResult.Applied>(
            GraphReplacementReducer.Apply(fixture.State, command, fixture.Position(11))).State;

        Assert.IsType<GraphReplacementReductionResult.Idempotent>(
            GraphReplacementReducer.Apply(prepared, command, fixture.Position(12)));
        Assert.IsType<GraphReplacementReductionResult.Conflict>(
            GraphReplacementReducer.Apply(prepared, fixture.Prepare(OperationId.Create()), fixture.Position(12)));
    }

    [Fact]
    public void Stale_predecessor_source_and_generation_fail_closed()
    {
        var fixture = new Fixture();
        Assert.IsType<GraphReplacementReductionResult.Conflict>(GraphReplacementReducer.Apply(
            fixture.State, fixture.Prepare(expected: fixture.Position(9)), fixture.Position(11)));
        Assert.IsType<GraphReplacementReductionResult.Rejected>(GraphReplacementReducer.Apply(
            fixture.State, fixture.Prepare(sourceFingerprint: Hash(90)), fixture.Position(11)));

        var otherAuthority = ExpectedAuthorityVectorV1.Create(fixture.Session,
            [new AuthorityAxisValueV1.Graph(GraphGenerationId.Create())]);
        Assert.IsType<GraphReplacementReductionResult.GenerationReplaced>(GraphReplacementReducer.Apply(
            fixture.State, fixture.Prepare(authority: otherAuthority), fixture.Position(11)));
    }

    [Theory]
    [InlineData(30_000_000_000UL, true)]
    [InlineData(30_000_000_001UL, false)]
    public void Overlap_bound_is_exact(ulong delta, bool accepted)
    {
        var fixture = new Fixture();
        var result = GraphReplacementReducer.Apply(fixture.State,
            fixture.Prepare(deadline: new MonotonicStampV1(fixture.Clock, fixture.Boot, 100 + delta)), fixture.Position(11));
        Assert.Equal(accepted, result is GraphReplacementReductionResult.Applied);
    }

    [Fact]
    public void Incomparable_expired_and_inactive_grants_reject()
    {
        var fixture = new Fixture();
        Assert.IsType<GraphReplacementReductionResult.Rejected>(GraphReplacementReducer.Apply(fixture.State,
            fixture.Prepare(deadline: new MonotonicStampV1(ClockDomainId.Create(), fixture.Boot, 101)), fixture.Position(11)));
        Assert.IsType<GraphReplacementReductionResult.Rejected>(GraphReplacementReducer.Apply(fixture.State,
            fixture.Prepare(deadline: new MonotonicStampV1(fixture.Clock, fixture.Boot, 99)), fixture.Position(11)));
        Assert.IsType<GraphReplacementReductionResult.Rejected>(GraphReplacementReducer.Apply(fixture.State,
            fixture.Prepare(grant: fixture.Grant(CapacityGrantStateV1.Reserved)), fixture.Position(11)));
        Assert.IsType<GraphReplacementReductionResult.Rejected>(GraphReplacementReducer.Apply(fixture.State,
            fixture.Prepare(grant: fixture.Grant(fixture.GrantId, CapacityGrantStateV1.Active, new CapacityDimensionId(1))),
            fixture.Position(11)));
        Assert.IsType<GraphReplacementReductionResult.Rejected>(GraphReplacementReducer.Apply(fixture.State,
            fixture.Prepare(grant: fixture.Grant(fixture.GrantId, CapacityGrantStateV1.Active,
                new CapacityDimensionId(3), new JournalPositionV1(
                    new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()), 2))),
            fixture.Position(11)));
    }

    [Fact]
    public void Commit_and_settlement_require_the_exact_phase_operation_and_predecessor()
    {
        var fixture = new Fixture();
        Assert.IsType<GraphReplacementReductionResult.Rejected>(GraphReplacementReducer.Apply(fixture.State,
            new GraphReplacementCommand.Commit(fixture.Operation, fixture.Position(10)), fixture.Position(11)));
        var prepared = Assert.IsType<GraphReplacementReductionResult.Applied>(
            GraphReplacementReducer.Apply(fixture.State, fixture.Prepare(), fixture.Position(11))).State;
        Assert.IsType<GraphReplacementReductionResult.Conflict>(GraphReplacementReducer.Apply(prepared,
            new GraphReplacementCommand.Commit(OperationId.Create(), fixture.Position(11)), fixture.Position(12)));
        Assert.IsType<GraphReplacementReductionResult.Rejected>(GraphReplacementReducer.Apply(prepared,
            new GraphReplacementCommand.SettleSource(fixture.Operation, fixture.Position(11), fixture.Settlement()),
            fixture.Position(12)));
    }

    [Fact]
    public void Changed_commit_or_settlement_bytes_conflict_after_success()
    {
        var fixture = new Fixture();
        var prepared = Assert.IsType<GraphReplacementReductionResult.Applied>(GraphReplacementReducer.Apply(
            fixture.State, fixture.Prepare(), fixture.Position(11))).State;
        var committed = Assert.IsType<GraphReplacementReductionResult.Applied>(GraphReplacementReducer.Apply(prepared,
            new GraphReplacementCommand.Commit(fixture.Operation, fixture.Position(11)), fixture.Position(12))).State;
        Assert.IsType<GraphReplacementReductionResult.Conflict>(GraphReplacementReducer.Apply(committed,
            new GraphReplacementCommand.Commit(fixture.Operation, fixture.Position(10)), fixture.Position(13)));
        var settled = Assert.IsType<GraphReplacementReductionResult.Applied>(GraphReplacementReducer.Apply(committed,
            new GraphReplacementCommand.SettleSource(fixture.Operation, fixture.Position(12), fixture.Settlement()), fixture.Position(13))).State;
        Assert.IsType<GraphReplacementReductionResult.Conflict>(GraphReplacementReducer.Apply(settled,
            new GraphReplacementCommand.SettleSource(fixture.Operation, fixture.Position(11), fixture.Settlement()), fixture.Position(14)));
    }

    [Fact]
    public void Exact_commands_are_idempotent_in_every_later_phase_and_never_resurrect_source()
    {
        var fixture = new Fixture();
        var prepare = fixture.Prepare();
        var prepared = Assert.IsType<GraphReplacementReductionResult.Applied>(
            GraphReplacementReducer.Apply(fixture.State, prepare, fixture.Position(11))).State;
        var commit = new GraphReplacementCommand.Commit(fixture.Operation, fixture.Position(11));
        var committed = Assert.IsType<GraphReplacementReductionResult.Applied>(
            GraphReplacementReducer.Apply(prepared, commit, fixture.Position(12))).State;
        var settle = new GraphReplacementCommand.SettleSource(fixture.Operation, fixture.Position(12), fixture.Settlement());
        var settled = Assert.IsType<GraphReplacementReductionResult.Applied>(
            GraphReplacementReducer.Apply(committed, settle, fixture.Position(13))).State;

        Assert.IsType<GraphReplacementReductionResult.Idempotent>(
            GraphReplacementReducer.Apply(committed, prepare, fixture.Position(13)));
        Assert.IsType<GraphReplacementReductionResult.Idempotent>(
            GraphReplacementReducer.Apply(committed, commit, fixture.Position(13)));
        Assert.IsType<GraphReplacementReductionResult.Idempotent>(
            GraphReplacementReducer.Apply(settled, prepare, fixture.Position(14)));
        Assert.IsType<GraphReplacementReductionResult.Idempotent>(
            GraphReplacementReducer.Apply(settled, commit, fixture.Position(14)));
        Assert.IsType<GraphReplacementReductionResult.Idempotent>(
            GraphReplacementReducer.Apply(settled, settle, fixture.Position(14)));
        Assert.Equal(fixture.TargetGeneration, Graph(settled.Authority));
    }

    [Fact]
    public void Source_settlement_requires_exact_later_terminal_capacity_evidence()
    {
        var fixture = new Fixture();
        var prepared = Assert.IsType<GraphReplacementReductionResult.Applied>(GraphReplacementReducer.Apply(
            fixture.State, fixture.Prepare(), fixture.Position(11))).State;
        var committed = Assert.IsType<GraphReplacementReductionResult.Applied>(GraphReplacementReducer.Apply(prepared,
            new GraphReplacementCommand.Commit(fixture.Operation, fixture.Position(11)), fixture.Position(12))).State;
        GraphReplacementReductionResult Apply(CapacityGrantSnapshotV1 proof) => GraphReplacementReducer.Apply(committed,
            new GraphReplacementCommand.SettleSource(fixture.Operation, fixture.Position(12), proof), fixture.Position(13));

        Assert.IsType<GraphReplacementReductionResult.Rejected>(Apply(null!));
        Assert.IsType<GraphReplacementReductionResult.Rejected>(Apply(fixture.Settlement(grantId: CapacityGrantId.Create())));
        Assert.IsType<GraphReplacementReductionResult.Rejected>(Apply(fixture.Settlement(currentFact: fixture.Position(3))));
        Assert.IsType<GraphReplacementReductionResult.Rejected>(Apply(fixture.Settlement(
            currentFact: new JournalPositionV1(new SessionAuthorityStampV1(
                RuntimeGenerationId.Create(), LiveSessionId.Create()), 4))));
        Assert.IsType<GraphReplacementReductionResult.Rejected>(Apply(fixture.Settlement(CapacityGrantStateV1.Active)));
        Assert.IsType<GraphReplacementReductionResult.Rejected>(Apply(fixture.Settlement(CapacityGrantStateV1.Unknown)));
        Assert.IsType<GraphReplacementReductionResult.Applied>(Apply(fixture.Settlement()));
    }

    private static GraphGenerationId Graph(ExpectedAuthorityVectorV1 value) =>
        Assert.IsType<AuthorityAxisValueV1.Graph>(value.Axes.Single(entry => entry.AxisId == AuthorityAxisId.Graph).Value).Value;

    private sealed class Fixture
    {
        internal readonly SessionAuthorityStampV1 Session = new(RuntimeGenerationId.Create(), LiveSessionId.Create());
        internal readonly GraphGenerationId SourceGeneration = GraphGenerationId.Create();
        internal readonly GraphGenerationId TargetGeneration = GraphGenerationId.Create();
        internal readonly OperationId Operation = OperationId.Create();
        internal readonly ClockDomainId Clock = ClockDomainId.Create();
        internal readonly BootId Boot = BootId.Create();
        internal readonly CapacityGrantId GrantId = CapacityGrantId.Create();
        internal readonly CapacityGrantId SourceGrantId = CapacityGrantId.Create();
        internal readonly ExpectedAuthorityVectorV1 Authority;
        internal readonly GraphTopologyPlan Source;
        internal readonly GraphTopologyPlan Target;
        internal readonly GraphReplacementState State;

        internal Fixture()
        {
            Authority = ExpectedAuthorityVectorV1.Create(Session,
                [new AuthorityAxisValueV1.Graph(SourceGeneration), new AuthorityAxisValueV1.Activity(ActivityGenerationId.Create())]);
            Source = Plan(SourceGeneration, SourceGrantId, "source");
            Target = Plan(TargetGeneration, GrantId, "target");
            State = GraphReplacementState.Create(Source, Grant(SourceGrantId, CapacityGrantStateV1.Active), Authority, Position(10));
        }

        internal GraphReplacementCommand.Prepare Prepare(OperationId? operation = null,
            JournalPositionV1? expected = null, Hash256? sourceFingerprint = null,
            ExpectedAuthorityVectorV1? authority = null, CapacityGrantSnapshotV1? grant = null,
            MonotonicStampV1? deadline = null) => new(operation ?? Operation, expected ?? Position(10),
                sourceFingerprint ?? Source.Fingerprint, Target, grant ?? Grant(GrantId, CapacityGrantStateV1.Active),
                authority ?? Authority, new MonotonicStampV1(Clock, Boot, 100),
                deadline ?? new MonotonicStampV1(Clock, Boot, 200));

        internal CapacityGrantSnapshotV1 Grant(CapacityGrantStateV1 state) => Grant(GrantId, state);

        internal CapacityGrantSnapshotV1 Grant(CapacityGrantId grantId, CapacityGrantStateV1 state,
            CapacityDimensionId? dimension = null, JournalPositionV1? grantedAt = null,
            JournalPositionV1? currentFact = null)
        {
            var charge = new CapacityChargeV1(dimension ?? new CapacityDimensionId(3),
                new CapacityScopeV1(TenantId.Create(), SessionId.Create()), 1, CapacityPurposeId.Create(),
                new CapacityChargeWindowV1.NoWindow());
            var terminal = state is CapacityGrantStateV1.Settled or CapacityGrantStateV1.Revoked;
            var balance = new CapacityChargeBalanceV1(charge, 1, 0, 0, terminal ? 0 : 1,
                state == CapacityGrantStateV1.Settled ? 1 : 0, 0, 0,
                state == CapacityGrantStateV1.Revoked ? 1 : 0, 0,
                terminal ? 0 : 1, 0);
            return new CapacityGrantSnapshotV1(grantId, OperationId.Create(), Authority, grantedAt ?? Position(2),
                currentFact ?? Position(3),
                new CapacityGrantExpiryV1.NoExpiry(), state, [balance]);
        }

        internal CapacityGrantSnapshotV1 Settlement(CapacityGrantStateV1 state = CapacityGrantStateV1.Settled,
            CapacityGrantId? grantId = null, JournalPositionV1? grantedAt = null,
            JournalPositionV1? currentFact = null) => Grant(grantId ?? SourceGrantId, state,
                new CapacityDimensionId(3), grantedAt, currentFact ?? Position(4));

        internal JournalPositionV1 Position(long sequence) => new(Session, sequence);
        private GraphTopologyPlan Plan(GraphGenerationId generation, CapacityGrantId grant, string key) =>
            new(Session, generation, grant, [new GraphTopologyNode(new BoundedAscii(key))], [], [new CapacityDimensionId(3)]);
    }

    private static Hash256 Hash(byte seed)
    {
        Span<byte> bytes = stackalloc byte[32]; bytes.Fill(seed);
        Assert.True(Hash256.TryCreate(bytes, out var value)); return value;
    }
}
