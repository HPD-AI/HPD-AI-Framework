using Helium.Hardware;

namespace Helium.Hastlayer;

/// <summary>
/// Cell layout helpers for Hastlayer SimpleMemory.
/// SimpleMemory cells are 32-bit. Multi-cell values are encoded little-endian:
/// low 32 bits first, then high 32 bits.
/// </summary>
public static class SimpleMemoryLayout
{
    public const int CellSizeBytes = SimpleMemory32.MemoryCellSizeBytes;

    public static void WriteUInt64(ICellMemory32 memory, int cellIndex, ulong value)
    {
        memory.WriteUInt32(cellIndex, unchecked((uint)value));
        memory.WriteUInt32(cellIndex + 1, unchecked((uint)(value >> 32)));
    }

    public static ulong ReadUInt64(ICellMemory32 memory, int cellIndex)
    {
        var low = memory.ReadUInt32(cellIndex);
        var high = memory.ReadUInt32(cellIndex + 1);
        return ((ulong)high << 32) | low;
    }

    public static void WriteInt64(ICellMemory32 memory, int cellIndex, long value) =>
        WriteUInt64(memory, cellIndex, unchecked((ulong)value));

    public static long ReadInt64(ICellMemory32 memory, int cellIndex) =>
        unchecked((long)ReadUInt64(memory, cellIndex));

    public static void WriteFix64(ICellMemory32 memory, int cellIndex, Fix64 value) =>
        WriteInt64(memory, cellIndex, value.RawBits);

    public static Fix64 ReadFix64(ICellMemory32 memory, int cellIndex) =>
        Fix64.FromRawBits(ReadInt64(memory, cellIndex));
}
