using System.Formats.Cbor;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphTopologyInstallationAdmissionV1Tests
{
    [Fact]
    public async Task Installation_is_one_fact_idempotent_and_conflicting_topology_is_closed()
    {
        var f = await Fixture.CreateAsync();
        var raw = await GraphTopologyInstallationAdmissionV1.InstallAsync(f.Journal, f.Request);
        Assert.True(raw is GraphTopologyInstallationAdmissionResultV1.Installed, raw.ToString());
        var installed = (GraphTopologyInstallationAdmissionResultV1.Installed)raw;
        Assert.Equal(4, installed.Envelope.Position.Sequence);

        var duplicate = Assert.IsType<GraphTopologyInstallationAdmissionResultV1.AlreadyInstalled>(
            await GraphTopologyInstallationAdmissionV1.InstallAsync(f.Journal, f.Request));
        Assert.Equal(installed.Envelope.Position, duplicate.Envelope.Position);

        var other = new GraphTopologyPlanV1(f.Session, f.GraphGeneration, f.Grant.GrantId,
            [new GraphTopologyNodeV1(new BoundedAscii("other"))], [], [new CapacityDimensionId(3)]);
        Assert.IsType<GraphTopologyInstallationAdmissionResultV1.Conflict>(
            await GraphTopologyInstallationAdmissionV1.InstallAsync(f.Journal, f.RequestFor(other)));
    }

    [Fact]
    public async Task Committed_then_lost_ack_is_reconciled_without_duplicate_growth()
    {
        var f = await Fixture.CreateAsync();
        var port = new LoseFirstAppendAckJournal(f.Journal);
        var raw = await GraphTopologyInstallationAdmissionV1.InstallAsync(port, f.Request);
        Assert.True(raw is GraphTopologyInstallationAdmissionResultV1.Installed, raw.ToString());
        var result = (GraphTopologyInstallationAdmissionResultV1.Installed)raw;

        Assert.Equal(4, result.Envelope.Position.Sequence);
        var read = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await f.Journal.ReadAsync(
            new ReadAuthorityRangeV1(f.Session, 0, long.MaxValue, 16, ProposedAuthorityFactV1.MaximumPayloadBytes)));
        Assert.Equal(4, read.SnapshotThrough);
        Assert.Equal(1, port.AppendCalls);
    }

    [Fact]
    public async Task Stale_claimed_axis_and_missing_historical_proof_do_not_append()
    {
        var f = await Fixture.CreateAsync();
        var wrongGraph = ExpectedAuthorityVectorV1.Create(f.Session,
            [new AuthorityAxisValueV1.Graph(GraphGenerationId.Create())]);
        var wrongPlan = new GraphTopologyPlanV1(f.Session,
            ((AuthorityAxisValueV1.Graph)wrongGraph.Axes.Single().Value).Value, f.Grant.GrantId,
            [new GraphTopologyNodeV1(new BoundedAscii("source"))], [], [new CapacityDimensionId(3)]);
        var stale = new GraphTopologyInstallationRequestV1(f.Session, wrongPlan, f.Grant.CurrentFact,
            wrongGraph, f.Correlation, new UtcInstant(10));
        Assert.IsType<GraphTopologyInstallationAdmissionResultV1.Rejected>(
            await GraphTopologyInstallationAdmissionV1.InstallAsync(f.Journal, stale));

        var missing = new GraphTopologyInstallationRequestV1(f.Session, f.Plan, new JournalPositionV1(f.Session, 1),
            f.Authority, f.Correlation, new UtcInstant(10));
        Assert.IsType<GraphTopologyInstallationAdmissionResultV1.OutcomeUnknown>(
            await GraphTopologyInstallationAdmissionV1.InstallAsync(f.Journal, missing));
        var read = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await f.Journal.ReadAsync(
            new ReadAuthorityRangeV1(f.Session, 0, long.MaxValue, 16, ProposedAuthorityFactV1.MaximumPayloadBytes)));
        Assert.Equal(3, read.SnapshotThrough);
    }

    [Fact]
    public async Task Eighth_lost_ack_is_reconciled_by_the_mandatory_final_read()
    {
        var f = await Fixture.CreateAsync();
        var journal = new CommitOnlyOnEighthJournal(f.Journal);

        var installed = Assert.IsType<GraphTopologyInstallationAdmissionResultV1.Installed>(
            await GraphTopologyInstallationAdmissionV1.InstallAsync(journal, f.Request));

        Assert.Equal(8, journal.AppendCalls);
        Assert.Equal(4, installed.Envelope.Position.Sequence);
    }

    [Fact]
    public void Result_union_rejects_invalid_values()
    {
        Assert.Throws<ArgumentNullException>(() => new GraphTopologyInstallationAdmissionResultV1.Installed(null!));
        Assert.Throws<ArgumentException>(() => new GraphTopologyInstallationAdmissionResultV1.RuntimeReplaced(default));
        Assert.Throws<ArgumentException>(() => new GraphTopologyInstallationAdmissionResultV1.Rejected(default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphTopologyInstallationAdmissionResultV1.RetryRequired(-1));
        Assert.Throws<ArgumentException>(() => new GraphTopologyInstallationAdmissionResultV1.OutcomeUnknown(
            default, new BoundedAscii("unknown"), 0));
    }

    private sealed class Fixture
    {
        private readonly ClockDomainId _clock = ClockDomainId.Create();
        private readonly BootId _boot = BootId.Create();
        internal SessionAuthorityStampV1 Session { get; } = new(RuntimeGenerationId.Create(), LiveSessionId.Create());
        internal GraphGenerationId GraphGeneration { get; } = GraphGenerationId.Create();
        internal ExpectedAuthorityVectorV1 Authority { get; private set; } = null!;
        internal InMemoryAuthorityJournalV1 Journal { get; private set; } = null!;
        internal CapacityGrantSnapshotV1 Grant { get; private set; } = null!;
        internal GraphTopologyPlanV1 Plan { get; private set; } = null!;
        internal CorrelationEnvelopeV1 Correlation { get; } = new(TenantId.Create());
        internal GraphTopologyInstallationRequestV1 Request => RequestFor(Plan);

        internal static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture();
            f.Authority = ExpectedAuthorityVectorV1.Create(f.Session,
                [new AuthorityAxisValueV1.Graph(f.GraphGeneration)]);
            f.Journal = new InMemoryAuthorityJournalV1(new AuthorityPayloadAdmissionRegistryV1([
                new AuthorityGenerationInitializationPayloadRegistrationV1(AuthorityAxisId.Graph),
                new CapacityReservationPayloadRegistrationV1(), new CapacitySettlementPayloadRegistrationV1(),
                GraphReplacementPayloadRegistrationsV1.Installed,
                new AuthorityGenerationTransitionPayloadRegistrationV1(AuthorityAxisId.Graph),
            ]), () => new UtcInstant(100), new AuthorityJournalCapacityV1(2, 32, 4 * 1024 * 1024));
            await f.AppendGraphInitializationAsync();
            var operation = OperationId.Create();
            var request = new CapacityRequestV1(operation, f.Authority,
                [new CapacityChargeV1(new CapacityDimensionId(3), new CapacityScopeV1(f.Correlation.TenantId,
                    null, new CapacitySubjectV1.Operation(operation)), 1, CapacityPurposeId.Create(),
                    new CapacityChargeWindowV1.NoWindow())], new MonotonicStampV1(f._clock, f._boot, 100),
                CapacityPriorityV1.Normal);
            var reservation = await CapacityAdmissionCoordinatorV1.ReserveAsync(f.Journal, request,
                new CapacityGrantExpiryV1.NoExpiry(), new CorrelationEnvelopeV1(f.Correlation.TenantId,
                    operationId: operation), new MonotonicStampV1(f._clock, f._boot, 90), new UtcInstant(2));
            var reserved = Assert.IsType<CapacityAdmissionResultV1.Granted>(reservation);
            var activationOperation = OperationId.Create();
            var activation = new CapacitySettlementFactBodyV1(reserved.Grant.GrantId, activationOperation,
                reserved.Envelope.Position, CapacitySettlementKindV1.Activated,
                [new CapacitySettlementChargeV1(request.Charges[0].DimensionId, request.Charges[0].Scope,
                    request.Charges[0].Purpose, 1)], new MonotonicStampV1(f._clock, f._boot, 90));
            var activated = await CapacityAdmissionCoordinatorV1.SettleAsync(f.Journal, f.Session, activation,
                new CorrelationEnvelopeV1(f.Correlation.TenantId, operationId: activationOperation), new UtcInstant(3));
            f.Grant = Assert.IsType<CapacityAdmissionResultV1.Settled>(activated).Grant;
            Assert.Equal(CapacityGrantStateV1.Active, f.Grant.State);
            f.Plan = new GraphTopologyPlanV1(f.Session, f.GraphGeneration, f.Grant.GrantId,
                [new GraphTopologyNodeV1(new BoundedAscii("source"))], [], [new CapacityDimensionId(3)]);
            return f;
        }

        internal GraphTopologyInstallationRequestV1 RequestFor(GraphTopologyPlanV1 plan) =>
            new(Session, plan, Grant.CurrentFact, Authority, Correlation, new UtcInstant(10));

        private async Task AppendGraphInitializationAsync()
        {
            var registration = new AuthorityGenerationInitializationPayloadRegistrationV1(AuthorityAxisId.Graph);
            Span<byte> generation = stackalloc byte[16]; Assert.True(GraphGeneration.TryWriteBytes(generation));
            var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
            writer.WriteStartMap(3); writer.WriteUInt64(1); SessionAuthorityStampV1Codec.Write(writer, Session);
            writer.WriteUInt64(2); writer.WriteByteString(generation); writer.WriteUInt64(3);
            writer.WriteUInt64((ushort)OwnerSliceId.S2); writer.WriteEndMap(); var payload = writer.Encode();
            var proposal = new ProposedAuthorityFactV1(JournalFactId.Create(), null, OwnerSliceId.S2,
                registration.Schema, payload, AuthorityPayloadHashV1.Compute(registration.SchemaToken,
                    registration.Schema, payload), Correlation, new UtcInstant(1));
            Assert.IsType<AppendAuthorityResultV1.Committed>(await Journal.AppendAsync(
                new AppendAuthorityBatchV1(Session, 0, [], [proposal], ProposedAuthorityFactV1.MaximumPayloadBytes)));
        }
    }

    private sealed class LoseFirstAppendAckJournal(IAuthorityJournalV1 inner) : IAuthorityJournalV1
    {
        internal int AppendCalls { get; private set; }
        public async ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request,
            CancellationToken cancellationToken = default)
        {
            AppendCalls++; var result = await inner.AppendAsync(request, cancellationToken);
            return AppendCalls == 1 && result is AppendAuthorityResultV1.Committed
                ? new AppendAuthorityResultV1.OutcomeUnknown(OperationId.Create()) : result;
        }
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,
            CancellationToken cancellationToken = default) => inner.ReadAsync(request, cancellationToken);
    }

    private sealed class CommitOnlyOnEighthJournal(IAuthorityJournalV1 inner) : IAuthorityJournalV1
    {
        internal int AppendCalls { get; private set; }
        public async ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request,
            CancellationToken cancellationToken = default)
        {
            AppendCalls++;
            if (AppendCalls < 8) return new AppendAuthorityResultV1.SessionConflict(request.ExpectedSessionHead,
                checked(request.ExpectedSessionHead + 1));
            _ = await inner.AppendAsync(request, cancellationToken);
            return new AppendAuthorityResultV1.OutcomeUnknown(OperationId.Create());
        }

        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,
            CancellationToken cancellationToken = default) => inner.ReadAsync(request, cancellationToken);
    }
}
