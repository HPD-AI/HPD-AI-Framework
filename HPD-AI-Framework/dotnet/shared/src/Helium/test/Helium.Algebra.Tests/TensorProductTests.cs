using Helium.Algebra;
using Helium.Primitives;

namespace Helium.Algebra.Tests;

public class TensorProductTests
{
    [Fact]
    public void ElementaryTensor_StoresCoefficientByBasisPair()
    {
        var left = new Fin(0, 2);
        var right = new Fin(1, 3);
        var t = TensorProduct<Integer, Fin, Fin>.Elementary(left, right, (Integer)5);

        Assert.Equal((Integer)5, t.Coefficient(left, right));
        Assert.Equal(Integer.Zero, t.Coefficient(new Fin(1, 2), right));
    }

    [Fact]
    public void Addition_CombinesMatchingBasisPairs()
    {
        var left = new Fin(0, 2);
        var right = new Fin(1, 3);

        var a = TensorProduct<Integer, Fin, Fin>.Elementary(left, right, (Integer)2);
        var b = TensorProduct<Integer, Fin, Fin>.Elementary(left, right, (Integer)3);
        var result = a + b;

        Assert.Equal((Integer)5, result.Coefficient(left, right));
    }

    [Fact]
    public void ScalarMultiplication_ScalesAllCoefficients()
    {
        var first = TensorProduct<Integer, Fin, Fin>.Elementary(new Fin(0, 2), new Fin(0, 3), (Integer)2);
        var second = TensorProduct<Integer, Fin, Fin>.Elementary(new Fin(1, 2), new Fin(2, 3), (Integer)4);
        var scaled = (Integer)3 * (first + second);

        Assert.Equal((Integer)6, scaled.Coefficient(new Fin(0, 2), new Fin(0, 3)));
        Assert.Equal((Integer)12, scaled.Coefficient(new Fin(1, 2), new Fin(2, 3)));
    }

    [Fact]
    public void AdditiveInverse_Cancels()
    {
        var t = TensorProduct<Integer, Fin, Fin>.Elementary(new Fin(0, 2), new Fin(1, 3), (Integer)5);

        Assert.Equal(TensorProduct<Integer, Fin, Fin>.Zero, t + (-t));
        Assert.True((t + (-t)).IsZero);
    }

    [Fact]
    public void Components_AreSortedByBasisOrder()
    {
        var a = TensorProduct<Integer, Fin, Fin>.Elementary(new Fin(1, 2), new Fin(2, 3), (Integer)7);
        var b = TensorProduct<Integer, Fin, Fin>.Elementary(new Fin(0, 2), new Fin(1, 3), (Integer)5);
        var components = (a + b).Components.ToArray();

        Assert.Equal(new Fin(0, 2), components[0].Left);
        Assert.Equal(new Fin(1, 3), components[0].Right);
        Assert.Equal((Integer)5, components[0].Coefficient);
        Assert.Equal(new Fin(1, 2), components[1].Left);
        Assert.Equal(new Fin(2, 3), components[1].Right);
        Assert.Equal((Integer)7, components[1].Coefficient);
    }

    [Fact]
    public void TensorProduct_HasFreeModuleBasisCardinality()
    {
        var basis = new List<TensorProduct<Integer, Fin, Fin>>();
        for (int i = 0; i < 2; i++)
        for (int j = 0; j < 3; j++)
            basis.Add(TensorProduct<Integer, Fin, Fin>.Elementary(new Fin(i, 2), new Fin(j, 3), Integer.One));

        Assert.Equal(6, basis.Count);
        for (int a = 0; a < basis.Count; a++)
        for (int b = a + 1; b < basis.Count; b++)
            Assert.NotEqual(basis[a], basis[b]);
    }

    [Fact]
    public void LegacyTensorProductArity_IsNotPublic()
    {
        var oldShape = typeof(TensorProduct<,,>).Assembly.GetExportedTypes()
            .Where(type => type.Name == "TensorProduct`1")
            .ToArray();

        Assert.Empty(oldShape);
    }
}
