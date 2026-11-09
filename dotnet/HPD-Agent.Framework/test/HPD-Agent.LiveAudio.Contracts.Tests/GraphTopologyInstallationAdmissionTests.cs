using System.Formats.Cbor;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphTopologyInstallationAdmissionTests
{
    [Fact]
    public async Task Installation_is_one_fact_idempotent_and_conflicting_topology_is_closed()
    {
        var f = await Fixture.CreateAsync();
        var raw = await GraphTopologyInstallationAdmission.InstallAsync(f.Journal, f.Request);
        Assert.True(raw is GraphTopologyInstallationAdmissionResult.Installed, raw.ToString());
        var installed = (GraphTopologyInstallationAdmissionResult.Installed)raw;
        Assert.Equal(4, installed.Envelope.Position.Sequence);

        var duplicate = Assert.IsType<GraphTopologyInstallationAdmissionResult.AlreadyInstalled>(
            await GraphTopologyInstallationAdmission.InstallAsync(f.Journal, f.Request));
        Assert.Equal(installed.Envelope.Position, duplicate.Envelope.Position);

        var other = new GraphTopologyPlan(f.Session, f.GraphGeneration, f.Grant.GrantId,
            [new GraphTopologyNode(new BoundedAscii("other"))], [], [new CapacityDimensionId(3)]);
        Assert.IsType<GraphTopologyInstallationAdmissionResult.Conflict>(
            await GraphTopologyInstallationAdmission.InstallAsync(f.Journal, f.RequestFor(other)));
    }

    [Fact]
    public async Task Committed_then_lost_ack_is_reconciled_without_duplicate_growth()
    {
        var f = await Fixture.CreateAsync();
        var port = new LoseFirstAppendAckJournal(f.Journal);
        var raw = await GraphTopologyInstallationAdmission.InstallAsync(port, f.Request);
        Assert.True(raw is GraphTopologyInstallationAdmissionResult.Installed, raw.ToString());
        var result = (GraphTopologyInstallationAdmissionResult.Installed)raw;

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
        var wrongPlan = new GraphTopologyPlan(f.Session,
            ((AuthorityAxisValueV1.Graph)wrongGraph.Axes.Single().Value).Value, f.Grant.GrantId,
            [new GraphTopologyNode(new BoundedAscii("source"))], [], [new CapacityDimensionId(3)]);
        var stale = new GraphTopologyInstallationRequest(f.Session, wrongPlan, f.Grant.CurrentFact,
            wrongGraph, f.Correlation, new UtcInstant(10));
        Assert.IsType<GraphTopologyInstallationAdmissionResult.Rejected>(
            await GraphTopologyInstallationAdmission.InstallAsync(f.Journal, stale));

        var missing = new GraphTopologyInstallationRequest(f.Session, f.Plan, new JournalPositionV1(f.Session, 1),
            f.Authority, f.Correlation, new UtcInstant(10));
        Assert.IsType<GraphTopologyInstallationAdmissionResult.OutcomeUnknown>(
            await GraphTopologyInstallationAdmission.InstallAsync(f.Journal, missing));
        var read = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await f.Journal.ReadAsync(
            new ReadAuthorityRangeV1(f.Session, 0, long.MaxValue, 16, ProposedAuthorityFactV1.MaximumPayloadBytes)));
        Assert.Equal(3, read.SnapshotThrough);
    }

    [Fact]
    public async Task Eighth_lost_ack_is_reconciled_by_the_mandatory_final_read()
    {
        var f = await Fixture.CreateAsync();
        var journal = new CommitOnlyOnEighthJournal(f.Journal);

        var installed = Assert.IsType<GraphTopologyInstallationAdmissionResult.Installed>(
            await GraphTopologyInstallationAdmission.InstallAsync(journal, f.Request));

        Assert.Equal(8, journal.AppendCalls);
        Assert.Equal(4, installed.Envelope.Position.Sequence);
    }

    [Fact]
    public void Result_union_rejects_invalid_values()
    {
        Assert.Throws<ArgumentNullException>(() => new GraphTopologyInstallationAdmissionResult.Installed(null!));
        Assert.Throws<ArgumentException>(() => new GraphTopologyInstallationAdmissionResult.RuntimeReplaced(default));
        Assert.Throws<ArgumentException>(() => new GraphTopologyInstallationAdmissionResult.Rejected(default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphTopologyInstallationAdmissionResult.RetryRequired(-1));
        Assert.Throws<ArgumentException>(() => new GraphTopologyInstallationAdmissionResult.OutcomeUnknown(
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
        internal GraphTopologyPlan Plan { get; private set; } = null!;
        internal CorrelationEnvelopeV1 Correlation { get; } = new(TenantId.Create());
        internal GraphTopologyInstallationRequest Request => RequestFor(Plan);

        internal static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture();
            f.Authority = ExpectedAuthorityVectorV1.Create(f.Session,
                [new AuthorityAxisValueV1.Graph(f.GraphGeneration)]);
            f.Journal = new InMemoryAuthorityJournalV1(new AuthorityPayloadAdmissionRegistryV1([
                new AuthorityGenerationInitializationPayloadRegistrationV1(AuthorityAxisId.Graph),
                new CapacityReservationPayloadRegistrationV1(), new CapacitySettlementPayloadRegistrationV1(),
                GraphReplacementPayloadRegistrations.Installed,
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
            f.Plan = new GraphTopologyPlan(f.Session, f.GraphGeneration, f.Grant.GrantId,
                [new GraphTopologyNode(new BoundedAscii("source"))], [], [new CapacityDimensionId(3)]);
            return f;
        }

        internal GraphTopologyInstallationRequest RequestFor(GraphTopologyPlan plan) =>
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
