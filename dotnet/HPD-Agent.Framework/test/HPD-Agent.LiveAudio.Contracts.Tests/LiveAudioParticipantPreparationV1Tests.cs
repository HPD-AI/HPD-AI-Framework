using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Tests;

public sealed class LiveAudioParticipantPreparationV1Tests
{
    [Fact]
    public async Task Prepares_in_canonical_request_order_without_start_surface()
    {
        var fixture = new Fixture(); var calls = new List<string>();
        var catalog = new LiveAudioParticipantFactoryCatalogV1([
            new Factory("zeta", OwnerSliceId.S11, calls), new Factory("alpha", OwnerSliceId.S2, calls)]);
        var result = Assert.IsType<LiveAudioParticipantPreparationResultV1.Prepared>(
            await LiveAudioParticipantPreparationCoordinatorV1.PrepareAsync(fixture.Request(
                fixture.Spec("zeta", OwnerSliceId.S11), fixture.Spec("alpha", OwnerSliceId.S2)), catalog));
        Assert.Equal(["prepare:alpha", "prepare:zeta"], calls);
        Assert.Equal(2, result.Participants.Count);
        Assert.Empty(result.SkippedOptionalFactories);
        Assert.DoesNotContain(typeof(ILiveAudioPreparedParticipantV1).GetMethods(), method => method.Name.Contains("Start", StringComparison.Ordinal));
        foreach (var participant in result.Participants) await participant.DisposeAsync();
    }

    [Fact]
    public async Task Missing_factory_unwinds_prepared_handles_in_reverse_order()
    {
        var fixture = new Fixture(); var calls = new List<string>();
        var catalog = new LiveAudioParticipantFactoryCatalogV1([
            new Factory("alpha", OwnerSliceId.S2, calls), new Factory("beta", OwnerSliceId.S3, calls)]);
        var result = Assert.IsType<LiveAudioParticipantPreparationResultV1.Unavailable>(
            await LiveAudioParticipantPreparationCoordinatorV1.PrepareAsync(fixture.Request(
                fixture.Spec("alpha", OwnerSliceId.S2), fixture.Spec("beta", OwnerSliceId.S3),
                fixture.Spec("zeta", OwnerSliceId.S11)), catalog));
        Assert.Equal("zeta", result.FactoryKey.ToString());
        Assert.Equal(["prepare:alpha", "prepare:beta", "dispose:beta", "dispose:alpha"], calls);
    }

    [Fact]
    public async Task Failure_and_cancellation_unwind_without_claiming_readiness()
    {
        var fixture = new Fixture(); var failedCalls = new List<string>();
        var failedCatalog = new LiveAudioParticipantFactoryCatalogV1([
            new Factory("alpha", OwnerSliceId.S2, failedCalls), new Factory("beta", OwnerSliceId.S3, failedCalls, fail: true)]);
        Assert.IsType<LiveAudioParticipantPreparationResultV1.Failed>(
            await LiveAudioParticipantPreparationCoordinatorV1.PrepareAsync(fixture.Request(
                fixture.Spec("alpha", OwnerSliceId.S2), fixture.Spec("beta", OwnerSliceId.S3)), failedCatalog));
        Assert.Equal(["prepare:alpha", "prepare:beta", "dispose:alpha"], failedCalls);

        var cancelledCalls = new List<string>(); using var cancellation = new CancellationTokenSource();
        var cancelledCatalog = new LiveAudioParticipantFactoryCatalogV1([
            new Factory("alpha", OwnerSliceId.S2, cancelledCalls),
            new Factory("beta", OwnerSliceId.S3, cancelledCalls, cancel: cancellation)]);
        Assert.IsType<LiveAudioParticipantPreparationResultV1.Cancelled>(
            await LiveAudioParticipantPreparationCoordinatorV1.PrepareAsync(fixture.Request(
                fixture.Spec("alpha", OwnerSliceId.S2), fixture.Spec("beta", OwnerSliceId.S3)), cancelledCatalog, cancellation.Token));
        Assert.Equal(["prepare:alpha", "prepare:beta", "dispose:alpha"], cancelledCalls);
    }

    [Fact]
    public async Task Optional_refusal_or_absence_is_explicitly_skipped()
    {
        var fixture = new Fixture(); var calls = new List<string>();
        var catalog = new LiveAudioParticipantFactoryCatalogV1([
            new Factory("alpha", OwnerSliceId.S2, calls), new Factory("beta", OwnerSliceId.S3, calls, refuse: true)]);
        var result = Assert.IsType<LiveAudioParticipantPreparationResultV1.Prepared>(
            await LiveAudioParticipantPreparationCoordinatorV1.PrepareAsync(fixture.Request(
                fixture.Spec("alpha", OwnerSliceId.S2), fixture.Spec("beta", OwnerSliceId.S3, required: false),
                fixture.Spec("zeta", OwnerSliceId.S11, required: false)), catalog));
        Assert.Single(result.Participants);
        Assert.Equal(["beta", "zeta"], result.SkippedOptionalFactories.Select(item => item.ToString()));
    }

