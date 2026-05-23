using System.Runtime.InteropServices;

namespace Helium.Hastlayer;

/// <summary>
/// Minimal 32-bit cell memory model matching the relevant Hastlayer SimpleMemory semantics.
/// This keeps Helium kernels software-testable until the complete Hastlayer SDK checkout is available.
/// </summary>
public sealed class SimpleMemory32 : ICellMemory32
{
    public const int MemoryCellSizeBytes = sizeof(int);

    private readonly byte[] _memory;

    private SimpleMemory32(int cellCount)
    {
        if (cellCount < 0) throw new ArgumentOutOfRangeException(nameof(cellCount));
        _memory = new byte[checked(cellCount * MemoryCellSizeBytes)];
    }

    public int ByteCount => _memory.Length;
    public int CellCount => _memory.Length / MemoryCellSizeBytes;

    private Span<byte> this[int cellIndex]
    {
        get
        {
            if ((uint)cellIndex >= (uint)CellCount)
                throw new ArgumentOutOfRangeException(nameof(cellIndex));
            return _memory.AsSpan(cellIndex * MemoryCellSizeBytes, MemoryCellSizeBytes);
        }
    }

    public static SimpleMemory32 CreateSoftwareMemory(int cellCount) => new(cellCount);

    public void WriteUInt32(int cellIndex, uint value) =>
        MemoryMarshal.Write(this[cellIndex], in value);

    public uint ReadUInt32(int cellIndex) =>
        MemoryMarshal.Read<uint>(this[cellIndex]);

    public void WriteInt32(int cellIndex, int value) =>
        MemoryMarshal.Write(this[cellIndex], in value);

    public int ReadInt32(int cellIndex) =>
        MemoryMarshal.Read<int>(this[cellIndex]);

    public Memory<byte> GetLiveMemory() => _memory;
}
