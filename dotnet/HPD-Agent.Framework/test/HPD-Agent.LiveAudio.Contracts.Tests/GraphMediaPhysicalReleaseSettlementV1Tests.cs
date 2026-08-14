using System.Formats.Cbor;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphMediaPhysicalReleaseSettlementV1Tests
{
    [Fact]
    public async Task Released_proof_admits_exact_S2_release_and_retry_is_idempotent()
    {
        var fixture = await CreateFixtureAsync();
        var coordinator = new GraphMediaPhysicalReleaseSettlementCoordinatorV1(
            fixture.S1, fixture.S2, fixture.ReleaseRegistry);

        var first = Assert.IsType<GraphMediaPhysicalReleaseSettlementResultV1.Settled>(
            await coordinator.SettleAsync(fixture.Released));
        AssertClosed(first.Grant, fixture.Charge, first.Envelope.Position);

        var retry = Assert.IsType<GraphMediaPhysicalReleaseSettlementResultV1.Settled>(
            await coordinator.SettleAsync(fixture.Released));
        Assert.Equal(first.Envelope.Position, retry.Envelope.Position);
        AssertClosed(retry.Grant, fixture.Charge, retry.Envelope.Position);
    }

    [Fact]
    public async Task Claimed_release_must_be_the_exact_fold_authenticated_S1_result()
    {
        var fixture = await CreateFixtureAsync();
        var forged = fixture.Released with { EvidenceHash = Hash(250) };
        var result = Assert.IsType<GraphMediaPhysicalReleaseSettlementResultV1.Quarantined>(
            await new GraphMediaPhysicalReleaseSettlementCoordinatorV1(fixture.S1, fixture.S2,
                fixture.ReleaseRegistry).SettleAsync(forged));
        Assert.Equal("release-history-invalid", result.SafeCode.ToString());
        Assert.Equal(fixture.Grant.CurrentFact, (await ReadGrantAsync(fixture)).CurrentFact);
    }

    [Fact]
    public async Task Capacity_predecessor_and_assignment_are_pinned_before_settlement()
    {
        var fixture = await CreateFixtureAsync();
        var original = fixture.Released.FactBody;
        var changedFactBody = new GraphMediaPhysicalReleaseFactBodyV1(original.CommandPosition,
            original.ResidenceId, original.ResidenceRequestHash, original.GrantId, Position(fixture.Session, 99),
            original.Assignment, original.Outcome, original.EvidenceHash, original.SafeCode, original.ObservedAt);
        var forged = fixture.Released with { FactBody = changedFactBody };
        var result = Assert.IsType<GraphMediaPhysicalReleaseSettlementResultV1.Quarantined>(
            await new GraphMediaPhysicalReleaseSettlementCoordinatorV1(fixture.S1, fixture.S2,
                fixture.ReleaseRegistry).SettleAsync(forged));
        Assert.Equal("release-history-invalid", result.SafeCode.ToString());
    }

    [Fact]
    public async Task Cancellation_before_authentication_performs_no_settlement()
    {
        var fixture = await CreateFixtureAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await
            new GraphMediaPhysicalReleaseSettlementCoordinatorV1(fixture.S1, fixture.S2,
                fixture.ReleaseRegistry).SettleAsync(fixture.Released, cancellation.Token));
        Assert.Equal(fixture.Grant.CurrentFact, (await ReadGrantAsync(fixture)).CurrentFact);
    }

    [Fact]
    public async Task Authenticated_release_assignment_must_match_the_pinned_grant()
    {
        var fixture = await CreateFixtureAsync(releaseAmount: 401);
        var result = Assert.IsType<GraphMediaPhysicalReleaseSettlementResultV1.Quarantined>(await
            new GraphMediaPhysicalReleaseSettlementCoordinatorV1(fixture.S1, fixture.S2,
                fixture.ReleaseRegistry).SettleAsync(fixture.Released));
        Assert.Equal("capacity-release-join-invalid", result.SafeCode.ToString());
        Assert.Equal(fixture.Grant.CurrentFact, (await ReadGrantAsync(fixture)).CurrentFact);
    }

    [Fact]
    public async Task Cancellation_after_settlement_invocation_cannot_hide_the_durable_result()
    {
        var fixture = await CreateFixtureAsync();
        using var cancellation = new CancellationTokenSource();
        var s2 = new CancelAfterAppendJournal(fixture.S2, cancellation);
        var result = Assert.IsType<GraphMediaPhysicalReleaseSettlementResultV1.Settled>(await
            new GraphMediaPhysicalReleaseSettlementCoordinatorV1(fixture.S1, s2,
                fixture.ReleaseRegistry).SettleAsync(fixture.Released, cancellation.Token));
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(1, s2.AppendCalls);
        AssertClosed(result.Grant, fixture.Charge, result.Envelope.Position);
    }

    private static async Task<Fixture> CreateFixtureAsync(long? releaseAmount = null)
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(1)), LiveSessionId.FromValue(Id(2)));
        var graph = GraphGenerationId.FromValue(Id(3));
        var authority = ExpectedAuthorityVectorV1.Create(session, [new AuthorityAxisValueV1.Graph(graph)]);
        var tenant = TenantId.FromValue(Id(4));
        var participant = ParticipantId.FromValue(Id(5));
        var capacityOperation = Operation(6);
        var releaseOperation = Operation(7);
        var correlation = new CorrelationEnvelopeV1(tenant, sessionId: SessionId.FromValue(Id(8)), operationId: capacityOperation);
        var releaseCorrelation = new CorrelationEnvelopeV1(tenant, sessionId: SessionId.FromValue(Id(8)), operationId: releaseOperation);
        var scope = new CapacityScopeV1(tenant, SessionId.FromValue(Id(8)), new CapacitySubjectV1.Participant(participant));
        var charge = new CapacityChargeV1(new CapacityDimensionId(1), scope, 400,
            CapacityPurposeId.FromValue(Id(9)), new CapacityChargeWindowV1.NoWindow());
        var request = new CapacityRequestV1(capacityOperation, authority, [charge], Stamp(100), CapacityPriorityV1.Normal);

        var initialization = new AuthorityGenerationInitializationPayloadRegistrationV1(AuthorityAxisId.Graph);
        var capacityRegistry = new AuthorityPayloadAdmissionRegistryV1([
            initialization, new CapacityReservationPayloadRegistrationV1(), new CapacitySettlementPayloadRegistrationV1()]);
        var s2 = new InMemoryAuthorityJournalV1(capacityRegistry, () => new UtcInstant(10),
            new AuthorityJournalCapacityV1(1, 64, 8_000_000));
        var initializationBytes = EncodeGraphInitialization(session, graph);
        Assert.IsType<AppendAuthorityResultV1.Committed>(await s2.AppendAsync(new(session, 0, [], [new(
            JournalFactId.Create(), null, OwnerSliceId.S2, initialization.Schema, initializationBytes,
            AuthorityPayloadHashV1.Compute(initialization.SchemaToken, initialization.Schema, initializationBytes),
            new CorrelationEnvelopeV1(tenant), new UtcInstant(10))], 1_000_000)));
        var granted = Assert.IsType<CapacityAdmissionResultV1.Granted>(await CapacityAdmissionCoordinatorV1.ReserveAsync(
            s2, request, new CapacityGrantExpiryV1.NoExpiry(), correlation, Stamp(10), new UtcInstant(11)));

        var releaseCharge = releaseAmount is null ? charge : new CapacityChargeV1(charge.DimensionId,
            charge.Scope, releaseAmount.Value, charge.Purpose, charge.Window);
        var assignment = new GraphMediaCapacityAssignmentV1(releaseCharge, GraphMediaRepresentationArmV1.ResidentBytes);
        var residence = new GraphMediaReleaseResidenceProofV1(capacityOperation, Hash(20), Id(21), Id(22), graph,
            new BoundedAscii("node"), participant, Position(session, 1), Position(session, 2), Position(session, 3),
            Position(session, 4), granted.Grant.GrantId, granted.Grant.GrantedAt, granted.Grant.CurrentFact,
            Hash(23), Hash(24), Hash(25), assignment, GraphMediaResidenceClassV1.Controlled,
            GraphMediaResidenceStateV1.Visible);
        var owner = new GraphMediaOwnerReleaseProofV1(residence.OwnerId, Operation(26), Hash(27),
            GraphMediaOwnerTransitionResultV1.Disposed, Hash(28), 0, Hash(29));
        var work = new GraphMediaWorkReleaseProofV1(Hash(30), GraphMediaReleaseEligibilityV1.Eligible, 1, 1);
        var commandBody = new GraphMediaPhysicalReleaseCommandBodyV1(releaseOperation, residence, owner, work,
            null, null, Stamp(40));
        var releaseRegistry = new AuthorityPayloadAdmissionRegistryV1([
            GraphMediaPhysicalReleasePayloadRegistrationsV1.Command,
            GraphMediaPhysicalReleasePayloadRegistrationsV1.Fact]);
        var s1 = new InMemoryAuthorityJournalV1(releaseRegistry, () => new UtcInstant(20),
            new AuthorityJournalCapacityV1(1, 64, 8_000_000));
        var commandPayload = GraphMediaPhysicalReleaseCodecsV1.EncodeOuter(new(session, authority,
            GraphMediaPhysicalReleaseCodecsV1.EncodeCommandBody(commandBody)));
        var commandProposal = Proposal(GraphMediaPhysicalReleasePayloadRegistrationsV1.Command,
            GraphMediaPhysicalReleaseFactIdsV1.Command(session, releaseOperation), commandPayload,
            releaseCorrelation, new UtcInstant(20));
        var command = Assert.IsType<AppendAuthorityResultV1.Committed>(await s1.AppendAsync(
            new(session, 0, [], [commandProposal], 1_000_000))).Envelopes.Single();
        var factBody = new GraphMediaPhysicalReleaseFactBodyV1(command.Position, residence.ResidenceId,
            residence.RequestHash, residence.GrantId, residence.CurrentFact, assignment,
            GraphMediaPhysicalReleaseOutcomeV1.Released, Hash(31), null, commandBody.ObservedAt);
        var factPayload = GraphMediaPhysicalReleaseCodecsV1.EncodeOuter(new(session, authority,
            GraphMediaPhysicalReleaseCodecsV1.EncodeFactBody(factBody)));
        var factProposal = Proposal(GraphMediaPhysicalReleasePayloadRegistrationsV1.Fact,
            GraphMediaPhysicalReleaseFactIdsV1.Fact(command.Position), factPayload, releaseCorrelation,
            new UtcInstant(20));
        var fact = Assert.IsType<AppendAuthorityResultV1.Committed>(await s1.AppendAsync(
            new(session, 1, [], [factProposal], 1_000_000))).Envelopes.Single();
        var fold = GraphMediaPhysicalReleaseFoldV1.Create(session, residence.ResidenceId, releaseRegistry);
        Assert.IsType<GraphMediaPhysicalReleaseFoldApplyResultV1.Applied>(fold.Apply(command));
        Assert.IsType<GraphMediaPhysicalReleaseFoldApplyResultV1.Applied>(fold.Apply(fact));
        var released = Assert.IsType<GraphMediaPhysicalReleaseFoldResultV1.Released>(fold.Complete());
        return new(session, s1, s2, releaseRegistry, released, granted.Grant, charge);
    }

    private static async Task<CapacityGrantSnapshotV1> ReadGrantAsync(Fixture fixture)
    {
        var read = await CapacityGrantSnapshotReaderV1.ReadAtAsync(fixture.S2, fixture.Session,
            fixture.Grant.GrantId, fixture.Grant.CurrentFact);
        return Assert.IsType<CapacityGrantSnapshotAtResultV1.Exact>(read).Grant;
    }

    private static void AssertClosed(CapacityGrantSnapshotV1 grant, CapacityChargeV1 charge, JournalPositionV1 current)
    {
        Assert.Equal(current, grant.CurrentFact);
        var balance = Assert.Single(grant.Balances, value => value.Charge == charge);
        Assert.Equal(0, balance.Unactivated); Assert.Equal(0, balance.Active);
        Assert.Equal(0, balance.Revoked); Assert.Equal(0, balance.ExplicitlyUnknown);
        Assert.Equal(charge.Amount, balance.Released);
        Assert.Equal(0, balance.EncumberedNormal); Assert.Equal(0, balance.EncumberedReserve);
    }

    private static ProposedAuthorityFactV1 Proposal(AuthorityPayloadRegistrationV1 registration,
        JournalFactId factId, byte[] payload, CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        new(factId, null, OwnerSliceId.S1, registration.Schema, payload,
            AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload), correlation, observedAt);

    private static byte[] EncodeGraphInitialization(SessionAuthorityStampV1 session, GraphGenerationId graph)
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(3); writer.WriteUInt64(1); SessionAuthorityStampV1Codec.Write(writer, session);
        writer.WriteUInt64(2); Span<byte> bytes = stackalloc byte[16]; Assert.True(graph.TryWriteBytes(bytes));
        writer.WriteByteString(bytes); writer.WriteUInt64(3); writer.WriteUInt64((ushort)OwnerSliceId.S2);
        writer.WriteEndMap(); return writer.Encode();
    }

    private static StableId128 Id(byte seed) { var bytes = new byte[16]; Array.Fill(bytes, seed); return StableId128.FromBytes(bytes); }
    private static OperationId Operation(byte seed) => OperationId.FromValue(Id(seed));
    private static Hash256 Hash(byte seed) => Hash256.Compute([seed]);
    private static MonotonicStampV1 Stamp(ulong ticks) => new(ClockDomainId.FromValue(Id(50)), BootId.FromValue(Id(51)), ticks);
    private static JournalPositionV1 Position(SessionAuthorityStampV1 session, long sequence) => new(session, sequence);
    private sealed record Fixture(SessionAuthorityStampV1 Session, InMemoryAuthorityJournalV1 S1,
        InMemoryAuthorityJournalV1 S2, AuthorityPayloadAdmissionRegistryV1 ReleaseRegistry,
        GraphMediaPhysicalReleaseFoldResultV1.Released Released, CapacityGrantSnapshotV1 Grant, CapacityChargeV1 Charge);

    private sealed class CancelAfterAppendJournal(IAuthorityJournalV1 inner, CancellationTokenSource cancellation) : IAuthorityJournalV1
    {
        internal int AppendCalls { get; private set; }
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,
            CancellationToken cancellationToken = default) => inner.ReadAsync(request, cancellationToken);
        public async ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request,
            CancellationToken cancellationToken = default)
        {
            AppendCalls++;
            var result = await inner.AppendAsync(request, cancellationToken);
            cancellation.Cancel();
            return result;
        }
    }
}
