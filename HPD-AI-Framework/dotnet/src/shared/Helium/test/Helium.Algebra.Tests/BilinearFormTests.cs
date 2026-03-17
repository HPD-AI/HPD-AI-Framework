using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algebra.Tests;

public class BilinearFormTests
{
    [Fact]
    public void StandardDotProduct()
    {
        var B = BilinearForm<Integer>.Standard(3);
        var v = Vector<Integer>.FromArray((Integer)1, (Integer)2, (Integer)3);
        var w = Vector<Integer>.FromArray((Integer)4, (Integer)5, (Integer)6);
        // B(v,w) = 1*4 + 2*5 + 3*6 = 32
        Assert.Equal((Integer)32, B.Apply(v, w));
    }

    [Fact]
    public void Bilinearity_Left()
    {
        var B = BilinearForm<Integer>.Standard(2);
        var x = Vector<Integer>.FromArray((Integer)1, (Integer)2);
        var y = Vector<Integer>.FromArray((Integer)3, (Integer)4);
        var z = Vector<Integer>.FromArray((Integer)5, (Integer)6);
        // B(x + y, z) == B(x, z) + B(y, z)
        Assert.Equal(B.Apply(x + y, z), B.Apply(x, z) + B.Apply(y, z));
    }

    [Fact]
    public void Bilinearity_Right()
    {
        var B = BilinearForm<Integer>.Standard(2);
        var x = Vector<Integer>.FromArray((Integer)1, (Integer)2);
        var y = Vector<Integer>.FromArray((Integer)3, (Integer)4);
        var z = Vector<Integer>.FromArray((Integer)5, (Integer)6);
        // B(x, y + z) == B(x, y) + B(x, z)
        Assert.Equal(B.Apply(x, y + z), B.Apply(x, y) + B.Apply(x, z));
    }

    [Fact]
    public void Bilinearity_Scalar()
    {
        var B = BilinearForm<Integer>.Standard(2);
        var x = Vector<Integer>.FromArray((Integer)1, (Integer)2);
        var y = Vector<Integer>.FromArray((Integer)3, (Integer)4);
        Integer r = 5;
        // B(r*x, y) == r * B(x, y)
        Assert.Equal(B.Apply(r * x, y), r * B.Apply(x, y));
    }

    [Fact]
    public void SymmetryOfStandard()
    {
        var B = BilinearForm<Integer>.Standard(3);
        Assert.True(B.IsSymmetric);
        var v = Vector<Integer>.FromArray((Integer)1, (Integer)2, (Integer)3);
        var w = Vector<Integer>.FromArray((Integer)4, (Integer)5, (Integer)6);
        Assert.Equal(B.Apply(v, w), B.Apply(w, v));
    }

    [Fact]
    public void QuadraticForm()
    {
        var B = BilinearForm<Integer>.Standard(2);
        var v = Vector<Integer>.FromArray((Integer)3, (Integer)4);
        // Q(v) = B(v,v) = 9 + 16 = 25
        Assert.Equal((Integer)25, B.Quadratic(v));
    }

    [Fact]
    public void GramMatrixRecovery()
    {
        var G = Matrix<Integer>.FromArray(2, 2, [(Integer)2, (Integer)1, (Integer)1, (Integer)3]);
        var B = BilinearForm<Integer>.FromGramMatrix(G);
        Assert.Equal(G, B.GramMatrix);
    }
}
