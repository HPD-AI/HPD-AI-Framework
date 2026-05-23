using System.Runtime.InteropServices;

namespace Helium.Hastlayer;

/// <summary>
/// Live typed views over a SimpleMemory32 payload. This mirrors the Hastlayer
/// SimpleMemoryAccessor.Get() model, where callers can work directly over the
/// memory backing when the device path exposes it.
/// </summary>
public readonly ref struct SimpleMemorySpanAdapter
{
    private readonly Span<byte> _memory;

    public SimpleMemorySpanAdapter(SimpleMemory32 memory) => _memory = memory.GetLiveMemory().Span;

    public Span<uint> AsUInt32Span() =>
        MemoryMarshal.Cast<byte, uint>(_memory);

    public Span<uint> UInt32Slice(int cellIndex, int cellCount)
    {
        if (cellIndex < 0) throw new ArgumentOutOfRangeException(nameof(cellIndex));
        if (cellCount < 0) throw new ArgumentOutOfRangeException(nameof(cellCount));
        return AsUInt32Span().Slice(cellIndex, cellCount);
    }

    public Span<ulong> UInt64PairsSlice(int cellIndex, int pairCount)
    {
        if (cellIndex < 0) throw new ArgumentOutOfRangeException(nameof(cellIndex));
        if (pairCount < 0) throw new ArgumentOutOfRangeException(nameof(pairCount));
        if ((cellIndex & 1) != 0)
            throw new ArgumentException("64-bit pair views must start at an even cell index.", nameof(cellIndex));

        return MemoryMarshal.Cast<byte, ulong>(_memory).Slice(cellIndex / 2, pairCount);
    }
}
