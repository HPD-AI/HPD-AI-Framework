using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Tests;

public sealed class LiveAudioParticipantPlanV1Tests
{
    [Fact]
    public void Compiler_uses_dependency_order_then_lexical_tie_break()
    {
        var fixture = new Fixture();
        var catalog = fixture.Catalog(
            fixture.Factory("output", OwnerSliceId.S6, "provider"),
            fixture.Factory("activity", OwnerSliceId.S3, "resources"),
            fixture.Factory("resources", OwnerSliceId.S2),
            fixture.Factory("provider", OwnerSliceId.S5, "resources"));
        var plan = LiveAudioParticipantPlanCompilerV1.Compile(fixture.Request(
            fixture.Spec("provider", OwnerSliceId.S5), fixture.Spec("resources", OwnerSliceId.S2),
            fixture.Spec("output", OwnerSliceId.S6), fixture.Spec("activity", OwnerSliceId.S3)), catalog);
        Assert.Equal(["resources", "activity", "provider", "output"], plan.Descriptors.Select(value => value.FactoryKey.ToString()));
        Assert.True(plan.Fingerprint.TryWriteBytes(new byte[32]));
        Assert.Empty(plan.SkippedOptionalFactories);
    }

    [Fact]
    public void Optional_missing_factory_is_fingerprinted_as_skipped()
    {
        var fixture = new Fixture();
        var plan = LiveAudioParticipantPlanCompilerV1.Compile(fixture.Request(
            fixture.Spec("resources", OwnerSliceId.S2), fixture.Spec("optional", OwnerSliceId.S3, required: false)),
            fixture.Catalog(fixture.Factory("resources", OwnerSliceId.S2)));
        Assert.Equal(["optional"], plan.SkippedOptionalFactories.Select(value => value.ToString()));
        Assert.Single(plan.Descriptors);
    }

    [Fact]
    public void Catalog_snapshots_each_descriptor_exactly_once()
    {
        var fixture = new Fixture();
        var source = fixture.Factory("resources", OwnerSliceId.S2).Descriptor;
        var factory = new CountingFactory(source);
        var catalog = new LiveAudioParticipantFactoryCatalogV1([factory]);
        var plan = LiveAudioParticipantPlanCompilerV1.Compile(
            fixture.Request(fixture.Spec("resources", OwnerSliceId.S2)), catalog);
        Assert.Single(plan.Descriptors);
        Assert.Equal(1, factory.DescriptorReads);
    }

