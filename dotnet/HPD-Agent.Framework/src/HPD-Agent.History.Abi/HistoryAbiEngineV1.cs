using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HPD.Agent.History.Abi;

public enum HistoryApiStatusV1 : int
{
    Ok=0, EndOfSnapshot=1, Pending=2, Timeout=3, Cancelled=4,
    BufferTooSmall=5, InvalidArgument=14, InvalidCursor=15,
    InvalidHandleGeneration=16, AlreadyClosed=17, CapacityRejected=21,
}
public enum HistoryHandleKindV1 : byte { Query=1,Subscription=2,Export=3,Content=4,Privacy=5,Hold=6 }

public sealed class HistoryAbiEngineV1
{
    public const uint AbiVersion=0x00010000;
    public const int MaximumRequestBytes=256*1024;
    public const int MaximumSlots=64;
    private readonly Slot?[] _slots=new Slot?[MaximumSlots];
    private readonly uint[] _generations=new uint[MaximumSlots];
    private readonly uint[] _closedGenerations=new uint[MaximumSlots];
    private readonly object _gate=new();

    public HistoryApiStatusV1 Open(HistoryHandleKindV1 kind,ReadOnlySpan<byte> request,out ulong handle)
    {
        handle=0;if(request.IsEmpty||request.Length>MaximumRequestBytes||!HistoryAbiRequestCodecV1.TryDecode(request,kind,out var payload,out var authorization,out var policyRevision))return HistoryApiStatusV1.InvalidArgument;
        lock(_gate){for(var index=0;index<MaximumSlots;index++){if(_slots[index] is not null)continue;var generation=unchecked(_generations[index]+1);if(generation==0)generation=1;_generations[index]=generation;_slots[index]=new(kind,payload,authorization,policyRevision);handle=((ulong)generation<<32)|(uint)(index+1);return HistoryApiStatusV1.Ok;}}
        return HistoryApiStatusV1.CapacityRejected;
    }
    public HistoryApiStatusV1 Next(ulong handle,HistoryHandleKindV1 kind,uint capacity,out byte[] chunk,out ulong cursor,out uint required)
    {
        chunk=[];cursor=0;required=0;lock(_gate){if(!TryGet(handle,kind,out _,out var slot))return HistoryApiStatusV1.InvalidHandleGeneration;if(slot.Cursor>=(ulong)slot.Payload.Length){cursor=slot.Cursor;return HistoryApiStatusV1.EndOfSnapshot;}var remaining=(uint)(slot.Payload.Length-(int)slot.Cursor);var count=Math.Min(remaining,4096u);if(capacity<count){required=count;cursor=slot.Cursor;return HistoryApiStatusV1.BufferTooSmall;}chunk=slot.Payload.AsSpan((int)slot.Cursor,(int)count).ToArray();slot.Cursor+=count;cursor=slot.Cursor;return HistoryApiStatusV1.Ok;}
    }
    public HistoryApiStatusV1 Ack(ulong handle,ulong cursor)
    {lock(_gate){if(!TryGet(handle,HistoryHandleKindV1.Subscription,out _,out var slot))return HistoryApiStatusV1.InvalidHandleGeneration;if(cursor<slot.Acknowledged||cursor>slot.Cursor)return HistoryApiStatusV1.InvalidCursor;slot.Acknowledged=cursor;return HistoryApiStatusV1.Ok;}}
    public HistoryApiStatusV1 Status(ulong handle,HistoryHandleKindV1 kind,out byte[] payload)
    {payload=[];lock(_gate){if(!TryGet(handle,kind,out _,out var slot))return HistoryApiStatusV1.InvalidHandleGeneration;payload=new byte[104];payload[0]=(byte)kind;payload[1]=slot.Cancelled?(byte)1:(byte)0;BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(8),slot.Cursor);BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(16),slot.Acknowledged);slot.Fingerprint.CopyTo(payload,24);BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(56),(ulong)slot.Payload.Length);BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(64),slot.PolicyRevision);slot.AuthorizationHash.CopyTo(payload,72);return HistoryApiStatusV1.Ok;}}
    public HistoryApiStatusV1 OpenContent(ulong exportHandle,out ulong contentHandle)
    {contentHandle=0;lock(_gate){if(!TryGet(exportHandle,HistoryHandleKindV1.Export,out _,out var slot))return HistoryApiStatusV1.InvalidHandleGeneration;return OpenUnderLock(HistoryHandleKindV1.Content,slot.Payload,slot.Authorization,slot.PolicyRevision,out contentHandle);}}
    public HistoryApiStatusV1 Cancel(ulong handle,HistoryHandleKindV1 kind)
    {lock(_gate){if(!TryGet(handle,kind,out _,out var slot))return HistoryApiStatusV1.InvalidHandleGeneration;slot.Cancelled=true;return HistoryApiStatusV1.Ok;}}
    public HistoryApiStatusV1 Close(ulong handle,HistoryHandleKindV1 kind)
    {lock(_gate){uint number=(uint)handle,generation=(uint)(handle>>32);if(number==0||number>MaximumSlots||generation==0)return HistoryApiStatusV1.InvalidHandleGeneration;var index=(int)number-1;if(_slots[index] is null&&_closedGenerations[index]==generation)return HistoryApiStatusV1.Ok;if(!TryGet(handle,kind,out index,out _))return HistoryApiStatusV1.InvalidHandleGeneration;_closedGenerations[index]=generation;_slots[index]=null;return HistoryApiStatusV1.Ok;}}
    public HistoryApiStatusV1 ReleaseHold(ulong handle,ReadOnlySpan<byte> request)
    {if(request.IsEmpty||!HistoryAbiRequestCodecV1.TryDecode(request,HistoryHandleKindV1.Hold,out _,out _,out _))return HistoryApiStatusV1.InvalidArgument;return Cancel(handle,HistoryHandleKindV1.Hold);}
    private HistoryApiStatusV1 OpenUnderLock(HistoryHandleKindV1 kind,ReadOnlySpan<byte> request,ReadOnlySpan<byte> authorization,ulong policyRevision,out ulong handle)
    {handle=0;for(var index=0;index<MaximumSlots;index++){if(_slots[index] is not null)continue;var generation=unchecked(_generations[index]+1);if(generation==0)generation=1;_generations[index]=generation;_slots[index]=new(kind,request,authorization,policyRevision);handle=((ulong)generation<<32)|(uint)(index+1);return HistoryApiStatusV1.Ok;}return HistoryApiStatusV1.CapacityRejected;}
    private bool TryGet(ulong handle,HistoryHandleKindV1 kind,out int index,out Slot slot)
    {uint number=(uint)handle,generation=(uint)(handle>>32);if(number==0||number>MaximumSlots||generation==0){index=-1;slot=null!;return false;}index=(int)number-1;if(_generations[index]!=generation||_slots[index] is not { } found||found.Kind!=kind){slot=null!;return false;}slot=found;return true;}
    private sealed class Slot
    {internal Slot(HistoryHandleKindV1 kind,ReadOnlySpan<byte> payload,ReadOnlySpan<byte> authorization,ulong policyRevision){Kind=kind;Payload=payload.ToArray();Authorization=authorization.ToArray();Fingerprint=SHA256.HashData(payload);AuthorizationHash=SHA256.HashData(authorization);PolicyRevision=policyRevision;}internal HistoryHandleKindV1 Kind{get;}internal byte[] Payload{get;}internal byte[] Authorization{get;}internal byte[] Fingerprint{get;}internal byte[] AuthorizationHash{get;}internal ulong PolicyRevision{get;}internal ulong Cursor{get;set;}internal ulong Acknowledged{get;set;}internal bool Cancelled{get;set;}}
}

