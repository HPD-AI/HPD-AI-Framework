using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HPD.Agent.Replay.Abi;

public enum ReplayAbiStatusV1 : int
{
    Ok = 0,
    InvalidArgument = 14,
    InvalidHandleGeneration = 16,
    AlreadyClosed = 17,
    Conflict = 20,
    CapacityRejected = 21,
}

public sealed class ReplayAbiEngineV1
{
    public const int MaximumArtifactBytes = 256 * 1024;
    public const int MaximumOperationBytes = 256 * 1024;
    public const int MaximumSlots = 64;
    private readonly Slot[] _slots = new Slot[MaximumSlots];
    private readonly uint[] _generations = new uint[MaximumSlots];
    private readonly uint[] _closedGenerations = new uint[MaximumSlots];
    private readonly object _gate = new();

    public ReplayAbiStatusV1 Open(ReadOnlySpan<byte> request, out ulong handle)
    {
        handle = 0;
        if (request.IsEmpty || request.Length > MaximumArtifactBytes || !ReplayAbiRequestCodecV1.TryDecodeArtifact(request,out var artifact)) return ReplayAbiStatusV1.InvalidArgument;
        lock (_gate)
        {
            for (var index = 0; index < MaximumSlots; index++)
            {
                if (_slots[index] is not null) continue;
                var generation = unchecked(_generations[index] + 1);
                if (generation == 0) generation = 1;
                _generations[index] = generation;
                _slots[index] = new Slot(artifact);
                handle = ((ulong)generation << 32) | (uint)(index + 1);
                return ReplayAbiStatusV1.Ok;
            }
        }
        return ReplayAbiStatusV1.CapacityRejected;
    }

    public ReplayAbiStatusV1 Advance(ulong handle, ReadOnlySpan<byte> request) => Mutate(handle, request, false,1);
    public ReplayAbiStatusV1 Step(ulong handle, ReadOnlySpan<byte> request) => Mutate(handle, request, true,2);

    public ReplayAbiStatusV1 Explore(ulong handle, ReadOnlySpan<byte> request, out byte[] payload)
    {
        payload = [];
        var status = Mutate(handle, request, false,3);
        if (status != ReplayAbiStatusV1.Ok) return status;
        return Status(handle, out payload);
    }

    public ReplayAbiStatusV1 Status(ulong handle, out byte[] payload)
    {
        payload = [];
        lock (_gate)
        {
            if (!TryGet(handle, out _, out var slot)) return ReplayAbiStatusV1.InvalidHandleGeneration;
            payload = slot.Snapshot(false);
            return ReplayAbiStatusV1.Ok;
        }
    }

    public ReplayAbiStatusV1 Complete(ulong handle, out byte[] payload)
    {
        payload = [];
        lock (_gate)
        {
            if (!TryGet(handle, out _, out var slot)) return ReplayAbiStatusV1.InvalidHandleGeneration;
            slot.Completed = true;
            payload = slot.Snapshot(true);
            return ReplayAbiStatusV1.Ok;
        }
    }

    public ReplayAbiStatusV1 Close(ulong handle)
    {
        lock (_gate)
        {
            var slotNumber=(uint)handle;var generation=(uint)(handle>>32);
            if(slotNumber==0||slotNumber>MaximumSlots||generation==0)return ReplayAbiStatusV1.InvalidHandleGeneration;
            var index=(int)slotNumber-1;
            if(_slots[index] is null&&_closedGenerations[index]==generation)return ReplayAbiStatusV1.AlreadyClosed;
            if (!TryGet(handle, out index, out _)) return ReplayAbiStatusV1.InvalidHandleGeneration;
            _closedGenerations[index]=generation;
            _slots[index] = null!;
            return ReplayAbiStatusV1.Ok;
        }
    }

    private ReplayAbiStatusV1 Mutate(ulong handle, ReadOnlySpan<byte> request, bool step,byte expectedOperation)
    {
        if (request.IsEmpty || request.Length > MaximumOperationBytes || !ReplayAbiRequestCodecV1.TryDecodeOperation(request,expectedOperation,out var payload,out var first,out var second)) return ReplayAbiStatusV1.InvalidArgument;
        lock (_gate)
        {
            if (!TryGet(handle, out _, out var slot)) return ReplayAbiStatusV1.InvalidHandleGeneration;
            if (slot.Completed) return ReplayAbiStatusV1.AlreadyClosed;
            return slot.Apply(payload, step,expectedOperation,first,second);
        }
    }

    private bool TryGet(ulong handle, out int index, out Slot slot)
    {
        var slotNumber = (uint)handle;
        var generation = (uint)(handle >> 32);
        if (slotNumber == 0 || slotNumber > MaximumSlots || generation == 0)
        {
            index = -1;
            slot = null!;
            return false;
        }
        index = (int)slotNumber - 1;
        if (_generations[index] != generation || _slots[index] is null)
        {
            slot = null!;
            return false;
        }
        slot = _slots[index];
        return true;
    }

    private sealed class Slot
    {
        private readonly byte[] _artifactHash;
        private byte[] _stateHash;
        private readonly HashSet<ulong> _completedWork=[];
        internal Slot(ReadOnlySpan<byte> artifact)
        {
            _artifactHash = SHA256.HashData(artifact);
            _stateHash = _artifactHash.ToArray();
        }
        internal ulong AdvanceCount { get; private set; }
        internal ulong StepCount { get; private set; }
        internal ulong Now { get; private set; }
        internal ulong ScheduleBranches { get; private set; }
        internal bool Completed { get; set; }