    [Fact]
    public void Fingerprint_is_stable_across_catalog_and_request_input_order()
    {
        var fixture = new Fixture();
        var first = LiveAudioParticipantPlanCompilerV1.Compile(fixture.Request(
            fixture.Spec("provider", OwnerSliceId.S5), fixture.Spec("resources", OwnerSliceId.S2)),
            fixture.Catalog(fixture.Factory("provider", OwnerSliceId.S5, "resources"), fixture.Factory("resources", OwnerSliceId.S2)));
        var second = LiveAudioParticipantPlanCompilerV1.Compile(fixture.Request(
            fixture.Spec("resources", OwnerSliceId.S2), fixture.Spec("provider", OwnerSliceId.S5)),
            fixture.Catalog(fixture.Factory("resources", OwnerSliceId.S2), fixture.Factory("provider", OwnerSliceId.S5, "resources")));
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Missing_dependency_and_cycle_fail_closed()
    {
        var fixture = new Fixture();
        Assert.Throws<ArgumentException>(() => LiveAudioParticipantPlanCompilerV1.Compile(
            fixture.Request(fixture.Spec("provider", OwnerSliceId.S5)),
            fixture.Catalog(fixture.Factory("provider", OwnerSliceId.S5, "resources"))));
        Assert.Throws<ArgumentException>(() => LiveAudioParticipantPlanCompilerV1.Compile(
            fixture.Request(fixture.Spec("alpha", OwnerSliceId.S2), fixture.Spec("beta", OwnerSliceId.S3)),
            fixture.Catalog(fixture.Factory("alpha", OwnerSliceId.S2, "beta"), fixture.Factory("beta", OwnerSliceId.S3, "alpha"))));
    }

    [Fact]
    public void Missing_relevant_owner_generation_fence_fails_closed()
    {
        var fixture = new Fixture();
        Assert.Throws<ArgumentException>(() => LiveAudioParticipantPlanCompilerV1.Compile(
            fixture.RequestWithoutAxis(AuthorityAxisId.Provider, fixture.Spec("provider", OwnerSliceId.S5)),
            fixture.Catalog(fixture.Factory("provider", OwnerSliceId.S5))));
    }

    [Fact]
    public void Descriptor_rejects_wrong_axis_owner_s10_duplicates_and_out_of_range_deadlines()
    {
        Assert.Throws<ArgumentException>(() => new LiveAudioParticipantDescriptorV1(new BoundedAscii("media"),
            OwnerSliceId.S2, AuthorityAxisId.Provider, [], [new CapacityDimensionId(1)], Duration(1), Duration(1), Duration(1)));
        Assert.Throws<ArgumentException>(() => new LiveAudioParticipantDescriptorV1(new BoundedAscii("replay"),
            OwnerSliceId.S10, AuthorityAxisId.Transport, [], [new CapacityDimensionId(1)], Duration(1), Duration(1), Duration(1)));
        Assert.Throws<ArgumentException>(() => new LiveAudioParticipantDescriptorV1(new BoundedAscii("media"),
            OwnerSliceId.S2, AuthorityAxisId.Graph, [new BoundedAscii("a"), new BoundedAscii("a")],
            [new CapacityDimensionId(1)], Duration(1), Duration(1), Duration(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveAudioParticipantDescriptorV1(new BoundedAscii("media"),
            OwnerSliceId.S2, AuthorityAxisId.Graph, [], [new CapacityDimensionId(1)], new DurationNs(0), Duration(1), Duration(1)));
    }

    [Fact]
    public void Descriptor_stops_enumeration_at_max_plus_one()
    {
        var dependencies = new CountingEnumerable<BoundedAscii>(
            Enumerable.Range(0, 18).Select(index => new BoundedAscii($"dependency-{index:D2}")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveAudioParticipantDescriptorV1(new BoundedAscii("media"),
            OwnerSliceId.S2, AuthorityAxisId.Graph, dependencies, [new CapacityDimensionId(1)], Duration(1), Duration(1), Duration(1)));
        Assert.Equal(17, dependencies.MoveNextCount);

        var dimensions = new CountingEnumerable<CapacityDimensionId>(Enumerable.Repeat(new CapacityDimensionId(1), 18));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveAudioParticipantDescriptorV1(new BoundedAscii("media"),
            OwnerSliceId.S2, AuthorityAxisId.Graph, [], dimensions, Duration(1), Duration(1), Duration(1)));
        Assert.Equal(17, dimensions.MoveNextCount);
    }

    private static DurationNs Duration(long seconds) => new(seconds * 1_000_000_000);

    private sealed class Factory(LiveAudioParticipantDescriptorV1 descriptor) : ILiveAudioParticipantFactoryV1
    {
        public LiveAudioParticipantDescriptorV1 Descriptor { get; } = descriptor;
        public ValueTask<LiveAudioParticipantFactoryResultV1> PrepareAsync(
            LiveAudioParticipantPreparationContextV1 context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<LiveAudioParticipantFactoryResultV1>(
                new LiveAudioParticipantFactoryResultV1.Refused(new BoundedAscii("not-used-by-plan-test")));
    }

    private sealed class CountingFactory(LiveAudioParticipantDescriptorV1 descriptor) : ILiveAudioParticipantFactoryV1
    {
        public int DescriptorReads { get; private set; }
        public LiveAudioParticipantDescriptorV1 Descriptor { get { DescriptorReads++; return descriptor; } }
        public ValueTask<LiveAudioParticipantFactoryResultV1> PrepareAsync(
            LiveAudioParticipantPreparationContextV1 context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<LiveAudioParticipantFactoryResultV1>(
                new LiveAudioParticipantFactoryResultV1.Refused(new BoundedAscii("not-used-by-plan-test")));
    }

    private sealed class CountingEnumerable<T>(IEnumerable<T> source) : IEnumerable<T>
    {
        public int MoveNextCount { get; private set; }
        public IEnumerator<T> GetEnumerator()
        {
            foreach (var item in source) { MoveNextCount++; yield return item; }
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
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
        private readonly LiveAudioPlanId _plan = LiveAudioPlanId.Create();

        internal Fixture()
        {
            _authority = ExpectedAuthorityVectorV1.Create(_session, AllAxes()); var position = new JournalPositionV1(_session, 1);
            _capacity = new CapacityGrantSnapshotV1(CapacityGrantId.Create(), _operation, _authority, position, position,
                new CapacityGrantExpiryV1.NoExpiry(), CapacityGrantStateV1.Reserved, [null!]);
            _capture = new CaptureGrantProofV1(CaptureGrantId.Create(), AuthorizationId.Create(), position, _authority,
                Hash(1), Hash(2), CaptureGrantStateV1.Active, new UtcInstant(1000));
        }

        internal LiveAudioParticipantFactoryCatalogV1 Catalog(params Factory[] values) => new(values);
        internal Factory Factory(string key, OwnerSliceId owner, params string[] dependencies) => new(new LiveAudioParticipantDescriptorV1(
            new BoundedAscii(key), owner, Axis(owner), dependencies.Select(value => new BoundedAscii(value)),
            [new CapacityDimensionId(1)], Duration(5), Duration(30), Duration(5)));
        internal LiveAudioParticipantSpecV1 Spec(string key, OwnerSliceId owner, bool required = true) =>
            new(new BoundedAscii(key), owner, required, Hash((byte)owner));
        internal LiveAudioSessionStartRequestV1 Request(params LiveAudioParticipantSpecV1[] specs) => new(
            _operation, null, new CorrelationEnvelopeV1(_tenant, operationId: _operation), _plan, _authority, _capacity, _capture,
            LiveAudioConcurrencyModeV1.Exclusive, new MonotonicStampV1(_clock, _boot, 100), specs);
        internal LiveAudioSessionStartRequestV1 RequestWithoutAxis(AuthorityAxisId excluded, params LiveAudioParticipantSpecV1[] specs)
        {
            var authority = ExpectedAuthorityVectorV1.Create(_session,
                AllAxes().Where(value => value.AxisId != excluded));
            var position = new JournalPositionV1(_session, 1);
            var capacity = new CapacityGrantSnapshotV1(CapacityGrantId.Create(), _operation, authority, position, position,
                new CapacityGrantExpiryV1.NoExpiry(), CapacityGrantStateV1.Reserved, [null!]);
            var capture = new CaptureGrantProofV1(CaptureGrantId.Create(), AuthorizationId.Create(), position, authority,
                Hash(1), Hash(2), CaptureGrantStateV1.Active, new UtcInstant(1000));
            return new LiveAudioSessionStartRequestV1(_operation, null,
                new CorrelationEnvelopeV1(_tenant, operationId: _operation), _plan, authority, capacity, capture,
                LiveAudioConcurrencyModeV1.Exclusive, new MonotonicStampV1(_clock, _boot, 100), specs);
        }

        private static AuthorityAxisId Axis(OwnerSliceId owner) => owner switch
        {
            OwnerSliceId.S2 => AuthorityAxisId.Graph, OwnerSliceId.S3 => AuthorityAxisId.Activity,
            OwnerSliceId.S4 => AuthorityAxisId.Turn, OwnerSliceId.S5 => AuthorityAxisId.Provider,
            OwnerSliceId.S6 => AuthorityAxisId.Output, OwnerSliceId.S7 => AuthorityAxisId.Tool,
            OwnerSliceId.S8 => AuthorityAxisId.Route, OwnerSliceId.S9 => AuthorityAxisId.Privacy,
            OwnerSliceId.S11 => AuthorityAxisId.Transport, _ => throw new ArgumentOutOfRangeException(nameof(owner)),
        };
        private static Hash256 Hash(byte value) { Span<byte> bytes = stackalloc byte[32]; bytes.Fill(value); Assert.True(Hash256.TryCreate(bytes, out var result)); return result; }
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
