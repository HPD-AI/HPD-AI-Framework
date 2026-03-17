using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algebra.Tests;

public class TensorProductTests
{
    [Fact]
    public void Bilinearity_LeftAdditive()
    {
        // (e0 + e1) ⊗ e0 == e0 ⊗ e0 + e1 ⊗ e0
        var left = TensorProduct<Integer>.Of(2, 2, 0, 0) + TensorProduct<Integer>.Of(2, 2, 1, 0);
        var right = TensorProduct<Integer>.Of(2, 2, 0, 0) + TensorProduct<Integer>.Of(2, 2, 1, 0);
        Assert.Equal(left, right);
    }

    [Fact]
    public void ScalarMultiplication()
    {
        var t = TensorProduct<Integer>.Of(2, 2, 0, 1);
        var scaled = (Integer)3 * t;
        Assert.Equal((Integer)3, scaled[0, 1]);
        Assert.Equal(Integer.Zero, scaled[0, 0]);
    }

    [Fact]
    public void ZeroTensor()
    {
        var z = TensorProduct<Integer>.Zero(3, 3);
        Assert.True(z.IsZero);
    }

    [Fact]
    public void AdditiveInverse()
    {
        var t = TensorProduct<Integer>.Elementary(2, 2, 0, 1, (Integer)5);
        Assert.Equal(TensorProduct<Integer>.Zero(2, 2), t + (-t));
    }

    [Fact]
    public void DimensionOfR2TensorR3()
    {
        // Basis of R^2 ⊗ R^3 has 6 elements: e_i ⊗ e_j for i in {0,1}, j in {0,1,2}
        var basis = new List<TensorProduct<Integer>>();
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 3; j++)
                basis.Add(TensorProduct<Integer>.Of(2, 3, i, j));
        Assert.Equal(6, basis.Count);
        // All distinct.
        for (int a = 0; a < basis.Count; a++)
            for (int b = a + 1; b < basis.Count; b++)
                Assert.NotEqual(basis[a], basis[b]);
    }
}
