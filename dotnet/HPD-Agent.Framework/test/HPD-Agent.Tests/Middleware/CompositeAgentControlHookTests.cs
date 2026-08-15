using HPD.Agent.Authority;
using HPD.Agent.Middleware;

namespace HPD.Agent.Tests.Middleware;

public sealed class CompositeAgentControlHookTests
{
    [Fact]
    public async Task CompositeRunsImmutableParticipantsInCanonicalOrder()
    {
        var observed = new List<string>();
        var composite = new CompositeAgentControlHook([
            Participant("z", 2, _ => { observed.Add("z"); return new AgentControlObservationResult.Observed(); }),
            Participant("b", 1, _ => { observed.Add("b"); return new AgentControlObservationResult.NotHandled(); }),
            Participant("a", 1, _ => { observed.Add("a"); return new AgentControlObservationResult.Observed(); }),
        ]);

        var result = await composite.ObserveAsync(Envelope(AgentControlKind.ToolObservation));

        Assert.IsType<AgentControlObservationResult.Observed>(result);
        Assert.Equal(["a", "b", "z"], observed);
    }

    [Fact]
    public async Task EmptyCompositeLeavesAdvisoryObservationsUnhandled()
    {
        var composite = new CompositeAgentControlHook([]);

        Assert.IsType<AgentControlObservationResult.NotHandled>(
            await composite.ObserveAsync(Envelope(AgentControlKind.ToolObservation)));
    }

    [Fact]
    public async Task RejectionAndFailureStopLaterParticipantsWithoutFabricatingObservation()
    {
        var calls = 0;
        var rejection = new CompositeAgentControlHook([
            Participant("a", 0, _ => new AgentControlObservationResult.Rejected(new BoundedAscii("denied"))),
            Participant("b", 1, _ => { calls++; return new AgentControlObservationResult.Observed(); }),
        ]);
        var failure = new CompositeAgentControlHook([
            new AgentControlParticipant(new BoundedAscii("a"), 0, new ThrowingHook()),
            Participant("b", 1, _ => { calls++; return new AgentControlObservationResult.Observed(); }),
        ]);

        Assert.Equal("denied", Assert.IsType<AgentControlObservationResult.Rejected>(
            await rejection.ObserveAsync(Envelope(AgentControlKind.ToolObservation))).SafeCode.ToString());
        Assert.Equal("hook-failed", Assert.IsType<AgentControlObservationResult.Rejected>(
            await failure.ObserveAsync(Envelope(AgentControlKind.ToolObservation))).SafeCode.ToString());
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task CancellationPropagatesAndEnvelopeOwnsItsBoundedPayload()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var envelope = Envelope(AgentControlKind.RuntimeLifecycle, bytes);
        bytes[0] = 9;
        Assert.Equal(new byte[] { 1, 2, 3 }, envelope.VersionedPayload.ToArray());
        var exposed = envelope.VersionedPayload.ToArray();
        exposed[0] = 8;
        Assert.Equal(new byte[] { 1, 2, 3 }, envelope.VersionedPayload.ToArray());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new CompositeAgentControlHook([]).ObserveAsync(envelope, cancellation.Token));
    }

    [Fact]
    public void ContractsRejectInvalidAndUnboundedInputs()
    {
        Assert.Throws<ArgumentException>(() => new AgentControlEnvelope(
            default, null, AgentControlKind.ToolObservation, new byte[] { 1 }, new BoundedAscii("x"), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentControlEnvelope(
            OperationId.Create(), null, AgentControlKind.ToolObservation, ReadOnlyMemory<byte>.Empty, new BoundedAscii("x"), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentControlEnvelope(
            OperationId.Create(), null, AgentControlKind.ToolObservation,
            new byte[AgentControlEnvelope.MaximumPayloadBytes + 1], new BoundedAscii("x"), 1));
        Assert.Throws<ArgumentException>(() => new AgentControlEnvelope(
            OperationId.Create(), null, AgentControlKind.ToolObservation, new byte[] { 1 }, new BoundedAscii("x"), 1,
            new JournalPositionV1(new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()), 1)));
        Assert.Throws<ArgumentException>(() => new CompositeAgentControlHook([
            Participant("same", 0, _ => new AgentControlObservationResult.Observed()),
            Participant("same", 1, _ => new AgentControlObservationResult.Observed()),
        ]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompositeAgentControlHook(
            Enumerable.Range(0, CompositeAgentControlHook.MaximumParticipants + 1)
                .Select(index => Participant($"p{index}", index, _ => new AgentControlObservationResult.Observed()))));
    }

    private static AgentControlEnvelope Envelope(AgentControlKind kind, byte[]? payload = null) => new(
        OperationId.Create(), null, kind, payload ?? new byte[] { 1 }, new BoundedAscii("fixture.v1"), 1);

    private static AgentControlParticipant Participant(
        string key, int order, Func<AgentControlEnvelope, AgentControlObservationResult> observe) =>
        new(new BoundedAscii(key), order, new DelegateHook(observe));

    private sealed class DelegateHook(Func<AgentControlEnvelope, AgentControlObservationResult> observe) : IAgentControlHook
    {
        public ValueTask<AgentControlObservationResult> ObserveAsync(
            AgentControlEnvelope envelope, CancellationToken waitCancellation = default) =>
            ValueTask.FromResult(observe(envelope));
    }

    private sealed class ThrowingHook : IAgentControlHook
    {
        public ValueTask<AgentControlObservationResult> ObserveAsync(
            AgentControlEnvelope envelope, CancellationToken waitCancellation = default) =>
            throw new InvalidOperationException("fixture");
    }
}
