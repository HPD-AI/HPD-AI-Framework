using HPD.Agent.Replay.Abi;
using HPD.Agent.Replay;
using System.Security.Cryptography;

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
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Open(Artifact([1]), out var first));
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Complete(first, out _));
        Assert.Equal(ReplayAbiStatusV1.AlreadyClosed, engine.Step(first, Operation(2,[0xa1,0x01,0x02])));
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Close(first));
        Assert.Equal(ReplayAbiStatusV1.AlreadyClosed, engine.Close(first));
        Assert.Equal(ReplayAbiStatusV1.InvalidHandleGeneration, engine.Status(first, out _));
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Open(Artifact([1]), out var second));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Bounds_and_slot_capacity_fail_closed()
    {
        var engine = new ReplayAbiEngineV1();
        Assert.Equal(ReplayAbiStatusV1.InvalidArgument, engine.Open([], out _));
        Assert.Equal(ReplayAbiStatusV1.InvalidArgument, engine.Open([0x18,0x01], out _));
        Assert.Equal(ReplayAbiStatusV1.InvalidArgument, engine.Open([0xbf,0xff], out _));
        Assert.Equal(ReplayAbiStatusV1.InvalidArgument, engine.Open([0xa2,0x02,0x01,0x01,0x01], out _));
        var corrupt=Artifact([1]);corrupt[^1]^=0xff;Assert.Equal(ReplayAbiStatusV1.InvalidArgument,engine.Open(corrupt,out _));
        Assert.Equal(ReplayAbiStatusV1.InvalidArgument, engine.Open(new byte[ReplayAbiEngineV1.MaximumArtifactBytes + 1], out _));
        var handles = new List<ulong>();
        for (var i = 0; i < ReplayAbiEngineV1.MaximumSlots; i++)
        {
            Assert.Equal(ReplayAbiStatusV1.Ok, engine.Open(Artifact([0xf4]), out var handle));
            handles.Add(handle);
        }
        Assert.Equal(ReplayAbiStatusV1.CapacityRejected, engine.Open(Artifact([1]), out _));
        foreach (var handle in handles) Assert.Equal(ReplayAbiStatusV1.Ok, engine.Close(handle));
    }

    [Fact]
    public void Advance_step_and_explore_are_distinct_and_owned()
    {
        var engine = new ReplayAbiEngineV1();
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Open(Artifact([0xa1, 0x01, 0x01]), out var handle));
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Advance(handle, Operation(1,[0xa2,0x01,0x01,0x02,0x05])));
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Advance(handle, Operation(1,[0xa2,0x01,0x19,0x03,0xe8,0x02,0x19,0x03,0xe8])));
        Assert.Equal(ReplayAbiStatusV1.Ok,engine.Status(handle,out var largeStatus));Assert.Equal(1000ul,System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(largeStatus.AsSpan(8,8)));
        Assert.Equal(ReplayAbiStatusV1.Conflict, engine.Advance(handle, Operation(1,[0xa2,0x01,0x01,0x02,0x04])));
        Assert.Equal(ReplayAbiStatusV1.InvalidArgument, engine.Advance(handle, Operation(2,[0xa1,0x01,0x01])));
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Step(handle, Operation(2,[0xa1,0x01,0x07])));
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Status(handle,out var beforeDuplicate));Assert.Equal(ReplayAbiStatusV1.Ok,engine.Step(handle,Operation(2,[0xa1,0x01,0x07])));Assert.Equal(ReplayAbiStatusV1.Ok,engine.Status(handle,out var afterDuplicate));Assert.Equal(beforeDuplicate,afterDuplicate);
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Explore(handle, Operation(3,[0xa1,0x01,0x03]), out var explored));
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Status(handle, out var status));
        Assert.Equal(104, explored.Length);
        Assert.Equal(explored, status);
        explored[72] ^= 0xff;
        Assert.NotEqual(explored, status);
    }

    [Fact]
    public void Replay_ids_are_family_scoped_canonical_and_nonzero()
    {
        Assert.True(ReplayArtifactId.TryCreate("rpa:00000000000000000000000001", out var artifact));
        Assert.Equal("rpa:00000000000000000000000001", artifact.Value);
        Assert.False(ReplayArtifactId.TryCreate("run:00000000000000000000000001", out _));
        Assert.False(ReplayArtifactId.TryCreate("rpa:00000000000000000000000000", out _));
        Assert.False(ReplayArtifactId.TryCreate("rpa:80000000000000000000000001", out _));
        Assert.False(ReplayArtifactId.TryCreate("rpa:0000000000000000000000000I", out _));
    }

    [Fact]
    public void Replay_bounds_reject_zero_and_inconsistent_capacity()
    {
        Assert.False(ReplayBoundsV1.TryCreate(0,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,out _));
        Assert.False(ReplayBoundsV1.TryCreate(1,2,1,1,1,1,1,1,1,1,1,1,1,1,1,1,out _));
        Assert.False(ReplayBoundsV1.TryCreate(2,1,1,1,1,2,1,1,1,1,1,1,1,1,1,1,out _));
        Assert.True(ReplayBoundsV1.TryCreate(2,1,1,1,2,1,1,1,1,2,1,1,1,1,1,1,out var bounds));
        Assert.NotNull(bounds);
    }

    private static byte[] Execute()
    {
        var engine = new ReplayAbiEngineV1();
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Open(Artifact([0xa1, 0x01, 0x01]), out var handle));
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Advance(handle, Operation(1,[0xa2,0x01,0x01,0x02,0x05])));
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Step(handle, Operation(2,[0xa1,0x01,0x07])));
        Assert.Equal(ReplayAbiStatusV1.Ok, engine.Complete(handle, out var result));
        return result;
    }

    private static byte[] Artifact(ReadOnlySpan<byte> payload)
    {
        Assert.InRange(payload.Length,1,23);var hash=SHA256.HashData(payload);var result=new byte[40+payload.Length];var offset=0;result[offset++]=0xa3;result[offset++]=0x01;result[offset++]=0x01;result[offset++]=0x02;result[offset++]=(byte)(0x40+payload.Length);payload.CopyTo(result.AsSpan(offset));offset+=payload.Length;result[offset++]=0x03;result[offset++]=0x58;result[offset++]=0x20;hash.CopyTo(result,offset);return result;
    }
    private static byte[] Operation(byte operation,ReadOnlySpan<byte> payload)
    {
        Assert.InRange(payload.Length,1,23);var result=new byte[5+payload.Length];result[0]=0xa2;result[1]=0x01;result[2]=operation;result[3]=0x02;result[4]=(byte)(0x40+payload.Length);payload.CopyTo(result.AsSpan(5));return result;
    }
}
