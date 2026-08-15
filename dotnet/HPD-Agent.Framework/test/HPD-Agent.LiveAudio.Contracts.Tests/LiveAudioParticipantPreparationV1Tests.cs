using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Tests;

public sealed class LiveAudioParticipantPreparationV1Tests
{
    [Fact]
    public async Task Prepares_in_canonical_request_order_without_start_surface()
    {
        var fixture = new Fixture(); var calls = new List<string>();
        var catalog = LiveAudioParticipantFactoryCatalogV1.CreateExplicit([
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
    public async Task Preparation_uses_compiled_dependency_order()
    {
        var fixture = new Fixture(); var calls = new List<string>();
        var resources = new Factory("zeta", OwnerSliceId.S2, calls);
        var provider = new Factory("alpha", OwnerSliceId.S5, calls,
            dependencies: [new BoundedAscii("zeta")]);
        var result = Assert.IsType<LiveAudioParticipantPreparationResultV1.Prepared>(
            await LiveAudioParticipantPreparationCoordinatorV1.PrepareAsync(fixture.Request(
                fixture.Spec("alpha", OwnerSliceId.S5), fixture.Spec("zeta", OwnerSliceId.S2)),
                LiveAudioParticipantFactoryCatalogV1.CreateExplicit([provider, resources])));
        Assert.Equal(["prepare:zeta", "prepare:alpha"], calls);
        foreach (var participant in result.Participants) await participant.DisposeAsync();
    }

    [Fact]
    public async Task Missing_required_factory_fails_before_any_preparation()
    {
        var fixture = new Fixture(); var calls = new List<string>();
        var catalog = LiveAudioParticipantFactoryCatalogV1.CreateExplicit([
            new Factory("alpha", OwnerSliceId.S2, calls), new Factory("beta", OwnerSliceId.S3, calls)]);
        var result = Assert.IsType<LiveAudioParticipantPreparationResultV1.Unavailable>(
            await LiveAudioParticipantPreparationCoordinatorV1.PrepareAsync(fixture.Request(
                fixture.Spec("alpha", OwnerSliceId.S2), fixture.Spec("beta", OwnerSliceId.S3),
                fixture.Spec("zeta", OwnerSliceId.S11)), catalog));
        Assert.Equal("zeta", result.FactoryKey.ToString());
        Assert.Empty(calls);
    }

    [Fact]
    public async Task Failure_and_cancellation_unwind_without_claiming_readiness()
    {
        var fixture = new Fixture(); var failedCalls = new List<string>();
        var failedCatalog = LiveAudioParticipantFactoryCatalogV1.CreateExplicit([
            new Factory("alpha", OwnerSliceId.S2, failedCalls), new Factory("beta", OwnerSliceId.S3, failedCalls),
            new Factory("gamma", OwnerSliceId.S4, failedCalls, fail: true)]);
        Assert.IsType<LiveAudioParticipantPreparationResultV1.Failed>(
            await LiveAudioParticipantPreparationCoordinatorV1.PrepareAsync(fixture.Request(
                fixture.Spec("alpha", OwnerSliceId.S2), fixture.Spec("beta", OwnerSliceId.S3),
                fixture.Spec("gamma", OwnerSliceId.S4)), failedCatalog));
        Assert.Equal(["prepare:alpha", "prepare:beta", "prepare:gamma", "dispose:beta", "dispose:alpha"], failedCalls);

        var cancelledCalls = new List<string>(); using var cancellation = new CancellationTokenSource();
        var cancelledCatalog = LiveAudioParticipantFactoryCatalogV1.CreateExplicit([
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
        var catalog = LiveAudioParticipantFactoryCatalogV1.CreateExplicit([
            new Factory("alpha", OwnerSliceId.S2, calls), new Factory("beta", OwnerSliceId.S3, calls, refuse: true)]);
        var result = Assert.IsType<LiveAudioParticipantPreparationResultV1.Prepared>(
            await LiveAudioParticipantPreparationCoordinatorV1.PrepareAsync(fixture.Request(
                fixture.Spec("alpha", OwnerSliceId.S2), fixture.Spec("beta", OwnerSliceId.S3, required: false),
                fixture.Spec("zeta", OwnerSliceId.S11, required: false)), catalog));
        Assert.Single(result.Participants);
        Assert.Equal(["beta", "zeta"], result.SkippedOptionalFactories.Select(item => item.ToString()));
        Assert.True(result.EffectiveFingerprint.TryWriteBytes(new byte[32]));
    }

    [Fact]
    public async Task Optional_refusal_closes_dependent_subgraph()
    {
        var fixture = new Fixture(); var calls = new List<string>();
        var catalog = LiveAudioParticipantFactoryCatalogV1.CreateExplicit([
            new Factory("alpha", OwnerSliceId.S2, calls, refuse: true),
            new Factory("beta", OwnerSliceId.S3, calls, dependencies: [new BoundedAscii("alpha")])]);
        var failed = Assert.IsType<LiveAudioParticipantPreparationResultV1.Failed>(
            await LiveAudioParticipantPreparationCoordinatorV1.PrepareAsync(fixture.Request(
                fixture.Spec("alpha", OwnerSliceId.S2, required: false), fixture.Spec("beta", OwnerSliceId.S3)), catalog));
        Assert.Equal("participant-dependency-unavailable", failed.SafeCode.ToString());
        Assert.Equal(["prepare:alpha"], calls);
    }

    [Fact]
    public async Task Ignored_prepare_cancellation_is_bounded_and_unknown()
    {
        var fixture = new Fixture(); var calls = new List<string>();
        var result = await LiveAudioParticipantPreparationCoordinatorV1.PrepareAsync(
            fixture.Request(fixture.Spec("alpha", OwnerSliceId.S2)),
            LiveAudioParticipantFactoryCatalogV1.CreateExplicit([new HangingPrepareFactory("alpha", OwnerSliceId.S2, calls)]));
        Assert.IsType<LiveAudioParticipantPreparationResultV1.OutcomeUnknown>(result);
        Assert.Equal(["prepare:alpha"], calls);
    }

    [Fact]
    public async Task Hanging_unwind_is_bounded_and_later_handles_still_cleanup()
    {
        var fixture = new Fixture(); var calls = new List<string>();
        var catalog = LiveAudioParticipantFactoryCatalogV1.CreateExplicit([
            new Factory("alpha", OwnerSliceId.S2, calls), new HangingDisposeFactory("beta", OwnerSliceId.S3, calls),
            new Factory("gamma", OwnerSliceId.S4, calls, fail: true)]);
        var result = await LiveAudioParticipantPreparationCoordinatorV1.PrepareAsync(fixture.Request(
            fixture.Spec("alpha", OwnerSliceId.S2), fixture.Spec("beta", OwnerSliceId.S3), fixture.Spec("gamma", OwnerSliceId.S4)), catalog);
        Assert.IsType<LiveAudioParticipantPreparationResultV1.OutcomeUnknown>(result);
        Assert.Equal(["prepare:alpha", "prepare:beta", "prepare:gamma", "dispose:beta", "dispose:alpha"], calls);
    }

    [Fact]
    public async Task Duplicate_participant_identity_unwinds_all_handles()
    {
        var fixture = new Fixture(); var calls = new List<string>(); var id = ParticipantId.Create();
        var catalog = LiveAudioParticipantFactoryCatalogV1.CreateExplicit([
            new FixedIdFactory("alpha", OwnerSliceId.S2, calls, id), new FixedIdFactory("beta", OwnerSliceId.S3, calls, id)]);
        var failed = Assert.IsType<LiveAudioParticipantPreparationResultV1.Failed>(
            await LiveAudioParticipantPreparationCoordinatorV1.PrepareAsync(fixture.Request(
                fixture.Spec("alpha", OwnerSliceId.S2), fixture.Spec("beta", OwnerSliceId.S3)), catalog));
        Assert.Equal("participant-identity-duplicate", failed.SafeCode.ToString());
        Assert.Equal(["prepare:alpha", "prepare:beta", "dispose:beta", "dispose:alpha"], calls);
    }

    [Fact]
    public async Task Unwind_failure_is_outcome_unknown()
    {
        var fixture = new Fixture(); var calls = new List<string>();
        var catalog = LiveAudioParticipantFactoryCatalogV1.CreateExplicit([
            new Factory("alpha", OwnerSliceId.S2, calls, disposeFails: true),
            new Factory("beta", OwnerSliceId.S3, calls, fail: true)]);
        var result = await LiveAudioParticipantPreparationCoordinatorV1.PrepareAsync(fixture.Request(
            fixture.Spec("alpha", OwnerSliceId.S2), fixture.Spec("beta", OwnerSliceId.S3)), catalog);
        Assert.IsType<LiveAudioParticipantPreparationResultV1.OutcomeUnknown>(result);
    }

    [Fact]
    public void Catalog_rejects_empty_duplicate_invalid_and_too_many_factories()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveAudioParticipantFactoryCatalogV1.CreateExplicit([]));
        Assert.Throws<ArgumentException>(() => LiveAudioParticipantFactoryCatalogV1.CreateExplicit([
            new Factory("alpha", OwnerSliceId.S2, []), new Factory("alpha", OwnerSliceId.S3, [])]));
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveAudioParticipantFactoryCatalogV1.CreateExplicit(
            Enumerable.Range(0, 65).Select(index => new Factory($"factory-{index:D2}", OwnerSliceId.S2, []))));
    }

    private sealed class Factory(string key, OwnerSliceId owner, List<string> calls, bool fail = false,
        CancellationTokenSource? cancel = null, bool refuse = false, bool disposeFails = false,
        BoundedAscii[]? dependencies = null) : ILiveAudioParticipantFactoryV1
    {
        public LiveAudioParticipantDescriptorV1 Descriptor { get; } = DescriptorFor(key, owner, dependencies ?? []);
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

    private static LiveAudioParticipantDescriptorV1 DescriptorFor(string key, OwnerSliceId owner, BoundedAscii[] dependencies,
        long prepareNanoseconds = 5_000_000_000, long terminateNanoseconds = 5_000_000_000) => new(
        new BoundedAscii(key), owner, AxisFor(owner), dependencies, [new CapacityDimensionId(1)],
        new DurationNs(prepareNanoseconds), new DurationNs(30_000_000_000), new DurationNs(terminateNanoseconds));

    private static AuthorityAxisId AxisFor(OwnerSliceId owner) => owner switch
    {
        OwnerSliceId.S2 => AuthorityAxisId.Graph,
        OwnerSliceId.S3 => AuthorityAxisId.Activity,
        OwnerSliceId.S4 => AuthorityAxisId.Turn,
        OwnerSliceId.S5 => AuthorityAxisId.Provider,
        OwnerSliceId.S6 => AuthorityAxisId.Output,
        OwnerSliceId.S7 => AuthorityAxisId.Tool,
        OwnerSliceId.S8 => AuthorityAxisId.Route,
        OwnerSliceId.S9 => AuthorityAxisId.Privacy,
        OwnerSliceId.S11 => AuthorityAxisId.Transport,
        _ => throw new ArgumentOutOfRangeException(nameof(owner)),
    };

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

    private sealed class HangingPrepareFactory(string key, OwnerSliceId owner, List<string> calls) : ILiveAudioParticipantFactoryV1
    {
        private readonly TaskCompletionSource<LiveAudioParticipantFactoryResultV1> _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LiveAudioParticipantDescriptorV1 Descriptor { get; } = DescriptorFor(key, owner, [], prepareNanoseconds: 5_000_000);
        public ValueTask<LiveAudioParticipantFactoryResultV1> PrepareAsync(LiveAudioParticipantPreparationContextV1 context,
            CancellationToken cancellationToken = default) { calls.Add($"prepare:{key}"); return new(_pending.Task); }
    }

    private sealed class HangingDisposeFactory(string key, OwnerSliceId owner, List<string> calls) : ILiveAudioParticipantFactoryV1
    {
        private readonly TaskCompletionSource _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LiveAudioParticipantDescriptorV1 Descriptor { get; } = DescriptorFor(key, owner, [], terminateNanoseconds: 5_000_000);
        public ValueTask<LiveAudioParticipantFactoryResultV1> PrepareAsync(LiveAudioParticipantPreparationContextV1 context,
            CancellationToken cancellationToken = default)
        { calls.Add($"prepare:{key}"); return ValueTask.FromResult<LiveAudioParticipantFactoryResultV1>(
            new LiveAudioParticipantFactoryResultV1.Prepared(new HangingHandle(key, owner, calls, _pending.Task))); }
    }

    private sealed class HangingHandle(string key, OwnerSliceId owner, List<string> calls, Task pending) : ILiveAudioPreparedParticipantV1
    {
        public ParticipantId ParticipantId { get; } = ParticipantId.Create();
        public BoundedAscii FactoryKey { get; } = new(key);
        public OwnerSliceId Owner { get; } = owner;
        public ValueTask DisposeAsync() { calls.Add($"dispose:{key}"); return new ValueTask(pending); }
    }

    private sealed class FixedIdFactory(string key, OwnerSliceId owner, List<string> calls, ParticipantId id) : ILiveAudioParticipantFactoryV1
    {
        public LiveAudioParticipantDescriptorV1 Descriptor { get; } = DescriptorFor(key, owner, []);
        public ValueTask<LiveAudioParticipantFactoryResultV1> PrepareAsync(LiveAudioParticipantPreparationContextV1 context,
            CancellationToken cancellationToken = default)
        { calls.Add($"prepare:{key}"); return ValueTask.FromResult<LiveAudioParticipantFactoryResultV1>(
            new LiveAudioParticipantFactoryResultV1.Prepared(new FixedHandle(key, owner, calls, id))); }
    }

    private sealed class FixedHandle(string key, OwnerSliceId owner, List<string> calls, ParticipantId id) : ILiveAudioPreparedParticipantV1
    {
        public ParticipantId ParticipantId { get; } = id;
        public BoundedAscii FactoryKey { get; } = new(key);
        public OwnerSliceId Owner { get; } = owner;
        public ValueTask DisposeAsync() { calls.Add($"dispose:{key}"); return ValueTask.CompletedTask; }
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
            _authority = ExpectedAuthorityVectorV1.Create(_session, AllAxes());
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
        private static AuthorityAxisValueV1[] AllAxes() =>
        [
            new AuthorityAxisValueV1.Graph(GraphGenerationId.Create()),
            new AuthorityAxisValueV1.Activity(ActivityGenerationId.Create()),
            new AuthorityAxisValueV1.Turn(TurnGenerationId.Create()),
            new AuthorityAxisValueV1.Provider(ProviderGenerationId.Create()),
            new AuthorityAxisValueV1.Output(OutputGenerationId.Create()),
            new AuthorityAxisValueV1.Tool(ToolGenerationId.Create()),
            new AuthorityAxisValueV1.Route(RouteGenerationId.Create()),
            new AuthorityAxisValueV1.Privacy(PrivacyGenerationId.Create()),
            new AuthorityAxisValueV1.Transport(TransportGenerationId.Create()),
        ];
    }
}
