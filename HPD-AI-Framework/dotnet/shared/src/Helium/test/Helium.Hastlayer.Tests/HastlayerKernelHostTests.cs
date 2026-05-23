using Helium.Hardware;
using Helium.Hastlayer;

namespace Helium.Hastlayer.Tests;

public class HastlayerKernelHostTests
{
    [Fact]
    public void RunHello_UsesRequiredCellCount()
    {
        var factory = new RecordingMemoryFactory();
        var host = new HastlayerKernelHost(factory);

        var result = host.RunHello(8);

        Assert.Equal(9U, result);
        Assert.Equal([HelloKernel.RequiredCellCount()], factory.RequestedCellCounts);
    }

    [Fact]
    public void RunFixedPointMatVec_ComputesRowsAndUsesRequiredCellCount()
    {
        var factory = new RecordingMemoryFactory();
        var host = new HastlayerKernelHost(factory);
        var one = Fix64.FromDouble(1);
        var two = Fix64.FromDouble(2);
        var three = Fix64.FromDouble(3);
        var four = Fix64.FromDouble(4);
        var five = Fix64.FromDouble(5);
        var six = Fix64.FromDouble(6);

        var result = host.RunFixedPointMatVec(2, 2, [one, two, three, four], [five, six]);

        Assert.Equal(17.0, result[0].ToDouble(), precision: 10);
        Assert.Equal(39.0, result[1].ToDouble(), precision: 10);
        Assert.Equal([FixedPointMatVecKernel.RequiredCellCount(2, 2)], factory.RequestedCellCounts);
    }

    [Fact]
    public void RunRnsPolyMul_ComputesConvolutionAndUsesRequiredCellCount()
    {
        var factory = new RecordingMemoryFactory();
        var host = new HastlayerKernelHost(factory);

        var result = host.RunRnsPolyMul([1, 2, 3], [4, 5, 6], prime: 17);

        Assert.Equal<uint>([4, 13, 11, 10, 1], result);
        Assert.Equal([RnsPolyMulKernel.RequiredCellCount(3)], factory.RequestedCellCounts);
    }

    [Fact]
    public void RunRnsNttPolyMul_ComputesNttConvolutionAndUsesRequiredCellCount()
    {
        var factory = new RecordingMemoryFactory();
        var host = new HastlayerKernelHost(factory);
        var root = (uint)Ntt.RootForLength(3UL, 4, NttPrimes.Ntt998);

        var result = host.RunRnsNttPolyMul([1, 2, 0, 0], [3, 4, 0, 0], (uint)NttPrimes.Ntt998, root);

        Assert.Equal<uint>([3, 10, 8, 0], result);
        Assert.Equal([RnsNttPolyMulKernel.RequiredCellCount(4)], factory.RequestedCellCounts);
    }

    [Fact]
    public void RunGoldilocksPolyMul_ComputesConvolutionAndUsesRequiredCellCount()
    {
        var factory = new RecordingMemoryFactory();
        var host = new HastlayerKernelHost(factory);

        var result = host.RunGoldilocksPolyMul(
            [GoldilocksPolyMulKernel.Prime - 1, 2],
            [GoldilocksPolyMulKernel.Prime - 1, 3]);

        Assert.Equal<ulong>([1, GoldilocksPolyMulKernel.Prime - 5, 6], result);
        Assert.Equal([GoldilocksPolyMulKernel.RequiredCellCount(2)], factory.RequestedCellCounts);
    }

    [Fact]
    public void RunGoldilocksNttPolyMul_ComputesNttConvolutionAndUsesRequiredCellCount()
    {
        var factory = new RecordingMemoryFactory();
        var host = new HastlayerKernelHost(factory);

        var result = host.RunGoldilocksNttPolyMul([1, 2, 0, 0], [3, 4, 0, 0]);

        Assert.Equal<ulong>([3, 10, 8, 0], result);
        Assert.Equal([GoldilocksNttPolyMulKernel.RequiredCellCount(4)], factory.RequestedCellCounts);
    }

    [Fact]
    public void HostValidation_RejectsInvalidInputs()
    {
        var host = new HastlayerKernelHost();

        Assert.Throws<ArgumentException>(() => host.RunRnsPolyMul([1], [1, 2], 17));
        Assert.Throws<ArgumentOutOfRangeException>(() => host.RunRnsPolyMul([1], [1], 0));
        Assert.Throws<ArgumentException>(() => host.RunRnsNttPolyMul([1], [1, 2], 17, 1));
        Assert.Throws<ArgumentException>(() => host.RunRnsNttPolyMul([1, 2, 3], [1, 2, 3], 17, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => host.RunRnsNttPolyMul([1], [1], 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => host.RunRnsNttPolyMul([1], [1], 17, 0));
        Assert.Throws<ArgumentException>(() => host.RunGoldilocksPolyMul([1], [1, 2]));
        Assert.Throws<ArgumentException>(() => host.RunGoldilocksNttPolyMul([1], [1, 2]));
        Assert.Throws<ArgumentException>(() => host.RunGoldilocksNttPolyMul([1, 2, 3], [1, 2, 3]));
        Assert.Throws<ArgumentException>(() => host.RunFixedPointMatVec(1, 2, [Fix64.One], [Fix64.One, Fix64.One]));
        Assert.Throws<ArgumentException>(() => host.RunFixedPointMatVec(1, 2, [Fix64.One, Fix64.One], [Fix64.One]));
    }

    private sealed class RecordingMemoryFactory : ICellMemory32Factory
    {
        private readonly List<int> _requestedCellCounts = [];

        public IReadOnlyList<int> RequestedCellCounts => _requestedCellCounts;

        public ICellMemory32 Create(int cellCount)
        {
            _requestedCellCounts.Add(cellCount);
            return SimpleMemory32.CreateSoftwareMemory(cellCount);
        }
    }
}
