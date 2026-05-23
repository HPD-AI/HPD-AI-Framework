using Helium.Hardware;
using Helium.Hastlayer;

namespace Helium.Hastlayer.Tests;

public class HastlayerKernelTests
{
    [Fact]
    public void SimpleMemoryLayout_UsesFourByteCells()
    {
        Assert.Equal(4, SimpleMemoryLayout.CellSizeBytes);
        Assert.Equal(SimpleMemory32.MemoryCellSizeBytes, SimpleMemoryLayout.CellSizeBytes);
    }

    [Fact]
    public void SimpleMemoryLayout_UInt64_RoundTripsLowWordFirst()
    {
        var memory = SimpleMemory32.CreateSoftwareMemory(2);

        SimpleMemoryLayout.WriteUInt64(memory, 0, 0x11223344AABBCCDDUL);

        Assert.Equal(0xAABBCCDDU, memory.ReadUInt32(0));
        Assert.Equal(0x11223344U, memory.ReadUInt32(1));
        Assert.Equal(0x11223344AABBCCDDUL, SimpleMemoryLayout.ReadUInt64(memory, 0));
    }

    [Fact]
    public void SimpleMemoryLayout_Fix64_RoundTripsRawBits()
    {
        var memory = SimpleMemory32.CreateSoftwareMemory(2);
        var value = Fix64.FromRawBits(unchecked((long)0x88776655AABBCCDDUL));

        SimpleMemoryLayout.WriteFix64(memory, 0, value);

        Assert.Equal(value, SimpleMemoryLayout.ReadFix64(memory, 0));
    }

    [Fact]
    public void SimpleMemorySpanAdapter_UInt32View_IsLive()
    {
        var memory = SimpleMemory32.CreateSoftwareMemory(4);
        var adapter = new SimpleMemorySpanAdapter(memory);

        var cells = adapter.AsUInt32Span();
        cells[2] = 123U;

        Assert.Equal(123U, memory.ReadUInt32(2));
        memory.WriteUInt32(3, 456U);
        Assert.Equal(456U, cells[3]);
    }

    [Fact]
    public void SimpleMemorySpanAdapter_UInt64PairsView_IsLive()
    {
        var memory = SimpleMemory32.CreateSoftwareMemory(4);
        var adapter = new SimpleMemorySpanAdapter(memory);

        var pairs = adapter.UInt64PairsSlice(0, 2);
        pairs[1] = 0x11223344AABBCCDDUL;

        Assert.Equal(0xAABBCCDDU, memory.ReadUInt32(2));
        Assert.Equal(0x11223344U, memory.ReadUInt32(3));
        Assert.Throws<ArgumentException>(() => CreateMisalignedUInt64PairSlice(memory));
    }

    private static void CreateMisalignedUInt64PairSlice(SimpleMemory32 memory)
    {
        var adapter = new SimpleMemorySpanAdapter(memory);
        _ = adapter.UInt64PairsSlice(1, 1);
    }

    [Fact]
    public void HelloKernel_SoftwareExecution_AddsOne()
    {
        var kernel = new HelloKernel();

        Assert.Equal(42U, kernel.ExecuteSoftware(41U));
    }

    [Fact]
    public void FixedPointMatVecKernel_SoftwareExecution_ComputesRows()
    {
        var kernel = new FixedPointMatVecKernel();
        var half = Fix64.FromDouble(0.5);
        var two = Fix64.FromDouble(2.0);
        var three = Fix64.FromDouble(3.0);
        var four = Fix64.FromDouble(4.0);
        var five = Fix64.FromDouble(5.0);
        var six = Fix64.FromDouble(6.0);

        var result = kernel.ExecuteSoftware(
            rows: 2,
            cols: 2,
            matrix: [two, three, four, five],
            vector: [half, six]);

        Assert.Equal(2, result.Length);
        Assert.Equal(19.0, result[0].ToDouble(), precision: 10);
        Assert.Equal(32.0, result[1].ToDouble(), precision: 10);
    }

    [Fact]
    public void FixedPointMatVecKernel_SoftwareExecution_MatchesSignedFractionalFix64()
    {
        var kernel = new FixedPointMatVecKernel();
        var result = kernel.ExecuteSoftware(
            rows: 1,
            cols: 2,
            matrix: [Fix64.FromDouble(-1.25), Fix64.FromDouble(2.5)],
            vector: [Fix64.FromDouble(4.0), Fix64.FromDouble(-0.5)]);

        Assert.Single(result);
        Assert.Equal(-6.25, result[0].ToDouble(), precision: 10);
    }

    [Fact]
    public void RnsPolyMulKernel_SoftwareExecution_ComputesModularConvolution()
    {
        var kernel = new RnsPolyMulKernel();

        var result = kernel.ExecuteSoftware([1, 2, 3], [4, 5, 6], prime: 17);

        Assert.Equal<uint>([4, 13, 11, 10, 1], result);
    }

