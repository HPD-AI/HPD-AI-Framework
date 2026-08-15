using System.Formats.Cbor;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphReplacementJournalFoldV1Tests
{
    [Fact]
    public void Empty_history_is_current_without_inventing_an_installation()
    {
        var fixture = new Fixture();
        var result = GraphReplacementJournalFoldV1.CreateAccumulator(fixture.Session).Complete();

        var current = Assert.IsType<GraphReplacementJournalFoldResultV1.Current>(result);
        Assert.Equal(0, current.SnapshotThrough);
        Assert.Null(current.State);
        Assert.Empty(current.PendingCommands);
    }

    [Fact]
    public void Inspect_exposes_only_exact_historical_capacity_references()
    {
        var fixture = new Fixture();
        var accumulator = GraphReplacementJournalFoldV1.CreateAccumulator(fixture.Session);
        var installed = accumulator.Inspect(fixture.Installed(3));
        var prepare = accumulator.Inspect(fixture.Command(4, new GraphReplacementJournalCommandV1.Prepare(
            fixture.Operation, fixture.Position(3), fixture.Source.Fingerprint, fixture.Target,
            fixture.Position(8), fixture.Authority, fixture.Observed, fixture.Deadline)));
        var commit = accumulator.Inspect(fixture.Command(5,
            new GraphReplacementJournalCommandV1.Commit(fixture.Operation, fixture.Position(4))));

        Assert.Equal(fixture.Position(2), installed.CapacityReference);
        Assert.Equal(fixture.Position(8), prepare.CapacityReference);
        Assert.Null(commit.CapacityReference);
    }

    [Fact]
    public void Exact_installation_creates_the_only_durable_initial_state()
    {
        var fixture = new Fixture();
        var accumulator = GraphReplacementJournalFoldV1.CreateAccumulator(fixture.Session);
        accumulator.Apply(accumulator.Inspect(fixture.GraphInitialization(1)));
        accumulator.Apply(accumulator.Inspect(fixture.Unrelated(2)));
        accumulator.Apply(accumulator.Inspect(fixture.Installed(3)), fixture.SourceGrant);

        var current = Assert.IsType<GraphReplacementJournalFoldResultV1.Current>(accumulator.Complete());
        Assert.Equal(3, current.SnapshotThrough);
        Assert.Equal(GraphReplacementPhaseV1.None, current.State!.Phase);
        Assert.Equal(fixture.Source.Fingerprint, current.State.SourcePlan.Fingerprint);
        Assert.Equal(fixture.Position(3), current.State.LastFact);
    }

    [Fact]
    public void Traffic_before_installation_and_noncontiguous_history_fail_closed()
    {
        var fixture = new Fixture();
        var commandFirst = GraphReplacementJournalFoldV1.CreateAccumulator(fixture.Session);
        commandFirst.Apply(commandFirst.Inspect(fixture.Command(1,
            new GraphReplacementJournalCommandV1.Commit(fixture.Operation, fixture.Position(1)))));
        Assert.Equal("graph-installation-missing",
            Assert.IsType<GraphReplacementJournalFoldResultV1.InvalidHistory>(commandFirst.Complete()).SafeCode.ToString());

        var gap = GraphReplacementJournalFoldV1.CreateAccumulator(fixture.Session);
        gap.Apply(gap.Inspect(fixture.Unrelated(2)));
        var invalid = Assert.IsType<GraphReplacementJournalFoldResultV1.InvalidHistory>(gap.Complete());
        Assert.Equal("noncontiguous-history", invalid.SafeCode.ToString());
        Assert.Equal(0, invalid.LastVerifiedPosition);
    }

    [Fact]
    public void Trusted_registration_rejects_missing_graph_without_throwing()
    {
        var fixture = new Fixture();
        var accumulator = GraphReplacementJournalFoldV1.CreateAccumulator(fixture.Session);
        accumulator.Apply(accumulator.Inspect(fixture.GraphInitialization(1)));
        accumulator.Apply(accumulator.Inspect(fixture.Unrelated(2)));
        accumulator.Apply(accumulator.Inspect(fixture.Installed(3)), fixture.SourceGrant);
        var empty = ExpectedAuthorityVectorV1.Create(fixture.Session, []);
        var command = new GraphReplacementJournalCommandV1.Commit(fixture.Operation, fixture.Position(3));

        var exception = Record.Exception(() => accumulator.Apply(
            accumulator.Inspect(fixture.Command(4, command, empty))));

        Assert.Null(exception);
        Assert.Equal("invalid-graph-replacement-command",
            Assert.IsType<GraphReplacementJournalFoldResultV1.InvalidHistory>(accumulator.Complete()).SafeCode.ToString());
    }

    [Fact]
    public void Null_history_is_invalid_instead_of_throwing()
    {
        var fixture = new Fixture();
        var accumulator = GraphReplacementJournalFoldV1.CreateAccumulator(fixture.Session);

        var exception = Record.Exception(() => accumulator.Apply(accumulator.Inspect(null)));

        Assert.Null(exception);
        var invalid = Assert.IsType<GraphReplacementJournalFoldResultV1.InvalidHistory>(accumulator.Complete());
        Assert.Equal("noncontiguous-history", invalid.SafeCode.ToString());
        Assert.Equal(0, invalid.LastVerifiedPosition);
    }

    [Fact]
    public void Prepare_commit_atomic_cut_and_settlement_replay_to_the_exact_closed_state()
    {
        var f = new Fixture();
        var a = f.InstalledPrefix();
        var prepare = new GraphReplacementJournalCommandV1.Prepare(f.Operation, f.Position(3),
            f.Source.Fingerprint, f.Target, f.TargetGrant.CurrentFact, f.Authority, f.Observed, f.Deadline);
        a.Apply(a.Inspect(f.Command(4, prepare)), f.TargetGrant);
        var prepared = new GraphReplacementSnapshotV1(GraphReplacementPhaseV1.Prepared, f.Source,
            f.SourceGrant.CurrentFact, new(f.Target, f.TargetGrant.CurrentFact), f.Authority, f.Position(5),
            new(f.Operation, f.Position(4)), null, null);
        a.Apply(a.Inspect(f.Result(5, f.Position(4), f.Position(3), f.Position(3),
            GraphReplacementJournalOutcomeV1.Prepared, prepared, f.Authority)));
        a.Apply(a.Inspect(f.Command(6, new GraphReplacementJournalCommandV1.Commit(f.Operation, f.Position(5)))));
        var committed = prepared with { Phase = GraphReplacementPhaseV1.Committed,
            CurrentAuthority = f.TargetAuthority, LastGraphFact = f.Position(7),
            Commit = new(f.Position(6), f.Position(8)) };
        a.Apply(a.Inspect(f.Result(7, f.Position(6), f.Position(5), f.Position(5),
            GraphReplacementJournalOutcomeV1.Committed, committed, f.Authority)));

        Assert.Equal(6, Assert.IsType<GraphReplacementJournalFoldResultV1.AtomicCommitIncomplete>(a.Complete()).LastVerifiedPosition);
        a.Apply(a.Inspect(f.GraphTransition(8, f.Position(6))));
        Assert.Equal(GraphReplacementPhaseV1.Committed,
            Assert.IsType<GraphReplacementJournalFoldResultV1.Current>(a.Complete()).State!.Phase);

        var settle = new GraphReplacementJournalCommandV1.SettleSource(f.Operation, f.Position(7), f.Settlement.CurrentFact);
        a.Apply(a.Inspect(f.Command(9, settle, f.TargetAuthority)), f.Settlement);
        var settled = committed with { Phase = GraphReplacementPhaseV1.SourceSettled, LastGraphFact = f.Position(10),
            Settlement = new(f.Position(9), f.Settlement.CurrentFact) };
        a.Apply(a.Inspect(f.Result(10, f.Position(9), f.Position(7), f.Position(7),
            GraphReplacementJournalOutcomeV1.SourceSettled, settled, f.TargetAuthority)));
        var current = Assert.IsType<GraphReplacementJournalFoldResultV1.Current>(a.Complete());
        Assert.Equal(10, current.SnapshotThrough);
        Assert.Equal(GraphReplacementPhaseV1.SourceSettled, current.State!.Phase);
    }

    [Fact]
    public void Duplicate_install_and_intervening_commit_fact_fail_closed()
    {
        var f = new Fixture();
        var duplicate = f.InstalledPrefix();
        duplicate.Apply(duplicate.Inspect(f.Installed(4)), f.SourceGrant);
        Assert.Equal("duplicate-graph-installation",
            Assert.IsType<GraphReplacementJournalFoldResultV1.InvalidHistory>(duplicate.Complete()).SafeCode.ToString());

        var interrupted = f.CommittedPrefix();
        interrupted.Apply(interrupted.Inspect(f.Unrelated(8)));
        Assert.Equal("invalid-atomic-commit-pair",
            Assert.IsType<GraphReplacementJournalFoldResultV1.InvalidHistory>(interrupted.Complete()).SafeCode.ToString());
    }

    [Fact]
    public void Capacity_proofs_must_precede_the_referencing_graph_fact()
    {
        var f = new Fixture();
        var install = GraphReplacementJournalFoldV1.CreateAccumulator(f.Session);
        install.Apply(install.Inspect(f.GraphInitialization(1)));
        install.Apply(install.Inspect(f.Unrelated(2)));
        install.Apply(install.Inspect(f.Installed(3)), f.Grant(f.Source.CapacityGrantId, f.Position(3)));
        Assert.Equal("invalid-graph-installation",
            Assert.IsType<GraphReplacementJournalFoldResultV1.InvalidHistory>(install.Complete()).SafeCode.ToString());

        var prepare = f.InstalledPrefix();
        var futureProof = f.Grant(f.Target.CapacityGrantId, f.Position(5));
        var command = new GraphReplacementJournalCommandV1.Prepare(f.Operation, f.Position(3),
            f.Source.Fingerprint, f.Target, f.Position(5), f.Authority, f.Observed, f.Deadline);
        prepare.Apply(prepare.Inspect(f.Command(4, command)), futureProof);
        Assert.Equal("invalid-graph-replacement-command",
            Assert.IsType<GraphReplacementJournalFoldResultV1.InvalidHistory>(prepare.Complete()).SafeCode.ToString());
    }

    [Fact]
    public void Runtime_replacement_is_terminal_and_later_facts_are_invalid()
    {
        var f = new Fixture();
        var replacement = RuntimeGenerationId.Create();
        var terminal = GraphReplacementJournalFoldV1.CreateAccumulator(f.Session);
        terminal.Apply(terminal.Inspect(f.RuntimeTransition(1, replacement)));
        var replaced = Assert.IsType<GraphReplacementJournalFoldResultV1.RuntimeReplaced>(terminal.Complete());
        Assert.Equal(replacement, replaced.Replacement);
        Assert.Equal(1, replaced.LastPosition);

        terminal.Apply(terminal.Inspect(f.Unrelated(2)));
        var invalid = Assert.IsType<GraphReplacementJournalFoldResultV1.InvalidHistory>(terminal.Complete());
        Assert.Equal("facts-after-runtime-replacement", invalid.SafeCode.ToString());
        Assert.Equal(1, invalid.LastVerifiedPosition);
    }

    private sealed class Fixture
    {
        internal readonly SessionAuthorityStampV1 Session = new(RuntimeGenerationId.Create(), LiveSessionId.Create());
        internal readonly OperationId Operation = OperationId.Create();
        internal readonly ExpectedAuthorityVectorV1 Authority;
        internal readonly GraphTopologyPlanV1 Source;
        internal readonly GraphTopologyPlanV1 Target;
        internal readonly CapacityGrantSnapshotV1 SourceGrant;
        internal readonly CapacityGrantSnapshotV1 TargetGrant;
        internal readonly CapacityGrantSnapshotV1 Settlement;
        internal readonly ExpectedAuthorityVectorV1 TargetAuthority;
        internal readonly MonotonicStampV1 Observed;
        internal readonly MonotonicStampV1 Deadline;

        internal Fixture()
        {
            var sourceGeneration = GraphGenerationId.Create();
            Authority = ExpectedAuthorityVectorV1.Create(Session, [new AuthorityAxisValueV1.Graph(sourceGeneration)]);
            Source = Plan(sourceGeneration, CapacityGrantId.Create(), "source");
            Target = Plan(GraphGenerationId.Create(), CapacityGrantId.Create(), "target");
            SourceGrant = Grant(Source.CapacityGrantId, Position(2));
            TargetGrant = Grant(Target.CapacityGrantId, Position(2));
            TargetAuthority = ExpectedAuthorityVectorV1.Create(Session,
                [new AuthorityAxisValueV1.Graph(Target.GraphGeneration)]);
            Settlement = Grant(Source.CapacityGrantId, Position(4), CapacityGrantStateV1.Settled, SourceGrant.GrantedAt);
            var clock = ClockDomainId.Create(); var boot = BootId.Create();
            Observed = new(clock, boot, 10); Deadline = new(clock, boot, 20);
        }

        internal JournalPositionV1 Position(long sequence) => new(Session, sequence);

        internal AuthorityFactEnvelopeV1 GraphInitialization(long sequence)
        {
            var schema = AuthorityGenerationInitializationCodecV1.SchemaFor(AuthorityAxisId.Graph);
            var token = AuthorityGenerationInitializationCodecV1.SchemaTokenFor(AuthorityAxisId.Graph);
            var payload = EncodeInitialization(Source.GraphGeneration);
            return Envelope(JournalFactId.Create(), sequence, OwnerSliceId.S2, schema, token, payload);
        }

        internal AuthorityFactEnvelopeV1 Installed(long sequence)
        {
            var body = new GraphTopologyInstalledV1(Source, Source.Fingerprint, Position(2), Authority);
            return ProtocolEnvelope(GraphReplacementFactIdsV1.Installed(Session, Source.Fingerprint), sequence,
                GraphReplacementPayloadRegistrationsV1.Installed, GraphReplacementCodecsV1.EncodeInstalled(body));
        }

        internal AuthorityFactEnvelopeV1 Command(long sequence, GraphReplacementJournalCommandV1 command,
            ExpectedAuthorityVectorV1? authority = null) =>
            ProtocolEnvelope(GraphReplacementFactIdsV1.Command(Session, command.OperationId, (ushort)command.Kind), sequence,
                GraphReplacementPayloadRegistrationsV1.Command, GraphReplacementCodecsV1.EncodeCommand(command), authority);

        internal AuthorityFactEnvelopeV1 Result(long sequence, JournalPositionV1 command,
            JournalPositionV1 expected, JournalPositionV1 actual, GraphReplacementJournalOutcomeV1 outcome,
            GraphReplacementSnapshotV1 snapshot, ExpectedAuthorityVectorV1 authority) =>
            ProtocolEnvelope(GraphReplacementFactIdsV1.Result(command), sequence,
                GraphReplacementPayloadRegistrationsV1.Fact,
                GraphReplacementCodecsV1.EncodeFact(new(command, expected, actual, outcome, snapshot, null)), authority);

        internal AuthorityFactEnvelopeV1 GraphTransition(long sequence, JournalPositionV1 commitCommand)
        {
            var schema = AuthorityGenerationTransitionCodecV1.SchemaFor(AuthorityAxisId.Graph);
            var token = AuthorityGenerationTransitionCodecV1.SchemaTokenFor(AuthorityAxisId.Graph);
            return Envelope(GraphReplacementFactIdsV1.Transition(commitCommand), sequence, OwnerSliceId.S2,
                schema, token, EncodeTransition(Source.GraphGeneration, Target.GraphGeneration));
        }

        internal AuthorityFactEnvelopeV1 RuntimeTransition(long sequence, RuntimeGenerationId proposed)
        {
            var schema = AuthorityGenerationTransitionCodecV1.SchemaFor(AuthorityAxisId.Runtime);
            var token = AuthorityGenerationTransitionCodecV1.SchemaTokenFor(AuthorityAxisId.Runtime);
            Span<byte> before = stackalloc byte[16]; Span<byte> after = stackalloc byte[16];
            Assert.True(Session.RuntimeGenerationId.TryWriteBytes(before)); Assert.True(proposed.TryWriteBytes(after));
            var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
            writer.WriteStartMap(4); writer.WriteUInt64(1); SessionAuthorityStampV1Codec.Write(writer, Session);
            writer.WriteUInt64(2); writer.WriteByteString(before); writer.WriteUInt64(3); writer.WriteByteString(after);
            writer.WriteUInt64(4); writer.WriteUInt64((ushort)OwnerSliceId.S1); writer.WriteEndMap();
            return Envelope(JournalFactId.Create(), sequence, OwnerSliceId.S1, schema, token, writer.Encode());
        }

        internal GraphReplacementJournalFoldV1.Accumulator InstalledPrefix()
        {
            var a = GraphReplacementJournalFoldV1.CreateAccumulator(Session);
            a.Apply(a.Inspect(GraphInitialization(1))); a.Apply(a.Inspect(Unrelated(2)));
            a.Apply(a.Inspect(Installed(3)), SourceGrant); return a;
        }

        internal GraphReplacementJournalFoldV1.Accumulator CommittedPrefix()
        {
            var a = InstalledPrefix();
            var prepare = new GraphReplacementJournalCommandV1.Prepare(Operation, Position(3), Source.Fingerprint,
                Target, TargetGrant.CurrentFact, Authority, Observed, Deadline);
            a.Apply(a.Inspect(Command(4, prepare)), TargetGrant);
            var prepared = new GraphReplacementSnapshotV1(GraphReplacementPhaseV1.Prepared, Source,
                SourceGrant.CurrentFact, new(Target, TargetGrant.CurrentFact), Authority, Position(5),
                new(Operation, Position(4)), null, null);
            a.Apply(a.Inspect(Result(5, Position(4), Position(3), Position(3),
                GraphReplacementJournalOutcomeV1.Prepared, prepared, Authority)));
            a.Apply(a.Inspect(Command(6, new GraphReplacementJournalCommandV1.Commit(Operation, Position(5)))));
            var committed = prepared with { Phase = GraphReplacementPhaseV1.Committed,
                CurrentAuthority = TargetAuthority, LastGraphFact = Position(7), Commit = new(Position(6), Position(8)) };
            a.Apply(a.Inspect(Result(7, Position(6), Position(5), Position(5),
                GraphReplacementJournalOutcomeV1.Committed, committed, Authority)));
            return a;
        }

        internal AuthorityFactEnvelopeV1 Unrelated(long sequence) => new(JournalFactId.Create(), Position(sequence), null,
            OwnerSliceId.S4, new SchemaReferenceV1(SchemaId.Create(), 1, 0), [0x80], Hash256.Compute([0x80]),
            new CorrelationEnvelopeV1(TenantId.Create()), new UtcInstant(sequence), new UtcInstant(sequence), Integrity());

        private AuthorityFactEnvelopeV1 ProtocolEnvelope(JournalFactId id, long sequence,
            AuthorityPayloadRegistrationV1 registration, byte[] body, ExpectedAuthorityVectorV1? authority = null)
        {
            var payload = GraphReplacementCodecsV1.EncodeOuter(new GraphOwnerPayloadV1(Session, authority ?? Authority, body));
            return Envelope(id, sequence, OwnerSliceId.S2, registration.SchemaToken, registration.Schema, payload);
        }

        private AuthorityFactEnvelopeV1 Envelope(JournalFactId id, long sequence, OwnerSliceId owner,
            BoundedAscii token, SchemaReferenceV1 schema, byte[] payload) =>
            Envelope(id, sequence, owner, schema, token, payload);

        private AuthorityFactEnvelopeV1 Envelope(JournalFactId id, long sequence, OwnerSliceId owner,
            SchemaReferenceV1 schema, BoundedAscii token, byte[] payload) => new(id, Position(sequence), null, owner,
                schema, payload, AuthorityPayloadHashV1.Compute(token, schema, payload),
                new CorrelationEnvelopeV1(TenantId.Create()), new UtcInstant(sequence), new UtcInstant(sequence), Integrity());

        private byte[] EncodeInitialization(GraphGenerationId generation)
        {
            Span<byte> bytes = stackalloc byte[16]; Assert.True(generation.TryWriteBytes(bytes));
            var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
            writer.WriteStartMap(3); writer.WriteUInt64(1); SessionAuthorityStampV1Codec.Write(writer, Session);
            writer.WriteUInt64(2); writer.WriteByteString(bytes); writer.WriteUInt64(3); writer.WriteUInt64((ushort)OwnerSliceId.S2);
            writer.WriteEndMap(); return writer.Encode();
        }

        private byte[] EncodeTransition(GraphGenerationId expected, GraphGenerationId proposed)
        {
            Span<byte> before = stackalloc byte[16]; Span<byte> after = stackalloc byte[16];
            Assert.True(expected.TryWriteBytes(before)); Assert.True(proposed.TryWriteBytes(after));
            var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
            writer.WriteStartMap(4); writer.WriteUInt64(1); SessionAuthorityStampV1Codec.Write(writer, Session);
            writer.WriteUInt64(2); writer.WriteByteString(before); writer.WriteUInt64(3); writer.WriteByteString(after);
            writer.WriteUInt64(4); writer.WriteUInt64((ushort)OwnerSliceId.S2); writer.WriteEndMap();
            return writer.Encode();
        }

        internal CapacityGrantSnapshotV1 Grant(CapacityGrantId id, JournalPositionV1 fact,
            CapacityGrantStateV1 state = CapacityGrantStateV1.Active, JournalPositionV1? grantedAt = null)
        {
            var charge = new CapacityChargeV1(new CapacityDimensionId(3),
                new CapacityScopeV1(TenantId.Create(), SessionId.Create()), 1, CapacityPurposeId.Create(),
                new CapacityChargeWindowV1.NoWindow());
            var terminal = state is CapacityGrantStateV1.Settled or CapacityGrantStateV1.Revoked;
            var balance = new CapacityChargeBalanceV1(charge, 1, 0, 0, terminal ? 0 : 1,
                state == CapacityGrantStateV1.Settled ? 1 : 0, 0, 0,
                state == CapacityGrantStateV1.Revoked ? 1 : 0, 0, terminal ? 0 : 1, 0);
            return new(id, OperationId.Create(), Authority, grantedAt ?? fact, fact,
                new CapacityGrantExpiryV1.NoExpiry(), state, [balance]);
        }

        private GraphTopologyPlanV1 Plan(GraphGenerationId generation, CapacityGrantId grant, string node) =>
            new(Session, generation, grant, [new GraphTopologyNodeV1(new BoundedAscii(node))], [], [new CapacityDimensionId(3)]);

        private static IntegrityEnvelopeV1 Integrity() => new(1, 1, Hash256.Compute([1]), []);
    }
}