internal static class HistoryAbiRequestCodecV1
{
    internal static bool TryDecode(ReadOnlySpan<byte> request,HistoryHandleKindV1 expectedKind,out byte[] payload,out byte[] authorizationBytes,out ulong policyRevision)
    {
        payload=[];authorizationBytes=[];policyRevision=0;if(!CanonicalCborValidatorV1.IsValid(request)||request.Length<80||request[0]!=0xa6||request[1]!=0x01||request[2]!=0x01||request[3]!=0x02||request[4]!=(byte)expectedKind||request[5]!=0x03||request[6]!=0x58||request[7]!=0x20)return false;
        var authorization=request.Slice(8,32);if(authorization.IndexOfAnyExcept((byte)0)<0)return false;var offset=40;if(request[offset++]!=0x04||!Unsigned(request,ref offset,out var revision)||offset>=request.Length||request[offset++]!=0x05||!ByteString(request,ref offset,out payload)||offset+35!=request.Length||request[offset++]!=0x06||request[offset++]!=0x58||request[offset++]!=0x20||!CanonicalCborValidatorV1.IsValid(payload))return false;
        Span<byte> revisionBytes=stackalloc byte[8];BinaryPrimitives.WriteUInt64BigEndian(revisionBytes,revision);using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);hash.AppendData("HPD.HISTORY.REQUEST.V1\0"u8);hash.AppendData([(byte)expectedKind]);hash.AppendData(authorization);hash.AppendData(revisionBytes);hash.AppendData(payload);Span<byte> actual=stackalloc byte[32];if(!hash.TryGetHashAndReset(actual,out var written)||written!=32||!actual.SequenceEqual(request[offset..]))return false;authorizationBytes=authorization.ToArray();policyRevision=revision;return true;
    }
    private static bool Unsigned(ReadOnlySpan<byte> bytes,ref int offset,out ulong value)
    {value=0;if(offset>=bytes.Length)return false;var initial=bytes[offset++];if(initial>>5!=0)return false;var additional=initial&31;if(additional<24)value=(ulong)additional;else if(additional==24){if(offset>=bytes.Length||bytes[offset]<24)return false;value=bytes[offset++];}else if(additional==25){if(offset>bytes.Length-2)return false;value=BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);if(value<=byte.MaxValue)return false;offset+=2;}else if(additional==26){if(offset>bytes.Length-4)return false;value=BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);if(value<=ushort.MaxValue)return false;offset+=4;}else if(additional==27){if(offset>bytes.Length-8)return false;value=BinaryPrimitives.ReadUInt64BigEndian(bytes[offset..]);if(value<=uint.MaxValue)return false;offset+=8;}else return false;return value>0;}
    private static bool ByteString(ReadOnlySpan<byte> bytes,ref int offset,out byte[] value)
    {value=[];if(offset>=bytes.Length)return false;var initial=bytes[offset++];if(initial>>5!=2)return false;var additional=initial&31;ulong length;if(additional<24)length=(ulong)additional;else if(additional==24){if(offset>=bytes.Length||bytes[offset]<24)return false;length=bytes[offset++];}else if(additional==25){if(offset>bytes.Length-2)return false;length=BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);if(length<=byte.MaxValue)return false;offset+=2;}else if(additional==26){if(offset>bytes.Length-4)return false;length=BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);if(length<=ushort.MaxValue)return false;offset+=4;}else return false;if(length>int.MaxValue||offset>bytes.Length-(int)length)return false;value=bytes.Slice(offset,(int)length).ToArray();offset+=(int)length;return true;}
}