    [Fact]
    public async Task Unwind_failure_is_outcome_unknown()
    {
        var fixture = new Fixture(); var calls = new List<string>();
        var catalog = new LiveAudioParticipantFactoryCatalogV1([
            new Factory("alpha", OwnerSliceId.S2, calls, disposeFails: true)]);
        var result = await LiveAudioParticipantPreparationCoordinatorV1.PrepareAsync(fixture.Request(
            fixture.Spec("alpha", OwnerSliceId.S2), fixture.Spec("zeta", OwnerSliceId.S11)), catalog);
        Assert.IsType<LiveAudioParticipantPreparationResultV1.OutcomeUnknown>(result);
    }

    [Fact]
    public void Catalog_rejects_empty_duplicate_invalid_and_too_many_factories()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveAudioParticipantFactoryCatalogV1([]));
        Assert.Throws<ArgumentException>(() => new LiveAudioParticipantFactoryCatalogV1([
            new Factory("alpha", OwnerSliceId.S2, []), new Factory("alpha", OwnerSliceId.S3, [])]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveAudioParticipantFactoryCatalogV1(
            Enumerable.Range(0, 65).Select(index => new Factory($"factory-{index:D2}", OwnerSliceId.S2, []))));
    }

    private sealed class Factory(string key, OwnerSliceId owner, List<string> calls, bool fail = false,
        CancellationTokenSource? cancel = null, bool refuse = false, bool disposeFails = false) : ILiveAudioParticipantFactoryV1
    {
        public BoundedAscii FactoryKey { get; } = new(key);
        public OwnerSliceId Owner { get; } = owner;
        public ValueTask<LiveAudioParticipantFactoryResultV1> PrepareAsync(
            LiveAudioParticipantPreparationContextV1 context, CancellationToken cancellationToken = default)
        {
            calls.Add($"prepare:{key}");
            if (fail) throw new InvalidOperationException("fixture failure");
            if (cancel is not null) { cancel.Cancel(); cancellationToken.ThrowIfCancellationRequested(); }
            if (refuse) return ValueTask.FromResult<LiveAudioParticipantFactoryResultV1>(
                new LiveAudioParticipantFactoryResultV1.Refused(new BoundedAscii("fixture-refused")));
            return ValueTask.FromResult<LiveAudioParticipantFactoryResultV1>(
                new LiveAudioParticipantFactoryResultV1.Prepared(new Prepared(key, owner, calls, disposeFails)));
        }
    }

    private sealed class Prepared(string key, OwnerSliceId owner, List<string> calls, bool disposeFails) : ILiveAudioPreparedParticipantV1
    {
        public ParticipantId ParticipantId { get; } = ParticipantId.Create();
        public BoundedAscii FactoryKey { get; } = new(key);
        public OwnerSliceId Owner { get; } = owner;
        public ValueTask DisposeAsync()
        {
            calls.Add($"dispose:{key}");
            return disposeFails ? ValueTask.FromException(new InvalidOperationException("fixture dispose failure")) : ValueTask.CompletedTask;
        }
    }

    private sealed class Fixture
    {
        private readonly SessionAuthorityStampV1 _session = new(RuntimeGenerationId.Create(), LiveSessionId.Create());
        private readonly ExpectedAuthorityVectorV1 _authority;
        private readonly CapacityGrantSnapshotV1 _capacity;
        private readonly CaptureGrantProofV1 _capture;
        private readonly OperationId _operation = OperationId.Create();
        private readonly TenantId _tenant = TenantId.Create();
        private readonly ClockDomainId _clock = ClockDomainId.Create();
        private readonly BootId _boot = BootId.Create();

        internal Fixture()
        {
            _authority = ExpectedAuthorityVectorV1.Create(_session, []);
            var position = new JournalPositionV1(_session, 1);
            _capacity = new CapacityGrantSnapshotV1(CapacityGrantId.Create(), _operation, _authority, position, position,
                new CapacityGrantExpiryV1.NoExpiry(), CapacityGrantStateV1.Reserved, [null!]);
            _capture = new CaptureGrantProofV1(CaptureGrantId.Create(), AuthorizationId.Create(), position, _authority,
                Hash(1), Hash(2), CaptureGrantStateV1.Active, new UtcInstant(1000));
        }

        internal LiveAudioParticipantSpecV1 Spec(string key, OwnerSliceId owner, bool required = true) =>
            new(new BoundedAscii(key), owner, required, Hash((byte)owner));

        internal LiveAudioSessionStartRequestV1 Request(params LiveAudioParticipantSpecV1[] participants) => new(
            _operation, null, new CorrelationEnvelopeV1(_tenant, operationId: _operation), LiveAudioPlanId.Create(),
            _authority, _capacity, _capture, LiveAudioConcurrencyModeV1.Exclusive,
            new MonotonicStampV1(_clock, _boot, 100), participants);

        private static Hash256 Hash(byte value)
        {
            Span<byte> bytes = stackalloc byte[32]; bytes.Fill(value);
            if (!Hash256.TryCreate(bytes, out var result)) throw new InvalidOperationException();
            return result;
        }
    }
}