        internal ReplayAbiStatusV1 Apply(ReadOnlySpan<byte> request, bool step,byte operation,ulong first,ulong second)
        {
            if(operation==1){if(first!=1)return ReplayAbiStatusV1.InvalidArgument;if(second<Now)return ReplayAbiStatusV1.Conflict;if(second==Now)return ReplayAbiStatusV1.Ok;Now=second;}
            else if(operation==2&&!_completedWork.Add(first))return ReplayAbiStatusV1.Ok;
            else if(operation==3){if(first>4096-ScheduleBranches)return ReplayAbiStatusV1.CapacityRejected;ScheduleBranches+=first;}
            Span<byte> prefix = stackalloc byte[49];
            _stateHash.CopyTo(prefix);
            BinaryPrimitives.WriteUInt64BigEndian(prefix[32..], AdvanceCount);
            BinaryPrimitives.WriteUInt64BigEndian(prefix[40..], StepCount);
            prefix[48] = step ? (byte)1 : (byte)0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData("HPD.REPLAY.STATE.V1\0"u8);
            hash.AppendData(prefix);
            hash.AppendData(request);
            _stateHash = hash.GetHashAndReset();
            if (step) StepCount++; else AdvanceCount++;
            return ReplayAbiStatusV1.Ok;
        }

        internal byte[] Snapshot(bool complete)
        {
            var payload = new byte[104];
            payload[0] = 1;
            payload[1] = complete ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(8), Now);
            BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(16), AdvanceCount);
            BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(24), StepCount);
            BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(32), ScheduleBranches);
            _artifactHash.CopyTo(payload, 40);
            _stateHash.CopyTo(payload, 72);
            return payload;
        }
    }
}

internal static class ReplayAbiRequestCodecV1
{
    internal static bool TryDecodeArtifact(ReadOnlySpan<byte> request,out byte[] artifact)
    {
        artifact=[];if(!CanonicalCborValidatorV1.IsValid(request)||request.Length<40||request[0]!=0xa3||request[1]!=0x01||request[2]!=0x01||request[3]!=0x02)return false;
        var offset=4;if(!ByteString(request,ref offset,out var payload)||offset+35!=request.Length||request[offset++]!=0x03||request[offset++]!=0x58||request[offset++]!=0x20)return false;
        var expected=request[offset..];Span<byte> actual=stackalloc byte[32];SHA256.HashData(payload,actual);if(!actual.SequenceEqual(expected))return false;artifact=payload;return true;
    }
    internal static bool TryDecodeOperation(ReadOnlySpan<byte> request,byte expectedOperation,out byte[] payload,out ulong first,out ulong second)
    {
        payload=[];first=0;second=0;if(!CanonicalCborValidatorV1.IsValid(request)||request.Length<6||request[0]!=0xa2||request[1]!=0x01||request[2]!=expectedOperation||request[3]!=0x02)return false;var offset=4;if(!ByteString(request,ref offset,out payload)||offset!=request.Length)return false;
        var payloadOffset=0;
        if(expectedOperation==1)return Read(payload,ref payloadOffset,0xa2)&&Read(payload,ref payloadOffset,0x01)&&Unsigned(payload,ref payloadOffset,out first)&&Read(payload,ref payloadOffset,0x02)&&Unsigned(payload,ref payloadOffset,out second)&&payloadOffset==payload.Length;
        return Read(payload,ref payloadOffset,0xa1)&&Read(payload,ref payloadOffset,0x01)&&Unsigned(payload,ref payloadOffset,out first)&&payloadOffset==payload.Length;
    }
    private static bool Read(ReadOnlySpan<byte> bytes,ref int offset,byte expected)
    {if(offset>=bytes.Length||bytes[offset]!=expected)return false;offset++;return true;}
    private static bool Unsigned(ReadOnlySpan<byte> bytes,ref int offset,out ulong value)
    {
        value=0;if(offset>=bytes.Length)return false;var initial=bytes[offset++];if(initial>>5!=0)return false;var additional=initial&31;
        if(additional<24)value=(ulong)additional;
        else if(additional==24){if(offset>=bytes.Length||bytes[offset]<24)return false;value=bytes[offset++];}
        else if(additional==25){if(offset>bytes.Length-2)return false;value=BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);if(value<=byte.MaxValue)return false;offset+=2;}
        else if(additional==26){if(offset>bytes.Length-4)return false;value=BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);if(value<=ushort.MaxValue)return false;offset+=4;}
        else if(additional==27){if(offset>bytes.Length-8)return false;value=BinaryPrimitives.ReadUInt64BigEndian(bytes[offset..]);if(value<=uint.MaxValue)return false;offset+=8;}
        else return false;
        return value>0;
    }
    private static bool ByteString(ReadOnlySpan<byte> bytes,ref int offset,out byte[] value)
    {
        value=[];if(offset>=bytes.Length)return false;var initial=bytes[offset++];if(initial>>5!=2)return false;var additional=initial&31;ulong length;if(additional<24)length=(ulong)additional;else if(additional==24){if(offset>=bytes.Length||bytes[offset]<24)return false;length=bytes[offset++];}else if(additional==25){if(offset>bytes.Length-2)return false;length=System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);if(length<=byte.MaxValue)return false;offset+=2;}else if(additional==26){if(offset>bytes.Length-4)return false;length=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);if(length<=ushort.MaxValue)return false;offset+=4;}else return false;if(length>int.MaxValue||offset>bytes.Length-(int)length)return false;value=bytes.Slice(offset,(int)length).ToArray();offset+=(int)length;return true;
    }
}
