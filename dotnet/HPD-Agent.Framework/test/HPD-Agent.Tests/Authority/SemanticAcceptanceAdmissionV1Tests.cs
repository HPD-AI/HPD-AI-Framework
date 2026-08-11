using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class SemanticAcceptanceAdmissionV1Tests
{
    [Fact]
    public async Task Admission_CommitsOnceAndRetryReturnsTheOriginalFact()
    {
        var fixture = new Fixture();
        var disposition = await fixture.SeedDispositionAsync(SubmissionDispositionV1.SubmissionClaimed);

        var first = await SemanticAcceptanceAdmissionV1.AdmitAsync(fixture.Journal, disposition);
        var retry = await SemanticAcceptanceAdmissionV1.AdmitAsync(fixture.Journal, disposition);

        var committed = Assert.IsType<SemanticAcceptanceAdmissionResultV1.Committed>(first).Envelope;
        var existing = Assert.IsType<SemanticAcceptanceAdmissionResultV1.AlreadyCommitted>(retry).Envelope;
        Assert.Equal(committed.FactId, existing.FactId);
        Assert.Equal(3, committed.Position.Sequence);
        Assert.Equal(OwnerSliceId.AgentCore, committed.Owner);
        Assert.True(SemanticInputAcceptedV1Codec.TryDecode(committed.PayloadMemory, out var accepted));
        Assert.NotNull(accepted);
        Assert.Equal(disposition, accepted!.SourcePosition);
        Assert.Equal(SemanticInputAcceptanceDispositionV1.Accepted, accepted.Disposition);
    }

    [Fact]
    public async Task Admission_RetryReturnsOriginalFactAfterTheJournalAdvances()
    {
        var fixture = new Fixture();
        var disposition = await fixture.SeedDispositionAsync(SubmissionDispositionV1.SubmissionClaimed);
        var committed = Assert.IsType<SemanticAcceptanceAdmissionResultV1.Committed>(
            await SemanticAcceptanceAdmissionV1.AdmitAsync(fixture.Journal, disposition)).Envelope;
        await fixture.AppendKnownFactAsync(expectedHead: 3);

        var retry = Assert.IsType<SemanticAcceptanceAdmissionResultV1.AlreadyCommitted>(
            await SemanticAcceptanceAdmissionV1.AdmitAsync(fixture.Journal, disposition)).Envelope;

        Assert.Equal(committed.FactId, retry.FactId);
        Assert.Equal(committed.Position, retry.Position);
        Assert.Equal(4, Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await fixture.Journal.ReadAsync(
            new ReadAuthorityRangeV1(fixture.Session, 0, long.MaxValue, 16, 65_536))).SnapshotThrough);
    }

    [Fact]
    public async Task Admission_DoesNotAppendAnIneligibleDisposition()
    {
        var fixture = new Fixture();
        var disposition = await fixture.SeedDispositionAsync(SubmissionDispositionV1.WithdrawalTombstoned);

        var result = await SemanticAcceptanceAdmissionV1.AdmitAsync(fixture.Journal, disposition);

        var rejected = Assert.IsType<SemanticAcceptanceAdmissionResultV1.ProofRejected>(result);
        Assert.IsType<SemanticAcceptanceProofResultV1.Ineligible>(rejected.Proof);
        var read = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await fixture.Journal.ReadAsync(
            new ReadAuthorityRangeV1(fixture.Session, 0, long.MaxValue, 16, 65_536)));
        Assert.Equal(2, read.SnapshotThrough);
    }

    [Fact]
    public async Task Admission_MapsCasRaceAndAmbiguousAppendWithoutChangingPayloadIdentity()
    {
        var raceFixture = new Fixture();
        var disposition = await raceFixture.SeedDispositionAsync(SubmissionDispositionV1.SubmissionClaimed);
        var racing = new RacingJournal(raceFixture);
        var raced = await SemanticAcceptanceAdmissionV1.AdmitAsync(racing, disposition);
        Assert.Equal(3, Assert.IsType<SemanticAcceptanceAdmissionResultV1.RetryRequired>(raced).ObservedHead);

        var unknownFixture = new Fixture();
        var unknownDisposition = await unknownFixture.SeedDispositionAsync(SubmissionDispositionV1.SubmissionClaimed);
        var unknown = await SemanticAcceptanceAdmissionV1.AdmitAsync(new ThrowingAppendJournal(unknownFixture.Journal), unknownDisposition);
        var ambiguous = Assert.IsType<SemanticAcceptanceAdmissionResultV1.OutcomeUnknown>(unknown);
        Assert.True(ambiguous.FactId.IsValid);
        Assert.Equal("append-exception", ambiguous.SafeCode.ToString());
    }

    [Theory]
    [InlineData("id")]
    [InlineData("session")]
    [InlineData("scope")]
    [InlineData("owner")]
    [InlineData("schema")]
    [InlineData("payload")]
    [InlineData("hash")]
    [InlineData("correlation")]
    [InlineData("observed")]
    [InlineData("committed-head")]
    public async Task Admission_NeverTrustsAContradictoryAppendSuccess(string mutation)
    {
        var fixture = new Fixture();
        var disposition = await fixture.SeedDispositionAsync(SubmissionDispositionV1.SubmissionClaimed);

        var result = await SemanticAcceptanceAdmissionV1.AdmitAsync(
            new ContradictorySuccessJournal(fixture.Journal, mutation), disposition);

        Assert.Equal("unexpected-append-result",
            Assert.IsType<SemanticAcceptanceAdmissionResultV1.OutcomeUnknown>(result).SafeCode.ToString());
    }

    private sealed class Fixture
    {
        private readonly TenantId _tenant = TenantId.Create();
        internal Fixture()
        {
            var registry = new AuthorityPayloadAdmissionRegistryV1([
                new SessionAuthorityStampPayloadRegistrationV1(),
                new SubmissionDispositionChosenPayloadRegistrationV1(),
                new SemanticInputAcceptedPayloadRegistrationV1(),
            ]);
            Journal = new InMemoryAuthorityJournalV1(registry, () => new UtcInstant(123),
                new AuthorityJournalCapacityV1(16, 256, 4 * 1024 * 1024));
            Session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        }

        internal InMemoryAuthorityJournalV1 Journal { get; }
        internal SessionAuthorityStampV1 Session { get; }

        internal async Task<JournalPositionV1> SeedDispositionAsync(SubmissionDispositionV1 disposition)
        {
            var seedValue = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
            var seedPayload = SessionAuthorityStampV1Codec.Encode(seedValue);
            var seed = Proposal(new SessionAuthorityStampPayloadRegistrationV1(), OwnerSliceId.S1, seedPayload, OperationId.Create());
            var first = Assert.IsType<AppendAuthorityResultV1.Committed>(await Journal.AppendAsync(
                new AppendAuthorityBatchV1(Session, 0, [], [seed], 16_384))).Envelopes[0].Position;
            var operation = OperationId.Create();
            var value = new SubmissionDispositionChosenV1(operation, first,
                ExpectedAuthorityVectorV1.Create(Session, []), disposition);
            var payload = SubmissionDispositionChosenV1Codec.Encode(value);
            var proposal = Proposal(new SubmissionDispositionChosenPayloadRegistrationV1(), OwnerSliceId.S1, payload, operation);
            return Assert.IsType<AppendAuthorityResultV1.Committed>(await Journal.AppendAsync(
                new AppendAuthorityBatchV1(Session, 1, [], [proposal], 16_384))).Envelopes[0].Position;
        }

        internal async ValueTask AppendRaceFactAsync()
            => await AppendKnownFactAsync(expectedHead: 2);

        internal async ValueTask AppendKnownFactAsync(long expectedHead)
        {
            var value = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
            var payload = SessionAuthorityStampV1Codec.Encode(value);
            var proposal = Proposal(new SessionAuthorityStampPayloadRegistrationV1(), OwnerSliceId.S1, payload, OperationId.Create());
            var result = await Journal.AppendAsync(new AppendAuthorityBatchV1(Session, expectedHead, [], [proposal], 16_384));
            Assert.IsType<AppendAuthorityResultV1.Committed>(result);
        }

        private ProposedAuthorityFactV1 Proposal(AuthorityPayloadRegistrationV1 registration, OwnerSliceId owner, byte[] payload, OperationId operation) =>
            new(JournalFactId.Create(), null, owner, registration.Schema, payload,
                AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload),
                new CorrelationEnvelopeV1(_tenant, operationId: operation), new UtcInstant(100));
    }

    private sealed class RacingJournal(Fixture fixture) : IAuthorityJournalV1
    {
        private bool _raced;
        public async ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default)
        {
            if (!_raced) { _raced = true; await fixture.AppendRaceFactAsync(); }
            return await fixture.Journal.AppendAsync(request, cancellationToken);
        }
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request, CancellationToken cancellationToken = default) => fixture.Journal.ReadAsync(request, cancellationToken);
    }

    private sealed class ThrowingAppendJournal(IAuthorityJournalV1 inner) : IAuthorityJournalV1
    {
        public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default) => throw new IOException("fixture");
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request, CancellationToken cancellationToken = default) => inner.ReadAsync(request, cancellationToken);
    }

    private sealed class ContradictorySuccessJournal(IAuthorityJournalV1 inner, string mutation) : IAuthorityJournalV1
    {
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(request, cancellationToken);

        public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default)
        {
            var proposal = Assert.Single(request.Facts);
            var session = mutation == "session"
                ? new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create())
                : request.Session;
            var previous = mutation == "committed-head" ? request.ExpectedSessionHead + 1 : request.ExpectedSessionHead;
            var position = new JournalPositionV1(session, previous + 1);
            ThreadPositionV1? scope = mutation == "scope" ? new ThreadPositionV1(ThreadId.Create(), 1, 1) : null;
            var owner = mutation == "owner" ? OwnerSliceId.S1 : proposal.Owner;
            var schema = mutation == "schema" ? new SchemaReferenceV1(SchemaId.Create(), 1, 0) : proposal.PayloadSchema;
            var payload = mutation == "payload" ? new byte[] { 0x80 } : proposal.Payload.ToArray();
            var hash = mutation == "hash" ? Hash256.Compute([9]) : proposal.PayloadHash;
            var correlation = mutation == "correlation" ? new CorrelationEnvelopeV1(TenantId.Create()) : proposal.Correlation;
            var observed = mutation == "observed" ? new UtcInstant(proposal.ObservedAt.NanosecondsSinceUnixEpoch + 1) : proposal.ObservedAt;
            var envelope = new AuthorityFactEnvelopeV1(
                mutation == "id" ? JournalFactId.Create() : proposal.FactId,
                position, scope, owner, schema, payload, hash, correlation, observed, new UtcInstant(200),
                new IntegrityEnvelopeV1(1, 1, Hash256.Compute([1]), []));
            AppendAuthorityResultV1 result = mutation == "committed-head"
                ? new AppendAuthorityResultV1.Committed(previous, previous + 1, [envelope])
                : new AppendAuthorityResultV1.AlreadyCommitted([envelope]);
            return ValueTask.FromResult(result);
        }
    }
}
