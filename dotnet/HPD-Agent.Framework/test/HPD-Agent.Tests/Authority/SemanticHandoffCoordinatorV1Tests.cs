using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class SemanticHandoffCoordinatorV1Tests
{
    [Fact]
    public async Task Bind_commits_the_single_l1_through_l4_chain_and_retry_is_exact()
    {
        var fixture = new Fixture();
        var decision = await fixture.SeedDecisionAsync();

        var first = Assert.IsType<SemanticHandoffResultV1.Bound>(await fixture.BindAsync(decision));
        var retry = Assert.IsType<SemanticHandoffResultV1.Bound>(await fixture.BindAsync(decision));

        Assert.Equal(2, first.DecisionPosition.Sequence);
        Assert.Equal(3, first.ReservationPosition.Sequence);
        Assert.Equal(4, first.DispositionPosition.Sequence);
        Assert.Equal(5, first.AcceptancePosition.Sequence);
        Assert.Equal(6, first.BindingPosition.Sequence);
        Assert.Equal(first, retry);
        Assert.Equal(6, await fixture.HeadAsync());
    }

    [Fact]
    public async Task Bind_reconciles_each_lost_append_acknowledgement_without_duplicate_facts()
    {
        for (var append = 1; append <= 4; append++)
        {
            var fixture = new Fixture();
            var decision = await fixture.SeedDecisionAsync();
            var journal = new CommitThenThrowJournal(fixture.Journal, append);

            var result = Assert.IsType<SemanticHandoffResultV1.Bound>(await fixture.BindAsync(decision, journal));

            Assert.Equal(6, result.BindingPosition.Sequence);
            Assert.Equal(6, await fixture.HeadAsync());
        }
    }

    [Fact]
    public async Task Bind_fails_closed_for_changed_operation_and_preinvoke_cancellation()
    {
        var fixture = new Fixture();
        var decision = await fixture.SeedDecisionAsync();
        var changedOperation = OperationId.Create();
        var changed = await SemanticHandoffCoordinatorV1.BindAsync(fixture.Journal, decision, changedOperation,
            fixture.Authority, new CorrelationEnvelopeV1(TenantId.Create(), operationId: changedOperation), fixture.ObservedAt);
        Assert.Equal("decision-proof-invalid", Assert.IsType<SemanticHandoffResultV1.InvalidHistory>(changed).SafeCode.ToString());
        Assert.Equal(2, await fixture.HeadAsync());

        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await fixture.BindAsync(decision, cancellationToken: source.Token));
        Assert.Equal(2, await fixture.HeadAsync());
    }

    private sealed class Fixture
    {
        private readonly TenantId _tenant = TenantId.Create();
        internal Fixture()
        {
            Session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
            Operation = OperationId.Create();
            Authority = ExpectedAuthorityVectorV1.Create(Session, []);
            Correlation = new CorrelationEnvelopeV1(_tenant, operationId: Operation);
            ObservedAt = new UtcInstant(100);
            var decision = AuthorityPayloadRegistrationV1.CreateOwnerRegistration(
                new BoundedAscii("hpd.authority-payload-turn-decision-finalized.v1"), 1, 0, OwnerSliceId.S4, 64,
                static (payload, _) => AuthorityCanonicalCborV1.IsSingleCanonicalValue(payload));
            Journal = new InMemoryAuthorityJournalV1(new AuthorityPayloadAdmissionRegistryV1([
                new SessionAuthorityStampPayloadRegistrationV1(), decision,
                new SemanticReservationCreatedPayloadRegistrationV1(),
                new SubmissionDispositionChosenPayloadRegistrationV1(),
                new SemanticInputAcceptedPayloadRegistrationV1(),
                new SemanticAcceptanceBoundPayloadRegistrationV1(),
            ]), () => new UtcInstant(123), new AuthorityJournalCapacityV1(32, 512, 4 * 1024 * 1024));
            DecisionRegistration = decision;
        }

        internal InMemoryAuthorityJournalV1 Journal { get; }
        internal AuthorityPayloadRegistrationV1 DecisionRegistration { get; }
        internal SessionAuthorityStampV1 Session { get; }
        internal OperationId Operation { get; }
        internal ExpectedAuthorityVectorV1 Authority { get; }
        internal CorrelationEnvelopeV1 Correlation { get; }
        internal UtcInstant ObservedAt { get; }

        internal async Task<JournalPositionV1> SeedDecisionAsync()
        {
            var stamp = SessionAuthorityStampV1Codec.Encode(Session);
            await AppendAsync(0, Proposal(new SessionAuthorityStampPayloadRegistrationV1(), stamp, OperationId.Create()));
            var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
            writer.WriteUInt64(1);
            return (await AppendAsync(1, Proposal(DecisionRegistration, writer.Encode(), Operation))).Position;
        }

        internal ValueTask<SemanticHandoffResultV1> BindAsync(JournalPositionV1 decision,
            IAuthorityJournalV1? journal = null, CancellationToken cancellationToken = default) =>
            SemanticHandoffCoordinatorV1.BindAsync(journal ?? Journal, decision, Operation, Authority,
                Correlation, ObservedAt, cancellationToken);

        internal async Task<long> HeadAsync() => Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await Journal.ReadAsync(
            new ReadAuthorityRangeV1(Session, 0, long.MaxValue, 32, 65_536))).SnapshotThrough;

        private async Task<AuthorityFactEnvelopeV1> AppendAsync(long head, ProposedAuthorityFactV1 proposal) =>
            Assert.Single(Assert.IsType<AppendAuthorityResultV1.Committed>(await Journal.AppendAsync(
                new AppendAuthorityBatchV1(Session, head, [], [proposal], 16_384))).Envelopes);

        private ProposedAuthorityFactV1 Proposal(AuthorityPayloadRegistrationV1 registration, byte[] payload, OperationId operation) =>
            new(JournalFactId.Create(), null, registration.Owner, registration.Schema, payload,
                AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload),
                new CorrelationEnvelopeV1(_tenant, operationId: operation), ObservedAt);
    }

    private sealed class CommitThenThrowJournal(IAuthorityJournalV1 inner, int selectedAppend) : IAuthorityJournalV1
    {
        private int _append;
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,
            CancellationToken cancellationToken = default) => inner.ReadAsync(request, cancellationToken);
        public async ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.AppendAsync(request, cancellationToken);
            if (++_append == selectedAppend) throw new IOException("lost acknowledgement");
            return result;
        }
    }
}
