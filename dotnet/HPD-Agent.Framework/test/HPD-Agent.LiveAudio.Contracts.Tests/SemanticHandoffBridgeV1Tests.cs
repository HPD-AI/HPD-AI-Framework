using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class SemanticHandoffBridgeV1Tests
{
    [Fact]
    public async Task Bridge_authenticates_l0_commits_l1_through_l5_and_retries_exactly()
    {
        var fixture = new Fixture();
        var decision = await fixture.SeedDecisionAsync();

        var first = Assert.IsType<SemanticHandoffBridgeResultV1.Admitted>(
            await SemanticHandoffBridgeV1.AdmitAsync(fixture.Journal, decision));
        var retry = Assert.IsType<SemanticHandoffBridgeResultV1.Admitted>(
            await SemanticHandoffBridgeV1.AdmitAsync(fixture.Journal, decision));

        Assert.Equal(5, first.AcceptancePosition.Sequence);
        Assert.Equal(6, first.BindingPosition.Sequence);
        Assert.Equal(7, first.ReceiptPosition.Sequence);
        Assert.Equal(first, retry);
        Assert.Equal(7, await fixture.HeadAsync());
    }

    [Fact]
    public async Task Bridge_reconciles_a_lost_l5_acknowledgement_and_rejects_mutated_l0()
    {
        var fixture = new Fixture();
        var decision = await fixture.SeedDecisionAsync();
        var admitted = Assert.IsType<SemanticHandoffBridgeResultV1.Admitted>(
            await SemanticHandoffBridgeV1.AdmitAsync(new CommitThenThrowJournal(fixture.Journal, 5), decision));
        Assert.Equal(7, admitted.ReceiptPosition.Sequence);
        Assert.Equal(7, await fixture.HeadAsync());

        var invalidFixture = new Fixture();
        var invalid = await invalidFixture.SeedDecisionAsync(mutateBodyOperation: true);
        var rejected = Assert.IsType<SemanticHandoffBridgeResultV1.InvalidHistory>(
            await SemanticHandoffBridgeV1.AdmitAsync(invalidFixture.Journal, invalid));
        Assert.Equal("turn-decision-proof-invalid", rejected.SafeCode.ToString());
        Assert.Equal(2, await invalidFixture.HeadAsync());
    }

    private sealed class Fixture
    {
        private readonly TenantId _tenant = TenantId.Create();
        internal Fixture()
        {
            Session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
            Operation = OperationId.Create();
            Authority = ExpectedAuthorityVectorV1.Create(Session, []);
            Journal = new InMemoryAuthorityJournalV1(new AuthorityPayloadAdmissionRegistryV1([
                new SessionAuthorityStampPayloadRegistrationV1(),
                TurnGenerationAuthorityPayloadRegistrationsV1.TurnDecisionFinalized,
                new SemanticReservationCreatedPayloadRegistrationV1(),
                new SubmissionDispositionChosenPayloadRegistrationV1(),
                new SemanticInputAcceptedPayloadRegistrationV1(),
                new SemanticAcceptanceBoundPayloadRegistrationV1(),
                new SemanticReceiptAdmittedPayloadRegistrationV1(),
            ]), () => new UtcInstant(123), new AuthorityJournalCapacityV1(32, 512, 4 * 1024 * 1024));
        }

        internal InMemoryAuthorityJournalV1 Journal { get; }
        internal SessionAuthorityStampV1 Session { get; }
        internal OperationId Operation { get; }
        internal ExpectedAuthorityVectorV1 Authority { get; }

        internal async Task<JournalPositionV1> SeedDecisionAsync(bool mutateBodyOperation = false)
        {
            var stamp = SessionAuthorityStampV1Codec.Encode(Session);
            var seed = await AppendAsync(0, Proposal(new SessionAuthorityStampPayloadRegistrationV1(), stamp, OperationId.Create()));
            var body = new TurnDecisionFinalizedV1(mutateBodyOperation ? OperationId.Create() : Operation,
                seed.Position, Authority, 1);
            var outer = new TurnDecisionFinalizedOuterV1(Session, Authority, TurnGenerationRecordCodecsV1.Encode(body));
            return (await AppendAsync(1, TypedOwnerEvidenceAdaptersV1.TurnFinalized(outer,
                JournalFactId.Create(), null, new CorrelationEnvelopeV1(_tenant, operationId: Operation), new UtcInstant(100)))).Position;
        }

        internal async Task<long> HeadAsync() => Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await Journal.ReadAsync(
            new ReadAuthorityRangeV1(Session, 0, long.MaxValue, 32, 65_536))).SnapshotThrough;

        private async Task<AuthorityFactEnvelopeV1> AppendAsync(long head, ProposedAuthorityFactV1 proposal) =>
            Assert.Single(Assert.IsType<AppendAuthorityResultV1.Committed>(await Journal.AppendAsync(
                new AppendAuthorityBatchV1(Session, head, [], [proposal], 16_384))).Envelopes);

        private ProposedAuthorityFactV1 Proposal(AuthorityPayloadRegistrationV1 registration, byte[] payload, OperationId operation) =>
            new(JournalFactId.Create(), null, registration.Owner, registration.Schema, payload,
                AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload),
                new CorrelationEnvelopeV1(_tenant, operationId: operation), new UtcInstant(100));
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
            if (++_append == selectedAppend) throw new IOException("lost receipt acknowledgement");
            return result;
        }
    }
}
