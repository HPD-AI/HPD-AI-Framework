using Helium.Hardware;

namespace Helium.Hardware.Tests;

public class AllocationTests
{
    [Fact]
    public void Ntt_PolyMul_WithCallerWorkspace_AllocatesZeroBytes()
    {
        ulong[] a = [1, 2, 0, 0];
        ulong[] b = [3, 4, 0, 0];
        ulong[] result = new ulong[4];
        ulong[] work = new ulong[8];
        var root = Ntt.RootForLength(3UL, 4, NttPrimes.Ntt998);

        Ntt.PolyMul(a, b, result, work, NttPrimes.Ntt998, root);

        var before = GC.GetAllocatedBytesForCurrentThread();
        Ntt.PolyMul(a, b, result, work, NttPrimes.Ntt998, root);
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void Ntt_ForwardAndInverse_InPlace_AllocateZeroBytes()
    {
        ulong[] values = [1, 2, 3, 4, 5, 6, 7, 8];
        var root = Ntt.RootForLength(3UL, values.Length, NttPrimes.Ntt998);

        Ntt.Forward(values, NttPrimes.Ntt998, root);
        Ntt.Inverse(values, NttPrimes.Ntt998, root);

        var before = GC.GetAllocatedBytesForCurrentThread();
        Ntt.Forward(values, NttPrimes.Ntt998, root);
        Ntt.Inverse(values, NttPrimes.Ntt998, root);
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void HardwareMatrix_Reductions_AllocateZeroBytes()
    {
        using var a = DoubleMatrix.FromArray(1, 4, [3.0, 4.0, 12.0, 5.0]);
        using var b = DoubleMatrix.FromArray(1, 4, [2.0, 3.0, 4.0, 7.0]);

        _ = HardwareMatrixOps.Sum(a);
        _ = HardwareMatrixOps.Mean(a);
        _ = HardwareMatrixOps.Dot(a, b);
        _ = HardwareMatrixOps.Norm(a);

        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = HardwareMatrixOps.Sum(a);
        _ = HardwareMatrixOps.Mean(a);
        _ = HardwareMatrixOps.Dot(a, b);
        _ = HardwareMatrixOps.Norm(a);
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void LocalHardwareBackend_Reductions_AllocateZeroBytes()
    {
        var backend = new LocalHardwareBackend<double>();
        using var a = backend.CreateMatrix(1, 4, [3.0, 4.0, 12.0, 5.0]);
        using var b = backend.CreateMatrix(1, 4, [2.0, 3.0, 4.0, 7.0]);

        _ = backend.Sum(a);
        _ = backend.Mean(a);
        _ = backend.Dot(a, b);
        _ = backend.Norm(a);

        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = backend.Sum(a);
        _ = backend.Mean(a);
        _ = backend.Dot(a, b);
        _ = backend.Norm(a);
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void HardwareLinearAlgebra_Solve_WithCallerWorkspace_AllocatesZeroBytes()
    {
        ulong[] matrix = [2, 1, 5, 7];
        ulong[] rhs = [11, 13];
        ulong[] x = [0, 0];
        ulong[] work = new ulong[HardwareLinearAlgebra.RequiredSolveWorkLength(2)];

        HardwareLinearAlgebra.Solve(matrix, rhs, x, 2, 17, work);

        var before = GC.GetAllocatedBytesForCurrentThread();
        HardwareLinearAlgebra.Solve(matrix, rhs, x, 2, 17, work);
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }
}
