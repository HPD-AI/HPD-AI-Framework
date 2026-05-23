using Helium.Hardware;

namespace Helium.Hardware.Tests;

public class HardwareLinearAlgebraTests
{
    [Fact]
    public void LuDecompose_ComputesInPlaceFiniteFieldLu()
    {
        ulong[] matrix = [2, 1, 5, 7];

        HardwareLinearAlgebra.LuDecompose(matrix, 2, 17);

        Assert.Equal([2UL, 1UL, 11UL, 13UL], matrix);
    }

    [Fact]
    public void Solve_ComputesFiniteFieldSolutionWithCallerProvidedWorkspace()
    {
        ulong[] matrix = [2, 1, 5, 7];
        ulong[] rhs = [11, 13];
        ulong[] x = [0, 0];
        ulong[] work = new ulong[HardwareLinearAlgebra.RequiredSolveWorkLength(2)];

        HardwareLinearAlgebra.Solve(matrix, rhs, x, 2, 17, work);

        Assert.Equal([9UL, 10UL], x);
    }

    [Fact]
    public void Solve_DoesNotMutateInputMatrix()
    {
        ulong[] matrix = [2, 1, 5, 7];
        ulong[] rhs = [11, 13];
        ulong[] x = [0, 0];
        ulong[] work = new ulong[HardwareLinearAlgebra.RequiredSolveWorkLength(2)];

        HardwareLinearAlgebra.Solve(matrix, rhs, x, 2, 17, work);

        Assert.Equal([2UL, 1UL, 5UL, 7UL], matrix);
    }

    [Fact]
    public void Solve_InsufficientWorkspace_Throws()
    {
        ulong[] matrix = [2, 1, 5, 7];
        ulong[] rhs = [11, 13];
        ulong[] x = [0, 0];
        ulong[] work = new ulong[HardwareLinearAlgebra.RequiredSolveWorkLength(2) - 1];

        Assert.Throws<ArgumentException>(() =>
            HardwareLinearAlgebra.Solve(matrix, rhs, x, 2, 17, work));
    }

    [Fact]
    public void LuDecompose_ZeroPivot_ThrowsForHostValidation()
    {
        ulong[] matrix = [0, 1, 5, 7];

        Assert.Throws<ArgumentException>(() => HardwareLinearAlgebra.LuDecompose(matrix, 2, 17));
    }
}
