using Helium.Hastlayer;

namespace Helium.Hastlayer.Tests;

public class KernelLayoutTests
{
    [Fact]
    public void RequiredCellCount_HelloKernel()
    {
        Assert.Equal(2, HelloKernel.RequiredCellCount());
    }

    [Fact]
    public void RequiredCellCount_FixedPointMatVec()
    {
        // Header 2 + matrix 2*3*2 cells + vector 3*2 cells + result 2*2 cells.
        Assert.Equal(24, FixedPointMatVecKernel.RequiredCellCount(rows: 2, cols: 3));
    }

    [Fact]
    public void RequiredCellCount_RnsPolyMul()
    {
        // Header 3 + inputs 2*n + result (2*n - 1).
        Assert.Equal(14, RnsPolyMulKernel.RequiredCellCount(3));
        Assert.Equal(3, RnsPolyMulKernel.RequiredCellCount(0));
    }

    [Fact]
    public void RequiredCellCount_RnsNttPolyMul()
    {
        // Header 3 + A n cells + B n cells + result n cells.
        Assert.Equal(15, RnsNttPolyMulKernel.RequiredCellCount(4));
        Assert.Equal(3, RnsNttPolyMulKernel.RequiredCellCount(0));
    }

    [Fact]
    public void RequiredCellCount_GoldilocksPolyMul()
    {
        // Header 3 + inputs 4*n cells + result 2*(2*n - 1) cells.
        Assert.Equal(17, GoldilocksPolyMulKernel.RequiredCellCount(2));
        Assert.Equal(3, GoldilocksPolyMulKernel.RequiredCellCount(0));
    }

    [Fact]
    public void RequiredCellCount_GoldilocksNttPolyMul()
    {
        // Header 3 + A 2*n cells + B 2*n cells + result 2*n cells.
        Assert.Equal(27, GoldilocksNttPolyMulKernel.RequiredCellCount(4));
        Assert.Equal(3, GoldilocksNttPolyMulKernel.RequiredCellCount(0));
    }

    [Fact]
    public void RequiredCellCount_RejectsNegativeDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FixedPointMatVecKernel.RequiredCellCount(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => FixedPointMatVecKernel.RequiredCellCount(1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => RnsPolyMulKernel.RequiredCellCount(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => RnsNttPolyMulKernel.RequiredCellCount(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GoldilocksPolyMulKernel.RequiredCellCount(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GoldilocksNttPolyMulKernel.RequiredCellCount(-1));
    }
}
