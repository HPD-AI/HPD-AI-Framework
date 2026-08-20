using HPD.Agent.History.Abi;
using System.Buffers.Binary;
using System.Security.Cryptography;
namespace HPD.Agent.History.Abi.Tests;
public sealed class HistoryAbiEngineV1Tests
{
    [Fact] public void Query_is_bounded_cursor_stable_and_generation_fenced()
    {
        var e=new HistoryAbiEngineV1();var request=Request(HistoryHandleKindV1.Query,[0x83,1,2,3]);
        Assert.Equal(HistoryApiStatusV1.Ok,e.Open(HistoryHandleKindV1.Query,request,out var h));
        Assert.Equal(HistoryApiStatusV1.BufferTooSmall,e.Next(h,HistoryHandleKindV1.Query,2,out var empty,out var cursor,out var required));
        Assert.Empty(empty);Assert.Equal(0ul,cursor);Assert.Equal(4u,required);
        Assert.Equal(HistoryApiStatusV1.Ok,e.Next(h,HistoryHandleKindV1.Query,4,out var bytes,out cursor,out required));
        Assert.Equal(new byte[]{0x83,1,2,3},bytes);Assert.Equal(4ul,cursor);Assert.Equal(0u,required);
        Assert.Equal(HistoryApiStatusV1.EndOfSnapshot,e.Next(h,HistoryHandleKindV1.Query,4,out _,out _,out _));
        Assert.Equal(HistoryApiStatusV1.Ok,e.Close(h,HistoryHandleKindV1.Query));Assert.Equal(HistoryApiStatusV1.Ok,e.Close(h,HistoryHandleKindV1.Query));
        Assert.Equal(HistoryApiStatusV1.InvalidHandleGeneration,e.Next(h,HistoryHandleKindV1.Query,4,out _,out _,out _));
    }
    [Fact] public void Subscription_ack_never_exceeds_delivery()
    {
        var e=new HistoryAbiEngineV1();Assert.Equal(HistoryApiStatusV1.Ok,e.Open(HistoryHandleKindV1.Subscription,Request(HistoryHandleKindV1.Subscription,[0x83,1,2,3]),out var h));
        Assert.Equal(HistoryApiStatusV1.InvalidCursor,e.Ack(h,1));Assert.Equal(HistoryApiStatusV1.Ok,e.Next(h,HistoryHandleKindV1.Subscription,4,out _,out var cursor,out _));
        Assert.Equal(HistoryApiStatusV1.Ok,e.Ack(h,cursor));Assert.Equal(HistoryApiStatusV1.InvalidCursor,e.Ack(h,cursor-1));
    }
    [Fact] public void Export_content_is_owned_and_privacy_cancel_is_queryable()
    {
        var e=new HistoryAbiEngineV1();Assert.Equal(HistoryApiStatusV1.Ok,e.Open(HistoryHandleKindV1.Export,Request(HistoryHandleKindV1.Export,[0x83,4,5,6]),out var export));
        Assert.Equal(HistoryApiStatusV1.Ok,e.OpenContent(export,out var content));Assert.Equal(HistoryApiStatusV1.Ok,e.Next(content,HistoryHandleKindV1.Content,4,out var bytes,out _,out _));
        Assert.Equal(new byte[]{0x83,4,5,6},bytes);bytes[0]=0;Assert.Equal(HistoryApiStatusV1.Ok,e.Status(export,HistoryHandleKindV1.Export,out var status));Assert.Equal(104,status.Length);
        Assert.Equal(1ul,BinaryPrimitives.ReadUInt64BigEndian(status.AsSpan(64,8)));Assert.Equal(SHA256.HashData(Enumerable.Range(1,32).Select(static value=>(byte)value).ToArray()),status.AsSpan(72,32).ToArray());
        Assert.Equal(HistoryApiStatusV1.Ok,e.Open(HistoryHandleKindV1.Privacy,Request(HistoryHandleKindV1.Privacy,[7]),out var privacy));Assert.Equal(HistoryApiStatusV1.Ok,e.Cancel(privacy,HistoryHandleKindV1.Privacy));
        Assert.Equal(HistoryApiStatusV1.Ok,e.Status(privacy,HistoryHandleKindV1.Privacy,out status));Assert.Equal(1,status[1]);
    }
    [Fact] public void Request_authority_kind_integrity_and_slot_bounds_fail_closed()
    {
        var e=new HistoryAbiEngineV1();Assert.Equal(HistoryApiStatusV1.InvalidArgument,e.Open(HistoryHandleKindV1.Query,[],out _));
        Assert.Equal(HistoryApiStatusV1.InvalidArgument,e.Open(HistoryHandleKindV1.Query,Request(HistoryHandleKindV1.Subscription,[1]),out _));
        var zeroAuthorization=Request(HistoryHandleKindV1.Query,[1]);zeroAuthorization.AsSpan(8,32).Clear();Assert.Equal(HistoryApiStatusV1.InvalidArgument,e.Open(HistoryHandleKindV1.Query,zeroAuthorization,out _));
        var corruptIntegrity=Request(HistoryHandleKindV1.Query,[1]);corruptIntegrity[^1]^=0xff;Assert.Equal(HistoryApiStatusV1.InvalidArgument,e.Open(HistoryHandleKindV1.Query,corruptIntegrity,out _));
        var noncanonical=Request(HistoryHandleKindV1.Query,[1]);noncanonical[^1]=0x18;Assert.Equal(HistoryApiStatusV1.InvalidArgument,e.Open(HistoryHandleKindV1.Query,noncanonical,out _));
        Assert.Equal(HistoryApiStatusV1.InvalidArgument,e.Open(HistoryHandleKindV1.Query,new byte[HistoryAbiEngineV1.MaximumRequestBytes+1],out _));
        var handles=new List<ulong>();for(var i=0;i<HistoryAbiEngineV1.MaximumSlots;i++){Assert.Equal(HistoryApiStatusV1.Ok,e.Open(HistoryHandleKindV1.Query,Request(HistoryHandleKindV1.Query,[0xf4]),out var h));handles.Add(h);}
        Assert.Equal(HistoryApiStatusV1.CapacityRejected,e.Open(HistoryHandleKindV1.Query,Request(HistoryHandleKindV1.Query,[1]),out _));foreach(var h in handles)Assert.Equal(HistoryApiStatusV1.Ok,e.Close(h,HistoryHandleKindV1.Query));
    }
    [Fact] public void Status_commits_authorization_and_policy_revision()
    {
        var e=new HistoryAbiEngineV1();var first=Request(HistoryHandleKindV1.Query,[1],1,1);var second=Request(HistoryHandleKindV1.Query,[1],2,2);
        Assert.Equal(HistoryApiStatusV1.Ok,e.Open(HistoryHandleKindV1.Query,first,out var firstHandle));Assert.Equal(HistoryApiStatusV1.Ok,e.Open(HistoryHandleKindV1.Query,second,out var secondHandle));
        Assert.Equal(HistoryApiStatusV1.Ok,e.Status(firstHandle,HistoryHandleKindV1.Query,out var firstStatus));Assert.Equal(HistoryApiStatusV1.Ok,e.Status(secondHandle,HistoryHandleKindV1.Query,out var secondStatus));
        Assert.Equal(1ul,BinaryPrimitives.ReadUInt64BigEndian(firstStatus.AsSpan(64,8)));Assert.Equal(2ul,BinaryPrimitives.ReadUInt64BigEndian(secondStatus.AsSpan(64,8)));
        Assert.False(firstStatus.AsSpan(72,32).SequenceEqual(secondStatus.AsSpan(72,32)));
    }
    [Fact] public void Hold_release_requires_the_open_authority()
    {
        var e=new HistoryAbiEngineV1();var request=Request(HistoryHandleKindV1.Hold,[1],1,1);Assert.Equal(HistoryApiStatusV1.Ok,e.Open(HistoryHandleKindV1.Hold,request,out var handle));
        Assert.Equal(HistoryApiStatusV1.InvalidArgument,e.ReleaseHold(handle,Request(HistoryHandleKindV1.Hold,[1],2,1)));Assert.Equal(HistoryApiStatusV1.InvalidArgument,e.ReleaseHold(handle,Request(HistoryHandleKindV1.Hold,[1],1,2)));
        Assert.Equal(HistoryApiStatusV1.Ok,e.Status(handle,HistoryHandleKindV1.Hold,out var before));Assert.Equal(0,before[1]);Assert.Equal(HistoryApiStatusV1.Ok,e.ReleaseHold(handle,request));Assert.Equal(HistoryApiStatusV1.Ok,e.Status(handle,HistoryHandleKindV1.Hold,out var after));Assert.Equal(1,after[1]);
    }
    [Fact] public void Every_single_byte_request_mutation_fails_closed()
    {
        var canonical=Request(HistoryHandleKindV1.Query,[0xa1,0x01,0x01]);
        for(var index=0;index<canonical.Length;index++){var mutated=canonical.ToArray();mutated[index]^=0x01;var engine=new HistoryAbiEngineV1();Assert.Equal(HistoryApiStatusV1.InvalidArgument,engine.Open(HistoryHandleKindV1.Query,mutated,out _));}
    }
    private static byte[] Request(HistoryHandleKindV1 kind,ReadOnlySpan<byte> payload)
        =>Request(kind,payload,1,1);
    private static byte[] Request(HistoryHandleKindV1 kind,ReadOnlySpan<byte> payload,byte authorizationSeed,byte policyRevision)
    {
        Assert.InRange(payload.Length,1,23);Assert.InRange(policyRevision,1,23);var result=new byte[79+payload.Length];var offset=0;result[offset++]=0xa6;result[offset++]=0x01;result[offset++]=0x01;result[offset++]=0x02;result[offset++]=(byte)kind;result[offset++]=0x03;result[offset++]=0x58;result[offset++]=0x20;for(var i=0;i<32;i++)result[offset++]=(byte)(authorizationSeed+i);result[offset++]=0x04;result[offset++]=policyRevision;result[offset++]=0x05;result[offset++]=(byte)(0x40+payload.Length);payload.CopyTo(result.AsSpan(offset));offset+=payload.Length;result[offset++]=0x06;result[offset++]=0x58;result[offset++]=0x20;Span<byte> revision=stackalloc byte[8];BinaryPrimitives.WriteUInt64BigEndian(revision,policyRevision);using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);hash.AppendData("HPD.HISTORY.REQUEST.V1\0"u8);hash.AppendData([(byte)kind]);hash.AppendData(result.AsSpan(8,32));hash.AppendData(revision);hash.AppendData(payload);hash.GetHashAndReset().CopyTo(result,offset);return result;
    }
}
