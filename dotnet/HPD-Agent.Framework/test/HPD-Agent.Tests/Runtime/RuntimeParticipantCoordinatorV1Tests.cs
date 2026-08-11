using HPD.Agent.Authority;
using HPD.Agent.Runtime;

namespace HPD.Agent.Tests.Runtime;

public sealed class RuntimeParticipantCoordinatorV1Tests
{
    [Fact]
    public async Task Lifecycle_UsesDependencyOrderThenReverseDrainAndTermination()
    {
        var events = new List<string>();
        var session = Participant("session", events);
        var output = Participant("output", events, dependencies: ["session"]);
        var plan = RuntimeParticipantPlanV1.Compile([output.Descriptor, session.Descriptor]);
        await using var coordinator = new RuntimeParticipantCoordinatorV1(plan, [output, session]);

        Assert.True((await coordinator.PrepareAsync(Admissions(plan))).IsSuccess);
        Assert.Equal(RuntimeParticipantCoordinatorStateV1.Prepared, coordinator.State);
        Assert.True((await coordinator.StartAsync()).IsSuccess);
        Assert.Equal(RuntimeParticipantCoordinatorStateV1.Started, coordinator.State);
        Assert.True((await coordinator.DrainAsync(RuntimeDrainIntentV1.Graceful)).IsSuccess);
        Assert.Equal(RuntimeParticipantCoordinatorStateV1.Draining, coordinator.State);
        Assert.True((await coordinator.TerminateAsync(RuntimeTerminationCauseV1.Requested)).IsSuccess);
        Assert.Equal(RuntimeParticipantCoordinatorStateV1.Completed, coordinator.State);

        Assert.Equal([
            "session:prepare", "output:prepare", "session:start", "output:start",
            "output:drain", "session:drain", "output:terminate:Requested",
            "session:terminate:Requested", "output:dispose", "session:dispose"], events);
    }

    [Fact]
    public async Task PrepareFailure_UnwindsAttemptedParticipantsAndCompletes()
    {
        var events = new List<string>();
        var session = Participant("session", events);
        var output = Participant("output", events, RuntimeParticipantDispositionV1.Refused, ["session"]);
        var tools = Participant("tools", events, dependencies: ["output"]);
        var plan = RuntimeParticipantPlanV1.Compile([tools.Descriptor, output.Descriptor, session.Descriptor]);
        await using var coordinator = new RuntimeParticipantCoordinatorV1(plan, [tools, output, session]);

        var result = await coordinator.PrepareAsync(Admissions(plan));

        Assert.Equal(RuntimeParticipantDispositionV1.Refused, result.Disposition);
        Assert.Equal(RuntimeParticipantCoordinatorStateV1.Completed, coordinator.State);
        Assert.Equal([
            "session:prepare", "output:prepare", "output:terminate:PrepareFailed",
            "session:terminate:PrepareFailed", "tools:dispose", "output:dispose", "session:dispose"], events);
        Assert.DoesNotContain("tools:prepare", events);
    }

    [Fact]
    public async Task StartFailure_UnwindsAllPreparedParticipantsInReverseOrder()
    {
        var events = new List<string>();
        var session = Participant("session", events);
        var output = Participant("output", events, startDisposition: RuntimeParticipantDispositionV1.Failed, dependencies: ["session"]);
        var plan = RuntimeParticipantPlanV1.Compile([output.Descriptor, session.Descriptor]);
        await using var coordinator = new RuntimeParticipantCoordinatorV1(plan, [session, output]);
        Assert.True((await coordinator.PrepareAsync(Admissions(plan))).IsSuccess);

        var result = await coordinator.StartAsync();

        Assert.Equal(RuntimeParticipantDispositionV1.Failed, result.Disposition);
        Assert.Equal(RuntimeParticipantCoordinatorStateV1.Completed, coordinator.State);
        Assert.Equal([
            "session:prepare", "output:prepare", "session:start", "output:start",
            "output:terminate:StartFailed", "session:terminate:StartFailed", "output:dispose", "session:dispose"], events);
    }

