using System.Formats.Cbor;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphReplacementAdmissionCoordinatorV1Tests
{
    [Fact]
    public async Task Prepare_and_atomic_commit_reconcile_idempotently_from_the_journal()
    {
        var f = await Fixture.CreateAsync();
        var prepareOperation = OperationId.Create();
        var prepare = new GraphReplacementJournalCommandV1.Prepare(prepareOperation, f.Installation,
            f.Source.Fingerprint, f.Target, f.TargetGrant.CurrentFact, f.Authority, f.Observed, f.Deadline);
        var prepareRequest = f.Request(prepare, f.Authority);
        var lostAcks = new LoseCommittedAcksJournal(f.Journal, 2);
        var prepared = Assert.IsType<GraphReplacementAdmissionResultV1.Admitted>(
            await GraphReplacementAdmissionCoordinatorV1.AdmitAsync(lostAcks, prepareRequest));
        Assert.Equal(GraphReplacementJournalOutcomeV1.Prepared, prepared.Outcome);
        Assert.Null(prepared.GraphTransition);
        Assert.Equal(2, lostAcks.AppendCalls);

        var duplicate = Assert.IsType<GraphReplacementAdmissionResultV1.AlreadyAdmitted>(
            await GraphReplacementAdmissionCoordinatorV1.AdmitAsync(f.Journal, prepareRequest));
        Assert.Equal(prepared.Result.Position, duplicate.Result.Position);

        var commit = new GraphReplacementJournalCommandV1.Commit(prepareOperation, prepared.Result.Position);
        var committed = Assert.IsType<GraphReplacementAdmissionResultV1.Admitted>(
            await GraphReplacementAdmissionCoordinatorV1.AdmitAsync(f.Journal, f.Request(commit, f.Authority)));
        Assert.Equal(GraphReplacementJournalOutcomeV1.Committed, committed.Outcome);
        Assert.NotNull(committed.GraphTransition);
        Assert.Equal(committed.Result.Position.Sequence + 1, committed.GraphTransition!.Position.Sequence);

        var retry = Assert.IsType<GraphReplacementAdmissionResultV1.AlreadyAdmitted>(
            await GraphReplacementAdmissionCoordinatorV1.AdmitAsync(f.Journal, f.Request(commit, f.Authority)));
        Assert.Equal(committed.Result.FactId, retry.Result.FactId);
        Assert.Equal(committed.GraphTransition.FactId, retry.GraphTransition!.FactId);
    }

    [Fact]
    public void Request_rejects_outer_prepare_authority_divergence()
    {
        var f = new Fixture();
        var authority = ExpectedAuthorityVectorV1.Create(f.Session,
            [new AuthorityAxisValueV1.Graph(f.GraphGeneration)]);
        var other = ExpectedAuthorityVectorV1.Create(f.Session,
            [new AuthorityAxisValueV1.Graph(GraphGenerationId.Create())]);
        var plan = new GraphTopologyPlanV1(f.Session, GraphGenerationId.Create(), CapacityGrantId.Create(),
            [new GraphTopologyNodeV1(new BoundedAscii("target"))], [], [new CapacityDimensionId(3)]);
        var prepare = new GraphReplacementJournalCommandV1.Prepare(OperationId.Create(), new(f.Session, 1),
            Hash256.Compute([1]), plan, new(f.Session, 1), authority,
            new(ClockDomainId.Create(), BootId.Create(), 1), new(ClockDomainId.Create(), BootId.Create(), 2));
        Assert.Throws<ArgumentException>(() => new GraphReplacementAdmissionRequestV1(prepare, other,
            new CorrelationEnvelopeV1(TenantId.Create()), new UtcInstant(1)));
    }

    [Fact]
    public async Task Missing_install_stale_authority_and_unprepared_commit_never_mint_a_transition()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var authority = ExpectedAuthorityVectorV1.Create(session,
            [new AuthorityAxisValueV1.Graph(GraphGenerationId.Create())]);
        var empty = new CountingJournal(new InMemoryAuthorityJournalV1(new AuthorityPayloadAdmissionRegistryV1([
            GraphReplacementPayloadRegistrationsV1.Command, GraphReplacementPayloadRegistrationsV1.Fact]),
            () => new UtcInstant(1), new AuthorityJournalCapacityV1(1, 8, 1_000_000)));
        var missing = new GraphReplacementAdmissionRequestV1(new GraphReplacementJournalCommandV1.Commit(
            OperationId.Create(), new(session, 1)), authority, new CorrelationEnvelopeV1(TenantId.Create()), new UtcInstant(1));
        Assert.Equal("graph-installation-missing", Assert.IsType<GraphReplacementAdmissionResultV1.Rejected>(
            await GraphReplacementAdmissionCoordinatorV1.AdmitAsync(empty, missing)).SafeCode.ToString());
        Assert.Equal(0, empty.AppendCalls);

        var f = await Fixture.CreateAsync();
        var wrong = ExpectedAuthorityVectorV1.Create(f.Session,
            [new AuthorityAxisValueV1.Graph(GraphGenerationId.Create())]);
        var stalePort = new CountingJournal(f.Journal);
        var stale = f.Request(new GraphReplacementJournalCommandV1.Commit(OperationId.Create(), f.Installation), wrong);
        Assert.Equal("authority-vector-stale", Assert.IsType<GraphReplacementAdmissionResultV1.Rejected>(
            await GraphReplacementAdmissionCoordinatorV1.AdmitAsync(stalePort, stale)).SafeCode.ToString());
        Assert.Equal(0, stalePort.AppendCalls);

        var rejected = Assert.IsType<GraphReplacementAdmissionResultV1.Admitted>(
            await GraphReplacementAdmissionCoordinatorV1.AdmitAsync(f.Journal, f.Request(
                new GraphReplacementJournalCommandV1.Commit(OperationId.Create(), f.Installation), f.Authority)));
        Assert.Equal(GraphReplacementJournalOutcomeV1.Rejected, rejected.Outcome);
        Assert.Null(rejected.GraphTransition);
    }

    [Fact]
    public async Task Eighth_result_append_lost_ack_is_resolved_by_the_mandatory_final_read()
    {
        var f = await Fixture.CreateAsync(); var operation = OperationId.Create();
        var command = new GraphReplacementJournalCommandV1.Prepare(operation, f.Installation, f.Source.Fingerprint,
            f.Target, f.TargetGrant.CurrentFact, f.Authority, f.Observed, f.Deadline);
        var port = new EighthLostAckJournal(f.Journal);
        var admitted = Assert.IsType<GraphReplacementAdmissionResultV1.Admitted>(
            await GraphReplacementAdmissionCoordinatorV1.AdmitAsync(port, f.Request(command, f.Authority)));
        Assert.Equal(GraphReplacementJournalOutcomeV1.Prepared, admitted.Outcome);
        Assert.Equal(8, port.AppendCalls);
    }

    [Fact]
    public void Result_union_rejects_unproven_tuples_and_invalid_diagnostics()
    {
        Assert.Throws<ArgumentNullException>(() => new GraphReplacementAdmissionResultV1.Admitted(
            null!, null!, null, GraphReplacementJournalOutcomeV1.Prepared));
        Assert.Throws<ArgumentException>(() => new GraphReplacementAdmissionResultV1.Rejected(default));
        Assert.Throws<ArgumentException>(() => new GraphReplacementAdmissionResultV1.ContradictoryDuplicate(default));
        Assert.Throws<ArgumentException>(() => new GraphReplacementAdmissionResultV1.OutcomeUnknown(default,
            new BoundedAscii("unknown")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphReplacementAdmissionResultV1.RetryRequired(-1));
    }

    internal sealed class Fixture
    {
        private readonly TenantId _tenant = TenantId.Create();
        private readonly ClockDomainId _clock = ClockDomainId.Create();
        private readonly BootId _boot = BootId.Create();
        internal SessionAuthorityStampV1 Session { get; } = new(RuntimeGenerationId.Create(), LiveSessionId.Create());
        internal GraphGenerationId GraphGeneration { get; } = GraphGenerationId.Create();
        internal ExpectedAuthorityVectorV1 Authority { get; private set; } = null!;
        internal InMemoryAuthorityJournalV1 Journal { get; private set; } = null!;
        internal GraphTopologyPlanV1 Source { get; private set; } = null!;
        internal GraphTopologyPlanV1 Target { get; private set; } = null!;
        internal CapacityGrantSnapshotV1 TargetGrant { get; private set; } = null!;
        internal JournalPositionV1 Installation { get; private set; }
        internal MonotonicStampV1 Observed => new(_clock, _boot, 100);
        internal MonotonicStampV1 Deadline => new(_clock, _boot, 200);

        internal static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture();
            f.Authority = ExpectedAuthorityVectorV1.Create(f.Session,
                [new AuthorityAxisValueV1.Graph(f.GraphGeneration)]);
            f.Journal = new InMemoryAuthorityJournalV1(new AuthorityPayloadAdmissionRegistryV1([
                new AuthorityGenerationInitializationPayloadRegistrationV1(AuthorityAxisId.Graph),
                new AuthorityGenerationTransitionPayloadRegistrationV1(AuthorityAxisId.Graph),
                new CapacityReservationPayloadRegistrationV1(), new CapacitySettlementPayloadRegistrationV1(),
                GraphReplacementPayloadRegistrationsV1.Installed, GraphReplacementPayloadRegistrationsV1.Command,
                GraphReplacementPayloadRegistrationsV1.Fact, GraphRuntimePayloadRegistrationsV1.Command,
                GraphRuntimePayloadRegistrationsV1.Fact,
            ]), () => new UtcInstant(100), new AuthorityJournalCapacityV1(2, 64, 8 * 1024 * 1024));
            await f.AppendGraphInitializationAsync();
            var source = await f.ActiveGrantAsync();
            f.Source = f.Plan(f.GraphGeneration, source.GrantId, "source");
            var installRequest = new GraphTopologyInstallationRequestV1(f.Session, f.Source, source.CurrentFact,
                f.Authority, new CorrelationEnvelopeV1(f._tenant), new UtcInstant(4));
            f.Installation = Assert.IsType<GraphTopologyInstallationAdmissionResultV1.Installed>(
                await GraphTopologyInstallationAdmissionV1.InstallAsync(f.Journal, installRequest)).Envelope.Position;
            f.TargetGrant = await f.ActiveGrantAsync();
            f.Target = f.Plan(GraphGenerationId.Create(), f.TargetGrant.GrantId, "target");
            return f;
        }

        internal GraphReplacementAdmissionRequestV1 Request(GraphReplacementJournalCommandV1 command,
            ExpectedAuthorityVectorV1 authority) => new(command, authority,
                new CorrelationEnvelopeV1(_tenant, operationId: command.OperationId), new UtcInstant(20));

        private async Task<CapacityGrantSnapshotV1> ActiveGrantAsync()
        {
            var operation = OperationId.Create();
            var charge = new CapacityChargeV1(new CapacityDimensionId(3),
                new CapacityScopeV1(_tenant, null, new CapacitySubjectV1.Operation(operation)), 1,
                CapacityPurposeId.Create(), new CapacityChargeWindowV1.NoWindow());
            var request = new CapacityRequestV1(operation, Authority, [charge], new(_clock, _boot, 500), CapacityPriorityV1.Normal);
            var reserved = Assert.IsType<CapacityAdmissionResultV1.Granted>(await CapacityAdmissionCoordinatorV1.ReserveAsync(
                Journal, request, new CapacityGrantExpiryV1.NoExpiry(), new CorrelationEnvelopeV1(_tenant,
                    operationId: operation), new(_clock, _boot, 90), new UtcInstant(2)));
            var activationOperation = OperationId.Create();
            var body = new CapacitySettlementFactBodyV1(reserved.Grant.GrantId, activationOperation,
                reserved.Envelope.Position, CapacitySettlementKindV1.Activated,
                [new CapacitySettlementChargeV1(charge.DimensionId, charge.Scope, charge.Purpose, 1)], new(_clock, _boot, 91));
            return Assert.IsType<CapacityAdmissionResultV1.Settled>(await CapacityAdmissionCoordinatorV1.SettleAsync(
                Journal, Session, body, new CorrelationEnvelopeV1(_tenant, operationId: activationOperation),
                new UtcInstant(3))).Grant;
        }

        private async Task AppendGraphInitializationAsync()
        {
            var registration = new AuthorityGenerationInitializationPayloadRegistrationV1(AuthorityAxisId.Graph);
            Span<byte> bytes = stackalloc byte[16]; Assert.True(GraphGeneration.TryWriteBytes(bytes));
            var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
            writer.WriteStartMap(3); writer.WriteUInt64(1); SessionAuthorityStampV1Codec.Write(writer, Session);
            writer.WriteUInt64(2); writer.WriteByteString(bytes); writer.WriteUInt64(3);
            writer.WriteUInt64((ushort)OwnerSliceId.S2); writer.WriteEndMap(); var payload = writer.Encode();
            var proposal = new ProposedAuthorityFactV1(JournalFactId.Create(), null, OwnerSliceId.S2,
                registration.Schema, payload, AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema,
                    payload), new CorrelationEnvelopeV1(_tenant), new UtcInstant(1));
            Assert.IsType<AppendAuthorityResultV1.Committed>(await Journal.AppendAsync(new AppendAuthorityBatchV1(
                Session, 0, [], [proposal], ProposedAuthorityFactV1.MaximumPayloadBytes)));
        }

        private GraphTopologyPlanV1 Plan(GraphGenerationId generation, CapacityGrantId grant, string node) =>
            new(Session, generation, grant, [new GraphTopologyNodeV1(new BoundedAscii(node))], [], [new CapacityDimensionId(3)]);
    }

    private sealed class LoseCommittedAcksJournal(IAuthorityJournalV1 inner, int count) : IAuthorityJournalV1
    {
        internal int AppendCalls { get; private set; }
        public async ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request,
            CancellationToken cancellationToken = default)
        {
            AppendCalls++; var result = await inner.AppendAsync(request, cancellationToken);
            return AppendCalls <= count && result is AppendAuthorityResultV1.Committed
                ? new AppendAuthorityResultV1.OutcomeUnknown(OperationId.Create()) : result;
        }
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,
            CancellationToken cancellationToken = default) => inner.ReadAsync(request, cancellationToken);
    }

    private sealed class CountingJournal(IAuthorityJournalV1 inner) : IAuthorityJournalV1
    {
        internal int AppendCalls { get; private set; }
        public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request,
            CancellationToken cancellationToken = default) { AppendCalls++; return inner.AppendAsync(request, cancellationToken); }
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,
            CancellationToken cancellationToken = default) => inner.ReadAsync(request, cancellationToken);
    }

    private sealed class EighthLostAckJournal(IAuthorityJournalV1 inner) : IAuthorityJournalV1
    {
        internal int AppendCalls { get; private set; }
        public async ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request,
            CancellationToken cancellationToken = default)
        {
            AppendCalls++;
            if (AppendCalls is >= 2 and <= 7)
                return new AppendAuthorityResultV1.SessionConflict(request.ExpectedSessionHead,
                    checked(request.ExpectedSessionHead + 1));
            var result = await inner.AppendAsync(request, cancellationToken);
            return AppendCalls == 8 && result is AppendAuthorityResultV1.Committed
                ? new AppendAuthorityResultV1.OutcomeUnknown(OperationId.Create()) : result;
        }
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,
            CancellationToken cancellationToken = default) => inner.ReadAsync(request, cancellationToken);
    }
}
