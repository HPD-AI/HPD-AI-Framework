using HPD.Agent.Authority;
using HPD.Agent.Runtime;

namespace HPD.Agent.Tests.Runtime;

public sealed class RuntimeParticipantPlanV1Tests
{
    [Fact]
    public void Compile_IsDeterministicAndDependencyFirstAcrossInputPermutations()
    {
        var session = Descriptor("session");
        var output = Descriptor("output", "session");
        var provider = Descriptor("provider", "session");
        var tools = Descriptor("tools", "output", "provider");

        var first = RuntimeParticipantPlanV1.Compile([tools, provider, session, output]);
        var second = RuntimeParticipantPlanV1.Compile([output, session, tools, provider]);

        Assert.Equal(["session", "output", "provider", "tools"], Ids(first));
        Assert.Equal(Ids(first), Ids(second));
    }

    [Fact]
    public void Compile_RejectsMissingSelfDuplicateAndCyclicDependencies()
    {
        Assert.Throws<ArgumentException>(() => RuntimeParticipantPlanV1.Compile([Descriptor("output", "session")]));
        Assert.Throws<ArgumentException>(() => RuntimeParticipantPlanV1.Compile([Descriptor("session", "session")]));
        Assert.Throws<ArgumentException>(() => RuntimeParticipantPlanV1.Compile([Descriptor("session"), Descriptor("session")]));
        Assert.Throws<ArgumentException>(() => RuntimeParticipantPlanV1.Compile([
            Descriptor("session", "output"), Descriptor("output", "session") ]));
    }

    [Fact]
    public void Compile_RejectsEmptyNullAndMaximumPlusOne()
    {
        Assert.Throws<ArgumentNullException>(() => RuntimeParticipantPlanV1.Compile(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => RuntimeParticipantPlanV1.Compile([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => RuntimeParticipantPlanV1.Compile(
            Enumerable.Range(0, RuntimeParticipantPlanV1.MaximumParticipants + 1)
                .Select(index => Descriptor($"p{index:D2}"))));
    }

    [Fact]
    public void Compile_ProducesIsolatedImmutablePlans()
    {
        var source = new List<RuntimeParticipantDescriptorV1> { Descriptor("session") };
        var first = RuntimeParticipantPlanV1.Compile(source);
        var second = RuntimeParticipantPlanV1.Compile([Descriptor("resources")]);
        source.Add(Descriptor("output"));

        Assert.Equal(["session"], Ids(first));
        Assert.Equal(["resources"], Ids(second));
        Assert.False(first.OrderedDescriptors is RuntimeParticipantDescriptorV1[]);
    }

    private static string[] Ids(RuntimeParticipantPlanV1 plan) =>
        plan.OrderedDescriptors.Select(static descriptor => descriptor.Id.ToString()).ToArray();

    private static RuntimeParticipantDescriptorV1 Descriptor(string id, params string[] dependencies) =>
        new(new(id), new("S1"), new("RuntimeParticipant"),
            dependencies.Select(static dependency => new BoundedAscii(dependency)), AuthorityAxisId.Runtime,
            new DurationNs(1), new DurationNs(1), new DurationNs(1), new DurationNs(1),
            [new("journal-bytes")]);
}
