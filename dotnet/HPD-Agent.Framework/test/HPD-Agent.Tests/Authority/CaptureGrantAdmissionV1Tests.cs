using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class CaptureGrantAdmissionV1Tests
{
    [Fact]
    public async Task Grant_is_two_ordered_facts_and_retry_is_idempotent()
    {
        var fixture = new Fixture();
        var granted = Assert.IsType<CaptureGrantAdmissionResultV1.Granted>(await fixture.AuthorizeAsync());
        var retry = Assert.IsType<CaptureGrantAdmissionResultV1.AlreadyGranted>(await fixture.AuthorizeAsync());
        Assert.Equal(granted.Proof, retry.Proof);
        Assert.Equal(2, granted.Proof.GrantedAt.Sequence);
        var facts = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await fixture.Journal.ReadAsync(
            new ReadAuthorityRangeV1(fixture.Session, 0, long.MaxValue, 8, 4096)));
        Assert.Equal(2, facts.Facts.Count);
        Assert.Equal(OwnerSliceId.S9, facts.Facts[0].Owner);
        Assert.Equal(OwnerSliceId.S9, facts.Facts[1].Owner);
    }

    [Fact]
    public async Task Current_reader_returns_active_then_expired_without_appending_truth()
    {
        var fixture = new Fixture(); await fixture.AuthorizeAsync();
        var active = Assert.IsType<CaptureGrantReadResultV1.Active>(await fixture.ReadAsync(899));
        Assert.Equal(CaptureGrantStateV1.Active, active.Proof.State);
        Assert.IsType<CaptureGrantReadResultV1.Inactive>(await fixture.ReadAsync(900));
        var facts = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await fixture.Journal.ReadAsync(
            new ReadAuthorityRangeV1(fixture.Session, 0, long.MaxValue, 8, 4096)));
        Assert.Equal(2, facts.Facts.Count);
    }

    [Fact]
    public async Task Expired_at_admission_is_durably_rejected_and_never_active()
    {
        var fixture = new Fixture(expiry: 100);
        Assert.IsType<CaptureGrantAdmissionResultV1.Rejected>(await fixture.AuthorizeAsync(observedAt: 100));
        var inactive = Assert.IsType<CaptureGrantReadResultV1.Inactive>(await fixture.ReadAsync(100));
        Assert.Equal(CaptureGrantStateV1.Expired, inactive.State);
    }

    [Fact]
    public async Task Same_operation_with_changed_terms_is_contradictory()
    {
        var fixture = new Fixture(); await fixture.AuthorizeAsync();
        var changed = fixture.Command(Hash(9));
        Assert.IsType<CaptureGrantAdmissionResultV1.ContradictoryDuplicate>(await fixture.AuthorizeAsync(changed));
    }

    [Fact]
    public async Task Same_operation_with_changed_observation_metadata_joins_original()
    {
        var fixture = new Fixture(); await fixture.AuthorizeAsync(observedAt: 10);
        Assert.IsType<CaptureGrantAdmissionResultV1.AlreadyGranted>(await fixture.AuthorizeAsync(observedAt: 11));
    }

    [Fact]
    public async Task Unknown_grant_reports_observed_frontier_not_nonexistence()
    {
        var fixture = new Fixture();
        var result = Assert.IsType<CaptureGrantReadResultV1.NotObserved>(await CaptureGrantAdmissionV1.ReadCurrentAsync(
            fixture.Journal, fixture.Session, CaptureGrantId.Create(), new UtcInstant(1)));
        Assert.Equal(0, result.SnapshotThrough);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Crash_at_result_append_reconciles_without_duplicate_grant(bool commitFirst)
    {
        var fixture = new Fixture();
        var faulting = new FaultingAppendJournal(fixture.Journal, 2, commitFirst);
        Assert.IsType<CaptureGrantAdmissionResultV1.OutcomeUnknown>(await fixture.AuthorizeAsync(journal: faulting));
        var recovered = await fixture.AuthorizeAsync();
        Assert.True(recovered is CaptureGrantAdmissionResultV1.Granted or CaptureGrantAdmissionResultV1.AlreadyGranted);
        Assert.IsType<CaptureGrantReadResultV1.Active>(await fixture.ReadAsync(20));
        var facts = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await fixture.Journal.ReadAsync(
            new ReadAuthorityRangeV1(fixture.Session, 0, long.MaxValue, 8, 4096)));
        Assert.Equal(2, facts.Facts.Count);
    }

    private sealed class Fixture
    {
        private readonly TenantId _tenant = TenantId.Create();
        private readonly long _expiry;
        internal Fixture(long expiry = 900)
        {
            _expiry = expiry; Session = new(RuntimeGenerationId.Create(), LiveSessionId.Create());
            Authority = ExpectedAuthorityVectorV1.Create(Session, []); Operation = OperationId.Create();
            GrantId = CaptureGrantId.Create(); AuthorizationId = AuthorizationId.Create();
            Journal = new InMemoryAuthorityJournalV1(new AuthorityPayloadAdmissionRegistryV1([
                new CaptureAuthorizationPayloadRegistrationV1(), new CaptureGrantCommittedPayloadRegistrationV1()]),
                () => new UtcInstant(1), new AuthorityJournalCapacityV1(8, 32, 1_048_576));
        }
        internal SessionAuthorityStampV1 Session { get; }
        internal ExpectedAuthorityVectorV1 Authority { get; }
        internal OperationId Operation { get; }
        internal CaptureGrantId GrantId { get; }
        internal AuthorizationId AuthorizationId { get; }
        internal InMemoryAuthorityJournalV1 Journal { get; }
        internal CaptureAuthorizationCommandV1 Command(Hash256? scope = null) => new(Session, Authority,
            new CaptureAuthorizationBodyV1(Operation, GrantId, AuthorizationId, scope ?? Hash(1), Hash(2), new UtcInstant(_expiry)));
        internal ValueTask<CaptureGrantAdmissionResultV1> AuthorizeAsync(CaptureAuthorizationCommandV1? command = null,
            long observedAt = 10, IAuthorityJournalV1? journal = null) =>
            CaptureGrantAdmissionV1.AuthorizeAsync(journal ?? Journal, command ?? Command(),
                new CorrelationEnvelopeV1(_tenant, operationId: Operation), new UtcInstant(observedAt));
        internal ValueTask<CaptureGrantReadResultV1> ReadAsync(long observedAt) =>
            CaptureGrantAdmissionV1.ReadCurrentAsync(Journal, Session, GrantId, new UtcInstant(observedAt));
    }


    private sealed class FaultingAppendJournal(IAuthorityJournalV1 inner, int faultAppend, bool commitFirst) : IAuthorityJournalV1
    {
        private int _appendCount;
        public async ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _appendCount) != faultAppend) return await inner.AppendAsync(request, cancellationToken);
            if (commitFirst) await inner.AppendAsync(request, cancellationToken);
            throw new IOException("fixture");
        }
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(request, cancellationToken);
    }

    private static Hash256 Hash(byte value) => Hash256.FromBytes(Enumerable.Repeat(value, 32).ToArray());
}
