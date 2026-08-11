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

    [Fact]
    public async Task Current_reader_reconstructs_only_nonterminal_grant()
    {
        var fixture = new Fixture(); var granted = Assert.IsType<CapacityAdmissionResultV1.Granted>(await fixture.ReserveAsync(fixture.Request(4)));
        var current = Assert.IsType<CapacityGrantReadResultV1.Current>(await CapacityAdmissionCoordinatorV1.ReadCurrentAsync(
            fixture.Journal, fixture.Session, granted.Grant.GrantId, fixture.Evidence(90)));
        Assert.Equal(granted.Grant.GrantId, current.Grant.GrantId);
        Assert.Equal(granted.Grant.OperationId, current.Grant.OperationId);
        Assert.Equal(granted.Grant.Authority, current.Grant.Authority);
        Assert.Equal(granted.Grant.GrantedAt, current.Grant.GrantedAt);
        Assert.Equal(granted.Grant.CurrentFact, current.Grant.CurrentFact);
        Assert.Equal(granted.Grant.ExpiresAt, current.Grant.ExpiresAt);
        Assert.Equal(granted.Grant.State, current.Grant.State);
        Assert.Equal(granted.Grant.Balances, current.Grant.Balances);
        Assert.IsType<CapacityGrantReadResultV1.NotObserved>(await CapacityAdmissionCoordinatorV1.ReadCurrentAsync(
            fixture.Journal, fixture.Session, CapacityGrantId.Create(), fixture.Evidence(90)));
    }

    [Fact]
    public async Task Historical_reader_returns_the_exact_grant_projection_at_the_named_fact()
    {
        var fixture = new Fixture(); var request = fixture.Request(4);
        var granted = Assert.IsType<CapacityAdmissionResultV1.Granted>(await fixture.ReserveAsync(request));
        var body = new CapacitySettlementFactBodyV1(granted.Grant.GrantId, OperationId.Create(), granted.Envelope.Position,
            CapacitySettlementKindV1.Activated,
            [new CapacitySettlementChargeV1(request.Charges[0].DimensionId, request.Charges[0].Scope, request.Charges[0].Purpose, 4)],
            fixture.Evidence(90));
        var activated = Assert.IsType<CapacityAdmissionResultV1.Settled>(await fixture.SettleAsync(body));

        var before = Assert.IsType<CapacityGrantSnapshotAtResultV1.Exact>(await CapacityGrantSnapshotReaderV1.ReadAtAsync(
            fixture.Journal, fixture.Session, granted.Grant.GrantId, granted.Envelope.Position));
        var after = Assert.IsType<CapacityGrantSnapshotAtResultV1.Exact>(await CapacityGrantSnapshotReaderV1.ReadAtAsync(
            fixture.Journal, fixture.Session, granted.Grant.GrantId, activated.Envelope.Position));

        Assert.Equal(CapacityGrantStateV1.Reserved, before.Grant.State);
        Assert.Equal(granted.Envelope.Position, before.Grant.CurrentFact);
        Assert.Equal(CapacityGrantStateV1.Active, after.Grant.State);
        Assert.Equal(activated.Envelope.Position, after.Grant.CurrentFact);
    }

    [Fact]
    public async Task Historical_reader_never_substitutes_a_latest_or_different_grant()
    {
        var fixture = new Fixture();
        var granted = Assert.IsType<CapacityAdmissionResultV1.Granted>(await fixture.ReserveAsync(fixture.Request(1)));

        Assert.IsType<CapacityGrantSnapshotAtResultV1.OutcomeUnknown>(await CapacityGrantSnapshotReaderV1.ReadAtAsync(
            fixture.Journal, fixture.Session, CapacityGrantId.Create(), granted.Envelope.Position));
        var missingFuture = new JournalPositionV1(fixture.Session, granted.Envelope.Position.Sequence + 1);
        Assert.IsType<CapacityGrantSnapshotAtResultV1.OutcomeUnknown>(await CapacityGrantSnapshotReaderV1.ReadAtAsync(
            fixture.Journal, fixture.Session, granted.Grant.GrantId, missingFuture));

        var other = Assert.IsType<CapacityAdmissionResultV1.Granted>(await fixture.ReserveAsync(fixture.Request(1)));
        Assert.IsType<CapacityGrantSnapshotAtResultV1.OutcomeUnknown>(await CapacityGrantSnapshotReaderV1.ReadAtAsync(
            fixture.Journal, fixture.Session, granted.Grant.GrantId, other.Envelope.Position));
    }

    [Fact]
    public async Task Historical_reader_pages_with_exact_bounds_and_observes_post_await_cancellation()
    {
        var fixture = new Fixture(); var granted = Assert.IsType<CapacityAdmissionResultV1.Granted>(
            await fixture.ReserveAsync(fixture.Request(1)));
        var paged = new BoundedReadJournal(fixture.Journal, oneItemPages: true);
        Assert.IsType<CapacityGrantSnapshotAtResultV1.Exact>(await CapacityGrantSnapshotReaderV1.ReadAtAsync(
            paged, fixture.Session, granted.Grant.GrantId, granted.Envelope.Position));
        Assert.All(paged.Requests, request => { Assert.Equal((ushort)256, request.MaximumFacts); Assert.Equal(65_536u, request.MaximumEncodedBytes); });

        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await CapacityGrantSnapshotReaderV1.ReadAtAsync(
            new CancellationIgnoringJournal(fixture.Journal), fixture.Session, granted.Grant.GrantId,
            granted.Envelope.Position, cancellation.Token));
    }

    [Theory]
    [InlineData("store")]
    [InlineData("oversize")]
    [InlineData("throw")]
    public async Task Historical_reader_maps_untrusted_read_failures_to_unknown(string mode)
    {
        var fixture = new Fixture(); var granted = Assert.IsType<CapacityAdmissionResultV1.Granted>(
            await fixture.ReserveAsync(fixture.Request(1)));
        Assert.IsType<CapacityGrantSnapshotAtResultV1.OutcomeUnknown>(await CapacityGrantSnapshotReaderV1.ReadAtAsync(
            new FailingReadJournal(fixture.Journal, mode), fixture.Session, granted.Grant.GrantId, granted.Envelope.Position));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("hash")]
    [InlineData("id")]
    [InlineData("version")]
    public async Task Historical_reader_rejects_tampered_capacity_envelopes(string mode)
    {
        var fixture = new Fixture(); var granted = Assert.IsType<CapacityAdmissionResultV1.Granted>(
            await fixture.ReserveAsync(fixture.Request(1)));
        Assert.IsType<CapacityGrantSnapshotAtResultV1.OutcomeUnknown>(await CapacityGrantSnapshotReaderV1.ReadAtAsync(
            new TamperingReadJournal(fixture.Journal, mode), fixture.Session, granted.Grant.GrantId, granted.Envelope.Position));
    }

    [Fact]
    public async Task Historical_reader_has_an_exact_65536_fact_frontier()
    {
        var fixture = new Fixture(); var granted = Assert.IsType<CapacityAdmissionResultV1.Granted>(
            await fixture.ReserveAsync(fixture.Request(1)));
        var atLimit = new JournalPositionV1(fixture.Session, 65_536);
        Assert.IsType<CapacityGrantSnapshotAtResultV1.Exact>(await CapacityGrantSnapshotReaderV1.ReadAtAsync(
            new FrontierJournal(granted.Envelope, 65_536), fixture.Session, granted.Grant.GrantId, atLimit));
        var overLimit = new JournalPositionV1(fixture.Session, 65_537);
        Assert.IsType<CapacityGrantSnapshotAtResultV1.OutcomeUnknown>(await CapacityGrantSnapshotReaderV1.ReadAtAsync(
            new FrontierJournal(granted.Envelope, 65_537), fixture.Session, granted.Grant.GrantId, overLimit));
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

    private sealed class BoundedReadJournal(IAuthorityJournalV1 inner, bool oneItemPages) : IAuthorityJournalV1
    {
        internal List<ReadAuthorityRangeV1> Requests { get; } = [];
        public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default) =>
            inner.AppendAsync(request, cancellationToken);
        public async ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var effective = oneItemPages ? new ReadAuthorityRangeV1(request.Session, request.AfterExclusive,
                request.ThroughInclusive, 1, request.MaximumEncodedBytes) : request;
            return await inner.ReadAsync(effective, cancellationToken);
        }
    }

    private sealed class CancellationIgnoringJournal(IAuthorityJournalV1 inner) : IAuthorityJournalV1
    {
        public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default) => inner.AppendAsync(request, cancellationToken);
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request, CancellationToken cancellationToken = default) => inner.ReadAsync(request, CancellationToken.None);
    }

    private sealed class FailingReadJournal(IAuthorityJournalV1 inner, string mode) : IAuthorityJournalV1
    {
        public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default) => inner.AppendAsync(request, cancellationToken);
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request, CancellationToken cancellationToken = default) => mode switch
        {
            "store" => ValueTask.FromResult<ReadAuthorityRangeResultV1>(new ReadAuthorityRangeResultV1.StoreUnavailable(new BoundedAscii("offline"))),
            "oversize" => ValueTask.FromResult<ReadAuthorityRangeResultV1>(new ReadAuthorityRangeResultV1.ItemTooLarge(
                new JournalPositionV1(request.Session, request.AfterExclusive + 1), request.MaximumEncodedBytes + 1UL, request.MaximumEncodedBytes)),
            "throw" => throw new IOException("read failed"),
            _ => inner.ReadAsync(request, cancellationToken),
        };
    }

    private sealed class TamperingReadJournal(IAuthorityJournalV1 inner, string mode) : IAuthorityJournalV1
    {
        public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default) => inner.AppendAsync(request, cancellationToken);
        public async ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request, CancellationToken cancellationToken = default)
        {
            var result = await inner.ReadAsync(request, cancellationToken);
            if (result is not ReadAuthorityRangeResultV1.Batch batch || batch.Facts.Count == 0) return result;
            var original = batch.Facts[0];
            var schema = mode == "version" ? new SchemaReferenceV1(original.PayloadSchema.SchemaId,
                checked((ushort)(original.PayloadSchema.Major + 1)), original.PayloadSchema.Minor) : original.PayloadSchema;
            var changed = new AuthorityFactEnvelopeV1(
                mode == "id" ? JournalFactId.Create() : original.FactId, original.Position, original.ThreadScope,
                mode == "owner" ? OwnerSliceId.S3 : original.Owner, schema, original.Payload.ToArray(),
                mode == "hash" ? Hash256.FromBytes(Enumerable.Repeat((byte)9, 32).ToArray()) : original.PayloadHash,
                original.Correlation, original.ObservedAt, original.AdmittedAt, original.Integrity);
            return new ReadAuthorityRangeResultV1.Batch(batch.Session, batch.SnapshotHead, batch.AfterExclusive,
                batch.SnapshotThrough, new[] { changed }.Concat(batch.Facts.Skip(1)), batch.HasMore);
        }
    }

    private sealed class FrontierJournal(AuthorityFactEnvelopeV1 reservation, long through) : IAuthorityJournalV1
    {
        private readonly SchemaReferenceV1 _fillerSchema = new(SchemaId.Create(), 1, 0);
        public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var end = Math.Min(through, request.AfterExclusive + request.MaximumFacts);
            var facts = new List<AuthorityFactEnvelopeV1>();
            for (var sequence = request.AfterExclusive + 1; sequence <= end; sequence++)
                facts.Add(sequence == through && through == 65_536 ? CopyReservation(sequence) : Filler(sequence));
            return ValueTask.FromResult<ReadAuthorityRangeResultV1>(new ReadAuthorityRangeResultV1.Batch(
                request.Session, through, request.AfterExclusive, through, facts, end < through));
        }
        private AuthorityFactEnvelopeV1 CopyReservation(long sequence) => new(reservation.FactId,
            new JournalPositionV1(reservation.Position.Session, sequence), null, reservation.Owner,
            reservation.PayloadSchema, reservation.Payload.ToArray(), reservation.PayloadHash, reservation.Correlation,
            reservation.ObservedAt, reservation.AdmittedAt, reservation.Integrity);
        private AuthorityFactEnvelopeV1 Filler(long sequence) => new(JournalFactId.Create(),
            new JournalPositionV1(reservation.Position.Session, sequence), null, OwnerSliceId.S3,
            _fillerSchema, ReadOnlySpan<byte>.Empty, reservation.PayloadHash, reservation.Correlation,
            reservation.ObservedAt, reservation.AdmittedAt, reservation.Integrity);
    }
}
