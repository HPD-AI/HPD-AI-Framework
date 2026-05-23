using Helium.Hastlayer;

namespace Helium.Hastlayer.Tests;

public class AllocationTests
{
    [Fact]
    public void SimpleMemoryLayout_ReadWriteUInt64_AllocatesZeroBytes()
    {
        ICellMemory32 memory = SimpleMemory32.CreateSoftwareMemory(4);
        SimpleMemoryLayout.WriteUInt64(memory, 0, 0x01020304AABBCCDDUL);
        _ = SimpleMemoryLayout.ReadUInt64(memory, 0);

        var before = GC.GetAllocatedBytesForCurrentThread();
        SimpleMemoryLayout.WriteUInt64(memory, 0, 0x01020304AABBCCDDUL);
        _ = SimpleMemoryLayout.ReadUInt64(memory, 0);
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void SimpleMemorySpanAdapter_Slicing_AllocatesZeroBytes()
    {
        var memory = SimpleMemory32.CreateSoftwareMemory(8);
        var adapter = new SimpleMemorySpanAdapter(memory);
        adapter.UInt32Slice(0, 4)[0] = 123U;
        adapter.UInt64PairsSlice(0, 2)[1] = 456UL;

        var before = GC.GetAllocatedBytesForCurrentThread();
        var uints = adapter.UInt32Slice(0, 4);
        var ulongs = adapter.UInt64PairsSlice(0, 2);
        uints[1] = 789U;
        ulongs[0] = 42UL;
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }
}
