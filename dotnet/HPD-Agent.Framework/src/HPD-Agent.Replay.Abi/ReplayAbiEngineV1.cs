using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HPD.Agent.Replay.Abi;

public enum ReplayAbiStatusV1 : int
{
    Ok = 0,
    InvalidArgument = 14,
    InvalidHandleGeneration = 16,
    AlreadyClosed = 17,
    CapacityRejected = 21,
}

public sealed class ReplayAbiEngineV1
{
    public const int MaximumArtifactBytes = 256 * 1024;
    public const int MaximumOperationBytes = 256 * 1024;
    public const int MaximumSlots = 64;
    private readonly Slot[] _slots = new Slot[MaximumSlots];
    private readonly uint[] _generations = new uint[MaximumSlots];
    private readonly object _gate = new();

    public ReplayAbiStatusV1 Open(ReadOnlySpan<byte> request, out ulong handle)
    {
        handle = 0;
        if (request.IsEmpty || request.Length > MaximumArtifactBytes) return ReplayAbiStatusV1.InvalidArgument;
        lock (_gate)
        {
            for (var index = 0; index < MaximumSlots; index++)
            {
                if (_slots[index] is not null) continue;
                var generation = unchecked(_generations[index] + 1);
                if (generation == 0) generation = 1;
                _generations[index] = generation;
                _slots[index] = new Slot(request);
                handle = ((ulong)generation << 32) | (uint)(index + 1);
                return ReplayAbiStatusV1.Ok;
            }
        }
        return ReplayAbiStatusV1.CapacityRejected;
    }

    public ReplayAbiStatusV1 Advance(ulong handle, ReadOnlySpan<byte> request) => Mutate(handle, request, false);
    public ReplayAbiStatusV1 Step(ulong handle, ReadOnlySpan<byte> request) => Mutate(handle, request, true);

    public ReplayAbiStatusV1 Explore(ulong handle, ReadOnlySpan<byte> request, out byte[] payload)
    {
        payload = [];
        var status = Mutate(handle, request, false);
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
            if (!TryGet(handle, out var index, out _)) return ReplayAbiStatusV1.InvalidHandleGeneration;
            _slots[index] = null!;
            return ReplayAbiStatusV1.Ok;
        }
    }

    private ReplayAbiStatusV1 Mutate(ulong handle, ReadOnlySpan<byte> request, bool step)
    {
        if (request.IsEmpty || request.Length > MaximumOperationBytes) return ReplayAbiStatusV1.InvalidArgument;
        lock (_gate)
        {
            if (!TryGet(handle, out _, out var slot)) return ReplayAbiStatusV1.InvalidHandleGeneration;
            if (slot.Completed) return ReplayAbiStatusV1.AlreadyClosed;
            slot.Apply(request, step);
            return ReplayAbiStatusV1.Ok;
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
        internal Slot(ReadOnlySpan<byte> artifact)
        {
            _artifactHash = SHA256.HashData(artifact);
            _stateHash = _artifactHash.ToArray();
        }
        internal ulong AdvanceCount { get; private set; }
        internal ulong StepCount { get; private set; }
        internal bool Completed { get; set; }

        internal void Apply(ReadOnlySpan<byte> request, bool step)
        {
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
        }

        internal byte[] Snapshot(bool complete)
        {
            var payload = new byte[88];
            payload[0] = 1;
            payload[1] = complete ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(8), AdvanceCount);
            BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(16), StepCount);
            _artifactHash.CopyTo(payload, 24);
            _stateHash.CopyTo(payload, 56);
            return payload;
        }
    }
}
