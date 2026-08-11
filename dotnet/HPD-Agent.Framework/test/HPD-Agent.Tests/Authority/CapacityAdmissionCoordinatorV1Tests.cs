using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class CapacityAdmissionCoordinatorV1Tests
{
    [Fact]
    public async Task Reservation_IsSoleJournalFactAndRetryIsIdempotent()
    {
        var fixture = new Fixture(); var request = fixture.Request(4);
        var first = Assert.IsType<CapacityAdmissionResultV1.Granted>(await fixture.ReserveAsync(request));
        var retry = Assert.IsType<CapacityAdmissionResultV1.AlreadyGranted>(await fixture.ReserveAsync(request));

        Assert.Equal(1, first.Envelope.Position.Sequence);
        Assert.Equal(first.Envelope.FactId, retry.Envelope.FactId);
        Assert.Equal(CapacityGrantStateV1.Reserved, retry.Grant.State);
        Assert.Equal(4, retry.Grant.Balances.Single().Unactivated);
    }

    [Fact]
    public async Task SameOperationWithDifferentCanonicalRequestIsContradictory()
    {
        var fixture = new Fixture(); var request = fixture.Request(4);
        Assert.IsType<CapacityAdmissionResultV1.Granted>(await fixture.ReserveAsync(request));
        var changed = fixture.Request(5, request.OperationId);

        Assert.IsType<CapacityAdmissionResultV1.ContradictoryDuplicate>(await fixture.ReserveAsync(changed));
    }

    [Fact]
    public async Task Settlement_IsPredecessorFencedAndReplaysFromFacts()
    {
        var fixture = new Fixture(); var request = fixture.Request(4);
        var granted = Assert.IsType<CapacityAdmissionResultV1.Granted>(await fixture.ReserveAsync(request));
        var operation = OperationId.Create();
        var body = new CapacitySettlementFactBodyV1(granted.Grant.GrantId, operation, granted.Envelope.Position,
            CapacitySettlementKindV1.Activated,
            [new CapacitySettlementChargeV1(request.Charges[0].DimensionId, request.Charges[0].Scope, request.Charges[0].Purpose, 4)],
            fixture.Evidence(90));

        var settled = Assert.IsType<CapacityAdmissionResultV1.Settled>(await fixture.SettleAsync(body));
        var retry = Assert.IsType<CapacityAdmissionResultV1.Settled>(await fixture.SettleAsync(body));

        Assert.Equal(2, settled.Envelope.Position.Sequence);
        Assert.Equal(settled.Envelope.FactId, retry.Envelope.FactId);
        Assert.Equal(CapacityGrantStateV1.Active, settled.Grant.State);
        Assert.Equal(4, settled.Grant.Balances.Single().Active);
        Assert.Equal(0, settled.Grant.Balances.Single().Unactivated);
    }

    [Fact]
    public async Task RefusalAndStalePredecessorAppendNoFact()
    {
        var fixture = new Fixture(); var chargedOperation = OperationId.Create();
        Assert.IsType<CapacityAdmissionResultV1.Granted>(await fixture.ReserveAsync(fixture.Request(1024, scopeOperation: chargedOperation)));
        var refused = Assert.IsType<CapacityAdmissionResultV1.Refused>(await fixture.ReserveAsync(fixture.Request(1, scopeOperation: chargedOperation)));
        Assert.Equal("capacity-exceeded", refused.SafeCode.ToString());

        var request = fixture.Request(4); var granted = Assert.IsType<CapacityAdmissionResultV1.Granted>(await fixture.ReserveAsync(request));
        var wrong = new CapacitySettlementFactBodyV1(granted.Grant.GrantId, OperationId.Create(),
            new JournalPositionV1(fixture.Session, granted.Envelope.Position.Sequence + 1), CapacitySettlementKindV1.Activated,
            [new CapacitySettlementChargeV1(request.Charges[0].DimensionId, request.Charges[0].Scope, request.Charges[0].Purpose, 1)], fixture.Evidence(90));
        Assert.IsType<CapacityAdmissionResultV1.Refused>(await fixture.SettleAsync(wrong));

        var read = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await fixture.Journal.ReadAsync(
            new ReadAuthorityRangeV1(fixture.Session, 0, long.MaxValue, 16, 1_048_576)));
        Assert.Equal(2, read.Facts.Count);
    }

    [Fact]
    public async Task ExpiredRequestFailsBeforeSnapshotOrAppend()
    {
        var fixture = new Fixture(); var request = fixture.Request(1);
        Assert.IsType<CapacityAdmissionResultV1.DeadlineExpired>(await fixture.ReserveAsync(request, 101));
        var read = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await fixture.Journal.ReadAsync(
            new ReadAuthorityRangeV1(fixture.Session, 0, long.MaxValue, 16, 1_048_576)));
        Assert.Empty(read.Facts);
    }

    private sealed class Fixture
    {
        private readonly TenantId _tenant = TenantId.Create();
        private readonly ClockDomainId _clock = ClockDomainId.Create();
        private readonly BootId _boot = BootId.Create();

        internal Fixture()
        {
            Session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
            Journal = new InMemoryAuthorityJournalV1(new AuthorityPayloadAdmissionRegistryV1([
                new CapacityReservationPayloadRegistrationV1(), new CapacitySettlementPayloadRegistrationV1()]),
                () => new UtcInstant(100), new AuthorityJournalCapacityV1(4, 64, 4 * 1024 * 1024));
        }

        internal SessionAuthorityStampV1 Session { get; }
        internal InMemoryAuthorityJournalV1 Journal { get; }
        internal MonotonicStampV1 Evidence(ulong value) => new(_clock, _boot, value);

        internal CapacityRequestV1 Request(long amount, OperationId? operation = null, OperationId? scopeOperation = null)
        {
            var requestOperation = operation ?? OperationId.Create();
            return new(requestOperation, ExpectedAuthorityVectorV1.Create(Session, []),
                [new CapacityChargeV1(CapacityDimensionsV1.QueueItems,
                    new CapacityScopeV1(_tenant, null, new CapacitySubjectV1.Operation(scopeOperation ?? requestOperation)), amount,
                    CapacityPurposeId.Create(), new CapacityChargeWindowV1.NoWindow())], Evidence(100), CapacityPriorityV1.Normal);
        }

        internal ValueTask<CapacityAdmissionResultV1> ReserveAsync(CapacityRequestV1 request, ulong admissionTime = 90) =>
            CapacityAdmissionCoordinatorV1.ReserveAsync(Journal, request, new CapacityGrantExpiryV1.NoExpiry(),
                Correlation(request.OperationId), Evidence(admissionTime), new UtcInstant(50));

        internal ValueTask<CapacityAdmissionResultV1> SettleAsync(CapacitySettlementFactBodyV1 body) =>
            CapacityAdmissionCoordinatorV1.SettleAsync(Journal, Session, body, Correlation(body.OperationId), new UtcInstant(60));

        private CorrelationEnvelopeV1 Correlation(OperationId operation) => new(_tenant, operationId: operation);
    }
}
