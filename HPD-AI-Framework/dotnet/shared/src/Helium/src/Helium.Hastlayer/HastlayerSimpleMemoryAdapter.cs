using Hast.Transformer.SimpleMemory;

namespace Helium.Hastlayer;

public sealed class HastlayerSimpleMemoryAdapter : ICellMemory32
{
    private readonly SimpleMemory _memory;

    public HastlayerSimpleMemoryAdapter(SimpleMemory memory) => _memory = memory;

    public int CellCount => _memory.CellCount;

    public void WriteUInt32(int cellIndex, uint value) => _memory.WriteUInt32(cellIndex, value);

    public uint ReadUInt32(int cellIndex) => _memory.ReadUInt32(cellIndex);

    public void WriteInt32(int cellIndex, int value) => _memory.WriteInt32(cellIndex, value);

    public int ReadInt32(int cellIndex) => _memory.ReadInt32(cellIndex);
}

public sealed class HastlayerSoftwareMemoryFactory : ICellMemory32Factory
{
    public static HastlayerSoftwareMemoryFactory Instance { get; } = new();

    private HastlayerSoftwareMemoryFactory() { }

    public ICellMemory32 Create(int cellCount) =>
        new HastlayerSimpleMemoryAdapter(SimpleMemory.CreateSoftwareMemory(cellCount));
}
