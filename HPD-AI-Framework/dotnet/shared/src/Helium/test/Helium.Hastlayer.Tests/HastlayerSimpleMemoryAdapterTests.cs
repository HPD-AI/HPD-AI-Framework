using Hast.Transformer.SimpleMemory;
using Helium.Hardware;
using Helium.Hastlayer;

namespace Helium.Hastlayer.Tests;

public class HastlayerSimpleMemoryAdapterTests
{
    [Fact]
    public void Adapter_RoundTripsThroughRealSimpleMemory()
    {
        var simpleMemory = SimpleMemory.CreateSoftwareMemory(2);
        ICellMemory32 memory = new HastlayerSimpleMemoryAdapter(simpleMemory);

        memory.WriteUInt32(0, 0xAABBCCDDU);
        memory.WriteInt32(1, -42);

        Assert.Equal(0xAABBCCDDU, simpleMemory.ReadUInt32(0));
        Assert.Equal(-42, simpleMemory.ReadInt32(1));
        Assert.Equal(2, memory.CellCount);
    }

    [Fact]
    public void RealSimpleMemoryKernelEntryPoint_ExecutesHelloKernel()
    {
        var memory = SimpleMemory.CreateSoftwareMemory(HelloKernel.RequiredCellCount());
        memory.WriteUInt32(HelloKernel.InputCell, 99);

        new HelloKernel().Execute(memory);

        Assert.Equal(100U, memory.ReadUInt32(HelloKernel.OutputCell));
    }

    [Fact]
    public void RealSimpleMemoryKernelEntryPoint_ExecutesFixedPointMatVecKernel()
    {
        var rows = 1;
        var cols = 2;
        var memory = SimpleMemory.CreateSoftwareMemory(FixedPointMatVecKernel.RequiredCellCount(rows, cols));
        memory.WriteUInt32(FixedPointMatVecKernel.RowsCell, (uint)rows);
        memory.WriteUInt32(FixedPointMatVecKernel.ColsCell, (uint)cols);

        var matrixStart = FixedPointMatVecKernel.PayloadStartCell;
        var vectorStart = matrixStart + (rows * cols * 2);
        var resultStart = vectorStart + (cols * 2);

        WriteFix64(memory, matrixStart, Fix64.FromDouble(-1.25));
        WriteFix64(memory, matrixStart + 2, Fix64.FromDouble(2.5));
        WriteFix64(memory, vectorStart, Fix64.FromDouble(4.0));
        WriteFix64(memory, vectorStart + 2, Fix64.FromDouble(-0.5));

        new FixedPointMatVecKernel().Execute(memory);

        Assert.Equal(-6.25, ReadFix64(memory, resultStart).ToDouble(), precision: 10);
    }

    [Fact]
    public void RealSimpleMemoryKernelEntryPoint_ExecutesRnsPolyMulKernel()
    {
        var memory = SimpleMemory.CreateSoftwareMemory(RnsPolyMulKernel.RequiredCellCount(2));
        memory.WriteUInt32(RnsPolyMulKernel.LengthCell, 2);
        memory.WriteUInt32(RnsPolyMulKernel.PrimeCell, 17);
        memory.WriteUInt32(RnsPolyMulKernel.PayloadStartCell, 3);
        memory.WriteUInt32(RnsPolyMulKernel.PayloadStartCell + 1, 4);
        memory.WriteUInt32(RnsPolyMulKernel.PayloadStartCell + 2, 5);
        memory.WriteUInt32(RnsPolyMulKernel.PayloadStartCell + 3, 6);

        new RnsPolyMulKernel().Execute(memory);

        var resultStart = RnsPolyMulKernel.PayloadStartCell + 4;
        Assert.Equal(15U, memory.ReadUInt32(resultStart));
        Assert.Equal(4U, memory.ReadUInt32(resultStart + 1));
        Assert.Equal(7U, memory.ReadUInt32(resultStart + 2));
    }

    [Fact]
    public void RealSimpleMemoryKernelEntryPoint_ExecutesRnsNttPolyMulKernel()
    {
        var memory = SimpleMemory.CreateSoftwareMemory(RnsNttPolyMulKernel.RequiredCellCount(4));
        var root = (uint)Ntt.RootForLength(3UL, 4, NttPrimes.Ntt998);
        memory.WriteUInt32(RnsNttPolyMulKernel.LengthCell, 4);
        memory.WriteUInt32(RnsNttPolyMulKernel.PrimeCell, (uint)NttPrimes.Ntt998);
        memory.WriteUInt32(RnsNttPolyMulKernel.RootCell, root);
        memory.WriteUInt32(RnsNttPolyMulKernel.PayloadStartCell, 1);
        memory.WriteUInt32(RnsNttPolyMulKernel.PayloadStartCell + 1, 2);
        memory.WriteUInt32(RnsNttPolyMulKernel.PayloadStartCell + 4, 3);
        memory.WriteUInt32(RnsNttPolyMulKernel.PayloadStartCell + 5, 4);

        new RnsNttPolyMulKernel().Execute(memory);

        var resultStart = RnsNttPolyMulKernel.PayloadStartCell + 8;
        Assert.Equal(3U, memory.ReadUInt32(resultStart));
        Assert.Equal(10U, memory.ReadUInt32(resultStart + 1));
        Assert.Equal(8U, memory.ReadUInt32(resultStart + 2));
        Assert.Equal(0U, memory.ReadUInt32(resultStart + 3));
    }