    [Fact]
    public void RnsPolyMulKernel_UsesDocumentedMemoryLayout()
    {
        var memory = SimpleMemory32.CreateSoftwareMemory(12);
        memory.WriteUInt32(RnsPolyMulKernel.LengthCell, 2);
        memory.WriteUInt32(RnsPolyMulKernel.PrimeCell, 17);
        memory.WriteUInt32(RnsPolyMulKernel.ReservedCell, 0);
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
    public void RnsNttPolyMulKernel_SoftwareExecution_ComputesNttConvolution()
    {
        var kernel = new RnsNttPolyMulKernel();
        var root = (uint)Ntt.RootForLength(3UL, 4, NttPrimes.Ntt998);

        var result = kernel.ExecuteSoftware([1, 2, 0, 0], [3, 4, 0, 0], (uint)NttPrimes.Ntt998, root);

        Assert.Equal<uint>([3, 10, 8, 0], result);
    }

    [Fact]
    public void RnsNttPolyMulKernel_UsesDocumentedMemoryLayout()
    {
        var memory = SimpleMemory32.CreateSoftwareMemory(RnsNttPolyMulKernel.RequiredCellCount(4));
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
    public void GoldilocksPolyMulKernel_SoftwareExecution_ComputesModularConvolution()
    {
        var kernel = new GoldilocksPolyMulKernel();

        var result = kernel.ExecuteSoftware(
            [GoldilocksPolyMulKernel.Prime - 1, 2],
            [GoldilocksPolyMulKernel.Prime - 1, 3]);

        Assert.Equal<ulong>([1, GoldilocksPolyMulKernel.Prime - 5, 6], result);
    }

    [Fact]
    public void GoldilocksPolyMulKernel_SoftwareExecution_ReducesOverflowingCoefficientSums()
    {
        var kernel = new GoldilocksPolyMulKernel();

        var result = kernel.ExecuteSoftware(
            [GoldilocksPolyMulKernel.Prime - 1, GoldilocksPolyMulKernel.Prime - 1],
            [1, 1]);

        Assert.Equal<ulong>(
            [GoldilocksPolyMulKernel.Prime - 1, GoldilocksPolyMulKernel.Prime - 2, GoldilocksPolyMulKernel.Prime - 1],
            result);
    }

    [Fact]
    public void GoldilocksPolyMulKernel_UsesDocumentedTwoCellCoefficientLayout()
    {
        var memory = SimpleMemory32.CreateSoftwareMemory(17);
        memory.WriteUInt32(GoldilocksPolyMulKernel.LengthCell, 2);
        SimpleMemoryLayout.WriteUInt64(memory, GoldilocksPolyMulKernel.PayloadStartCell, 2);
        SimpleMemoryLayout.WriteUInt64(memory, GoldilocksPolyMulKernel.PayloadStartCell + 2, 3);
        SimpleMemoryLayout.WriteUInt64(memory, GoldilocksPolyMulKernel.PayloadStartCell + 4, 5);
        SimpleMemoryLayout.WriteUInt64(memory, GoldilocksPolyMulKernel.PayloadStartCell + 6, 7);

        new GoldilocksPolyMulKernel().Execute(memory);

        var resultStart = GoldilocksPolyMulKernel.PayloadStartCell + 8;
        Assert.Equal(10UL, SimpleMemoryLayout.ReadUInt64(memory, resultStart));
        Assert.Equal(29UL, SimpleMemoryLayout.ReadUInt64(memory, resultStart + 2));
        Assert.Equal(21UL, SimpleMemoryLayout.ReadUInt64(memory, resultStart + 4));
    }

    [Fact]
    public void GoldilocksNttPolyMulKernel_SoftwareExecution_ComputesNttConvolution()
    {
        var kernel = new GoldilocksNttPolyMulKernel();

        var result = kernel.ExecuteSoftware([1, 2, 0, 0], [3, 4, 0, 0]);

        Assert.Equal<ulong>([3, 10, 8, 0], result);
    }

    [Fact]
    public void GoldilocksNttPolyMulKernel_SoftwareExecution_ReducesModularProducts()
    {
        var kernel = new GoldilocksNttPolyMulKernel();

        var result = kernel.ExecuteSoftware(
            [GoldilocksNttPolyMulKernel.Prime - 1, 2, 0, 0],
            [GoldilocksNttPolyMulKernel.Prime - 1, 3, 0, 0]);

        Assert.Equal<ulong>([1, GoldilocksNttPolyMulKernel.Prime - 5, 6, 0], result);
    }

    [Fact]
    public void GoldilocksNttPolyMulKernel_UsesDocumentedTwoCellCoefficientLayout()
    {
        var memory = SimpleMemory32.CreateSoftwareMemory(GoldilocksNttPolyMulKernel.RequiredCellCount(4));
        memory.WriteUInt32(GoldilocksNttPolyMulKernel.LengthCell, 4);
        SimpleMemoryLayout.WriteUInt64(memory, GoldilocksNttPolyMulKernel.PayloadStartCell, 1);
        SimpleMemoryLayout.WriteUInt64(memory, GoldilocksNttPolyMulKernel.PayloadStartCell + 2, 2);
        SimpleMemoryLayout.WriteUInt64(memory, GoldilocksNttPolyMulKernel.PayloadStartCell + 8, 3);
        SimpleMemoryLayout.WriteUInt64(memory, GoldilocksNttPolyMulKernel.PayloadStartCell + 10, 4);

        new GoldilocksNttPolyMulKernel().Execute(memory);

        var resultStart = GoldilocksNttPolyMulKernel.PayloadStartCell + 16;
        Assert.Equal(3UL, SimpleMemoryLayout.ReadUInt64(memory, resultStart));
        Assert.Equal(10UL, SimpleMemoryLayout.ReadUInt64(memory, resultStart + 2));
        Assert.Equal(8UL, SimpleMemoryLayout.ReadUInt64(memory, resultStart + 4));
        Assert.Equal(0UL, SimpleMemoryLayout.ReadUInt64(memory, resultStart + 6));
    }
}
