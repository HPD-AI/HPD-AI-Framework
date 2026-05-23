namespace Helium.Hastlayer;

/// <summary>
/// Minimal 32-bit cell memory contract used by kernel core logic.
/// Hastlayer entry points still use concrete memory types; this interface keeps
/// the arithmetic shared between software tests and real SimpleMemory adapters.
/// </summary>
public interface ICellMemory32
{
    int CellCount { get; }
    void WriteUInt32(int cellIndex, uint value);
    uint ReadUInt32(int cellIndex);
    void WriteInt32(int cellIndex, int value);
    int ReadInt32(int cellIndex);
}
