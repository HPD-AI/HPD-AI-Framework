using HPD.Agent.Replay.Abi;

namespace HPD.Agent.Replay.Abi.Tests;

public sealed class ReplayAbiEngineV1Tests
{
    [Fact]
    public void Deterministic_state_is_stable_across_restart()
    {
        var first = Execute();
        var second = Execute();
        Assert.Equal(first, second);
    }

    [Fact]
    public void Generation_fences_stale_handles_and_completion_fences_mutation()
    {
        var engine = new ReplayAbiEngineV1();
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Open([1], out var first));
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Complete(first, out _));
        Assert.Equal(ReplayAbiStatusV1.AlreadyClosed, engine.Step(first, [2]));
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Close(first));
        Assert.Equal(ReplayAbiStatusV1.InvalidHandleGeneration, engine.Status(first, out _));
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Open([1], out var second));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Bounds_and_slot_capacity_fail_closed()
    {
        var engine = new ReplayAbiEngineV1();
        Assert.Equal(ReplayAbiStatusV1.InvalidArgument, engine.Open([], out _));
        Assert.Equal(ReplayAbiStatusV1.InvalidArgument, engine.Open(new byte[ReplayAbiEngineV1.MaximumArtifactBytes + 1], out _));
        var handles = new List<ulong>();
        for (var i = 0; i < ReplayAbiEngineV1.MaximumSlots; i++)
        {
            Assert.Equal(ReplayAbiStatusV1.Ok, engine.Open([(byte)(i + 1)], out var handle));
            handles.Add(handle);
        }
        Assert.Equal(ReplayAbiStatusV1.CapacityRejected, engine.Open([1], out _));
        foreach (var handle in handles) Assert.Equal(ReplayAbiStatusV1.Ok, engine.Close(handle));
    }

    [Fact]
    public void Advance_step_and_explore_are_distinct_and_owned()
    {
        var engine = new ReplayAbiEngineV1();
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Open([0xa1, 0x01, 0x01], out var handle));
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Advance(handle, [0xa1, 0x02, 0x01]));
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Step(handle, [0xa1, 0x03, 0x01]));
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Explore(handle, [0xa1, 0x04, 0x01], out var explored));
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Status(handle, out var status));
        Assert.Equal(88, explored.Length);
        Assert.Equal(explored, status);
        explored[56] ^= 0xff;
        Assert.NotEqual(explored, status);
    }

    private static byte[] Execute()
    {
        var engine = new ReplayAbiEngineV1();
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Open([0xa1, 0x01, 0x01], out var handle));
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Advance(handle, [0xa1, 0x02, 0x01]));
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Step(handle, [0xa1, 0x03, 0x01]));
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Complete(handle, out var result));
        return result;
    }
}
