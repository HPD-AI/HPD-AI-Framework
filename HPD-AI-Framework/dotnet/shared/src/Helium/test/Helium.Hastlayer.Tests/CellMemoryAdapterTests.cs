using Helium.Hastlayer;

namespace Helium.Hastlayer.Tests;

public class CellMemoryAdapterTests
{
    [Fact]
    public void KernelCore_CanRunOverICellMemory32()
    {
        ICellMemory32 memory = SimpleMemory32.CreateSoftwareMemory(HelloKernel.CellCount);
        memory.WriteUInt32(HelloKernel.InputCell, 10);

        HelloKernel.ExecuteCore(memory);

        Assert.Equal(11U, memory.ReadUInt32(HelloKernel.OutputCell));
    }

    [Fact]
    public void LayoutHelpers_CanRunOverICellMemory32()
    {
        ICellMemory32 memory = SimpleMemory32.CreateSoftwareMemory(2);

        SimpleMemoryLayout.WriteUInt64(memory, 0, 0x01020304AABBCCDDUL);

        Assert.Equal(0x01020304AABBCCDDUL, SimpleMemoryLayout.ReadUInt64(memory, 0));
    }
}
