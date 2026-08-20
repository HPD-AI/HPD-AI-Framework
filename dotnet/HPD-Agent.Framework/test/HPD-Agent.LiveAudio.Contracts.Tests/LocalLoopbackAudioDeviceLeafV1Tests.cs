using HPD.Agent.Audio.Runtime.Transports;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class LocalLoopbackAudioDeviceLeafV1Tests
{
    [Fact]
    public async Task Virtual_capture_and_playout_use_the_S11_lifecycle()
    {
        await using var leaf = Leaf(capacity: 2);
        var state = State();
        state = await Apply(state, new TransportLifecycleCommandV1.Bind(OperationId.Create(), 0), leaf);
        state = await Apply(state, new TransportLifecycleCommandV1.Start(OperationId.Create(), 1), leaf);

        var source = new byte[] { 1, 2, 3, 4 };
        var accepted = Assert.IsType<LocalAudioDeviceWriteResultV1.Accepted>(
            await leaf.WritePlayoutAsync(source));
        source[0] = 99;
        var captured = Assert.IsType<LocalAudioDeviceReadResultV1.Frame>(
            await leaf.ReadCaptureAsync());

        Assert.Equal(accepted.Sequence, captured.Value.Sequence);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, captured.Value.Pcm.ToArray());
        Assert.True(leaf.Descriptor.IsVirtual);

        state = await Apply(state, new TransportLifecycleCommandV1.Stop(OperationId.Create(), 2), leaf);
        Assert.Equal(TransportLifecycleV1.Stopped, state.Snapshot.Lifecycle);
        Assert.IsType<LocalAudioDeviceReadResultV1.End>(await leaf.ReadCaptureAsync());
    }

    [Fact]
    public async Task Capacity_is_bounded_and_recovers_after_capture()
    {
        await using var leaf = Leaf(capacity: 1);
        var state = await Start(leaf);
        Assert.IsType<LocalAudioDeviceWriteResultV1.Accepted>(await leaf.WritePlayoutAsync(new byte[] { 1 }));
        var refused = Assert.IsType<LocalAudioDeviceWriteResultV1.Refused>(
            await leaf.WritePlayoutAsync(new byte[] { 2 }));
        Assert.Equal("local-device-capacity-refused", refused.SafeCode.ToString());
        Assert.IsType<LocalAudioDeviceReadResultV1.Frame>(await leaf.ReadCaptureAsync());
        Assert.IsType<LocalAudioDeviceWriteResultV1.Accepted>(await leaf.WritePlayoutAsync(new byte[] { 3 }));
        _ = state;
    }

    [Fact]
    public async Task Invalid_frames_and_inactive_access_fail_closed()
    {
        await using var leaf = Leaf(capacity: 1, maximumFrameBytes: 2);
        var inactive = Assert.IsType<LocalAudioDeviceWriteResultV1.Refused>(
            await leaf.WritePlayoutAsync(new byte[] { 1 }));
        Assert.Equal("local-device-not-active", inactive.SafeCode.ToString());

        _ = await Start(leaf);
        var empty = Assert.IsType<LocalAudioDeviceWriteResultV1.Refused>(
            await leaf.WritePlayoutAsync(ReadOnlyMemory<byte>.Empty));
        var oversized = Assert.IsType<LocalAudioDeviceWriteResultV1.Refused>(
            await leaf.WritePlayoutAsync(new byte[] { 1, 2, 3 }));
        Assert.Equal("local-device-frame-invalid", empty.SafeCode.ToString());
        Assert.Equal("local-device-frame-invalid", oversized.SafeCode.ToString());
    }

    [Fact]
    public async Task Cancellation_precedes_device_mutation()
    {
        await using var leaf = Leaf(capacity: 1);
        _ = await Start(leaf);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await leaf.WritePlayoutAsync(new byte[] { 1 }, cancellation.Token));
        Assert.IsType<LocalAudioDeviceWriteResultV1.Accepted>(await leaf.WritePlayoutAsync(new byte[] { 2 }));
    }

    [Fact]
    public async Task Coordinator_revision_and_exact_retry_fence_the_leaf()
    {
        await using var leaf = Leaf(capacity: 1);
        var state = State();
        var bind = new TransportLifecycleCommandV1.Bind(OperationId.Create(), 0);
        var applied = Assert.IsType<TransportCoordinatorResultV1.Applied>(
            await TransportCoordinatorV1.ApplyAsync(state, bind, leaf, 8));
        Assert.IsType<TransportCoordinatorResultV1.Duplicate>(
            await TransportCoordinatorV1.ApplyAsync(applied.State, bind, leaf, 8));
        Assert.IsType<TransportCoordinatorResultV1.Rejected>(
            await TransportCoordinatorV1.ApplyAsync(
                applied.State,
                new TransportLifecycleCommandV1.Start(OperationId.Create(), 0),
                leaf,
                8));
    }

    private static LocalLoopbackAudioDeviceLeafV1 Leaf(ushort capacity, uint maximumFrameBytes = 64) =>
        new(new LocalAudioDeviceDescriptorV1(
            new BoundedAscii("virtual-loopback-1"), 48_000, 1, maximumFrameBytes, capacity));

    private static TransportCoordinatorStateV1 State()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var generation = TransportGenerationId.Create();
        var authority = ExpectedAuthorityVectorV1.Create(
            session, [new AuthorityAxisValueV1.Transport(generation)]);
        return TransportCoordinatorV1.Create(
            new TransportPlanV1(OperationId.Create(), TransportProfileV1.LocalDevice, generation, authority));
    }

    private static async Task<TransportCoordinatorStateV1> Start(LocalLoopbackAudioDeviceLeafV1 leaf)
    {
        var state = State();
        state = await Apply(state, new TransportLifecycleCommandV1.Bind(OperationId.Create(), 0), leaf);
        return await Apply(state, new TransportLifecycleCommandV1.Start(OperationId.Create(), 1), leaf);
    }

    private static async Task<TransportCoordinatorStateV1> Apply(
        TransportCoordinatorStateV1 state,
        TransportLifecycleCommandV1 command,
        ITransportLifecycleEffectPortV1 leaf) =>
        Assert.IsType<TransportCoordinatorResultV1.Applied>(
            await TransportCoordinatorV1.ApplyAsync(state, command, leaf, 8)).State;
}