    [Fact]
    public void RealSimpleMemoryKernelEntryPoint_ExecutesGoldilocksPolyMulKernel()
    {
        var memory = SimpleMemory.CreateSoftwareMemory(GoldilocksPolyMulKernel.RequiredCellCount(2));
        memory.WriteUInt32(GoldilocksPolyMulKernel.LengthCell, 2);
        WriteUInt64(memory, GoldilocksPolyMulKernel.PayloadStartCell, GoldilocksPolyMulKernel.Prime - 1);
        WriteUInt64(memory, GoldilocksPolyMulKernel.PayloadStartCell + 2, 2);
        WriteUInt64(memory, GoldilocksPolyMulKernel.PayloadStartCell + 4, GoldilocksPolyMulKernel.Prime - 1);
        WriteUInt64(memory, GoldilocksPolyMulKernel.PayloadStartCell + 6, 3);

        new GoldilocksPolyMulKernel().Execute(memory);

        var resultStart = GoldilocksPolyMulKernel.PayloadStartCell + 8;
        Assert.Equal(1UL, ReadUInt64(memory, resultStart));
        Assert.Equal(GoldilocksPolyMulKernel.Prime - 5, ReadUInt64(memory, resultStart + 2));
        Assert.Equal(6UL, ReadUInt64(memory, resultStart + 4));
    }

    [Fact]
    public void RealSimpleMemoryKernelEntryPoint_ExecutesGoldilocksNttPolyMulKernel()
    {
        var memory = SimpleMemory.CreateSoftwareMemory(GoldilocksNttPolyMulKernel.RequiredCellCount(4));
        memory.WriteUInt32(GoldilocksNttPolyMulKernel.LengthCell, 4);
        WriteUInt64(memory, GoldilocksNttPolyMulKernel.PayloadStartCell, 1);
        WriteUInt64(memory, GoldilocksNttPolyMulKernel.PayloadStartCell + 2, 2);
        WriteUInt64(memory, GoldilocksNttPolyMulKernel.PayloadStartCell + 8, 3);
        WriteUInt64(memory, GoldilocksNttPolyMulKernel.PayloadStartCell + 10, 4);

        new GoldilocksNttPolyMulKernel().Execute(memory);

        var resultStart = GoldilocksNttPolyMulKernel.PayloadStartCell + 16;
        Assert.Equal(3UL, ReadUInt64(memory, resultStart));
        Assert.Equal(10UL, ReadUInt64(memory, resultStart + 2));
        Assert.Equal(8UL, ReadUInt64(memory, resultStart + 4));
        Assert.Equal(0UL, ReadUInt64(memory, resultStart + 6));
    }

    [Fact]
    public void RealSimpleMemoryFactory_WorksWithHostRunner()
    {
        var host = new HastlayerKernelHost(HastlayerSoftwareMemoryFactory.Instance);

        var result = host.RunRnsPolyMul([1, 2], [3, 4], prime: 17);

        Assert.Equal<uint>([3, 10, 8], result);
    }

    private static void WriteFix64(SimpleMemory memory, int cellIndex, Fix64 value) =>
        WriteInt64(memory, cellIndex, value.RawBits);

    private static Fix64 ReadFix64(SimpleMemory memory, int cellIndex) =>
        Fix64.FromRawBits(ReadInt64(memory, cellIndex));

    private static void WriteInt64(SimpleMemory memory, int cellIndex, long value) =>
        WriteUInt64(memory, cellIndex, unchecked((ulong)value));

    private static long ReadInt64(SimpleMemory memory, int cellIndex) =>
        unchecked((long)ReadUInt64(memory, cellIndex));

    private static void WriteUInt64(SimpleMemory memory, int cellIndex, ulong value)
    {
        memory.WriteUInt32(cellIndex, unchecked((uint)value));
        memory.WriteUInt32(cellIndex + 1, unchecked((uint)(value >> 32)));
    }

    private static ulong ReadUInt64(SimpleMemory memory, int cellIndex)
    {
        var low = memory.ReadUInt32(cellIndex);
        var high = memory.ReadUInt32(cellIndex + 1);
        return ((ulong)high << 32) | low;
    }
}
