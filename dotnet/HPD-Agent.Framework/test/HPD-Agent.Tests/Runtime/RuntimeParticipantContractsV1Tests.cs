using HPD.Agent.Authority;
using HPD.Agent.Runtime;

namespace HPD.Agent.Tests.Runtime;

public sealed class RuntimeParticipantContractsV1Tests
{
    [Fact]
    public void Descriptor_CanonicalizesAndJoinsRegisteredAxesAndDimensions()
    {
        var descriptor = new RuntimeParticipantDescriptorV1(
            new("provider"), new("S5"), new("ProviderProtocolEffects"),
            [new("session"), new("resources")], AuthorityAxisId.Provider,
            new DurationNs(1), new DurationNs(2), new DurationNs(3),
            [new("provider-inflight"), new("encoded-bytes")]);

        Assert.Equal(["resources", "session"], descriptor.Dependencies.Select(static value => value.ToString()));
        Assert.Equal(["encoded-bytes", "provider-inflight"], descriptor.CapacityDimensions.Select(static value => value.ToString()));
        Assert.Equal(AuthorityAxisId.Provider, descriptor.GenerationFence);
    }

    [Fact]
    public void Descriptor_RejectsUnknownDuplicateUnboundedAndNonpositiveValues()
    {
        Assert.Throws<ArgumentException>(() => Create(axis: (AuthorityAxisId)99));
        Assert.Throws<ArgumentException>(() => Create(dimensions: [new("unknown-dimension")]));
        Assert.Throws<ArgumentException>(() => Create(dependencies: [new("session"), new("session")]));
        Assert.Throws<ArgumentException>(() => Create(prepare: new DurationNs(0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(dependencies: Enumerable.Range(0, 33).Select(index => new BoundedAscii($"p{index}"))));
    }

    [Fact]
    public void ContextAndResult_RejectInvalidAuthorityAndClosedEnums()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var authority = ExpectedAuthorityVectorV1.Create(session, []);
        var context = new RuntimeParticipantContextV1(ParticipantId.Create(), authority);
        var result = new RuntimeParticipantResultV1(RuntimeParticipantDispositionV1.Succeeded, new("Prepared"));

        Assert.True(context.ParticipantId.IsValid);
        Assert.True(result.IsSuccess);
        Assert.Throws<ArgumentException>(() => new RuntimeParticipantContextV1(default, authority));
        Assert.Throws<ArgumentNullException>(() => new RuntimeParticipantContextV1(ParticipantId.Create(), null!));
        Assert.Throws<ArgumentException>(() => new RuntimeParticipantResultV1((RuntimeParticipantDispositionV1)99, new("bad")));
    }

    [Fact]
    public void LifecycleEnums_HaveExactClosedValues()
    {
        Assert.Equal(new ushort[] { 1, 2, 3, 4, 5 }, Enum.GetValues<RuntimeParticipantDispositionV1>().Select(static value => (ushort)value));
        Assert.Equal(new ushort[] { 1, 2 }, Enum.GetValues<RuntimeDrainIntentV1>().Select(static value => (ushort)value));
        Assert.Equal(new ushort[] { 1, 2, 3, 4, 5, 6, 7 }, Enum.GetValues<RuntimeTerminationCauseV1>().Select(static value => (ushort)value));
    }

    private static RuntimeParticipantDescriptorV1 Create(
        IEnumerable<BoundedAscii>? dependencies = null,
        AuthorityAxisId axis = AuthorityAxisId.Runtime,
        DurationNs? prepare = null,
        IEnumerable<BoundedAscii>? dimensions = null) =>
        new(new("session"), new("S1"), new("SessionLifecycle"), dependencies ?? [], axis,
            prepare ?? new DurationNs(1), new DurationNs(1), new DurationNs(1), dimensions ?? [new("journal-bytes")]);
}