    [Fact]
    public async Task ConstructorAndAdmission_RequireExactPlanBindings()
    {
        var events = new List<string>();
        var session = Participant("session", events);
        var plan = RuntimeParticipantPlanV1.Compile([session.Descriptor]);
        Assert.Throws<ArgumentException>(() => new RuntimeParticipantCoordinatorV1(plan, []));
        Assert.Throws<ArgumentException>(() => new RuntimeParticipantCoordinatorV1(plan, [Participant("session", events)]));

        await using var coordinator = new RuntimeParticipantCoordinatorV1(plan, [session]);
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await coordinator.PrepareAsync([]));
        Assert.Equal(RuntimeParticipantCoordinatorStateV1.Created, coordinator.State);
    }

    [Fact]
    public void CoordinatorEnums_HaveExactClosedValues()
    {
        Assert.Equal(new ushort[] { 1, 2, 3, 4, 5, 6 },
            Enum.GetValues<RuntimeParticipantCoordinatorStateV1>().Select(static value => (ushort)value));
    }

    private static RuntimeParticipantAdmissionV1[] Admissions(RuntimeParticipantPlanV1 plan)
    {
        var stamp = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var authority = ExpectedAuthorityVectorV1.Create(stamp, []);
        return plan.OrderedDescriptors
            .Select(descriptor => new RuntimeParticipantAdmissionV1(
                descriptor.Id, new RuntimeParticipantContextV1(ParticipantId.Create(), authority)))
            .ToArray();
    }

    private static FakeParticipant Participant(
        string id,
        List<string> events,
        RuntimeParticipantDispositionV1 prepareDisposition = RuntimeParticipantDispositionV1.Succeeded,
        string[]? dependencies = null,
        RuntimeParticipantDispositionV1 startDisposition = RuntimeParticipantDispositionV1.Succeeded) =>
        new(Descriptor(id, dependencies ?? []), events, prepareDisposition, startDisposition);

    private static RuntimeParticipantDescriptorV1 Descriptor(string id, string[] dependencies) =>
        new(new(id), new("S1"), new("RuntimeParticipant"),
            dependencies.Select(static dependency => new BoundedAscii(dependency)), AuthorityAxisId.Runtime,
            new DurationNs(5_000_000_000), new DurationNs(5_000_000_000),
            new DurationNs(5_000_000_000), new DurationNs(5_000_000_000), [new("journal-bytes")]);

    private sealed class FakeParticipant(
        RuntimeParticipantDescriptorV1 descriptor,
        List<string> events,
        RuntimeParticipantDispositionV1 prepareDisposition,
        RuntimeParticipantDispositionV1 startDisposition) : IRuntimeParticipantV1
    {
        public RuntimeParticipantDescriptorV1 Descriptor { get; } = descriptor;

        public ValueTask<RuntimeParticipantPrepareResultV1> PrepareAsync(
            RuntimeParticipantContextV1 context,
            CancellationToken cancellationToken)
        {
            events.Add($"{Descriptor.Id}:prepare");
            var handle = prepareDisposition == RuntimeParticipantDispositionV1.Succeeded
                ? new RuntimePreparedHandleV1(Descriptor.Id, context)
                : null;
            return ValueTask.FromResult(new RuntimeParticipantPrepareResultV1(prepareDisposition, new("prepare"), handle));
        }

        public ValueTask<RuntimeParticipantResultV1> StartAsync(RuntimePreparedHandleV1 handle, CancellationToken cancellationToken)
        {
            events.Add($"{Descriptor.Id}:start");
            return ValueTask.FromResult(new RuntimeParticipantResultV1(startDisposition, new("start")));
        }

        public ValueTask<RuntimeParticipantResultV1> DrainAsync(RuntimeDrainIntentV1 intent, CancellationToken cancellationToken)
        {
            events.Add($"{Descriptor.Id}:drain");
            return ValueTask.FromResult(new RuntimeParticipantResultV1(RuntimeParticipantDispositionV1.Succeeded, new("drain")));
        }

        public ValueTask<RuntimeParticipantResultV1> TerminateAsync(RuntimeTerminationCauseV1 cause, CancellationToken cancellationToken)
        {
            events.Add($"{Descriptor.Id}:terminate:{cause}");
            return ValueTask.FromResult(new RuntimeParticipantResultV1(RuntimeParticipantDispositionV1.Succeeded, new("terminate")));
        }

        public ValueTask DisposeAsync()
        {
            events.Add($"{Descriptor.Id}:dispose");
            return ValueTask.CompletedTask;
        }
    }
}
