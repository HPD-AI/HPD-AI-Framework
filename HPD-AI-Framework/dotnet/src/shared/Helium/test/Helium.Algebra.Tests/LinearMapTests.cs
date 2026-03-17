using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algebra.Tests;

public class LinearMapTests
{
    [Fact]
    public void IdentityMapPreservesVector()
    {
        var id = LinearMap<Integer>.Identity(3);
        var v = Vector<Integer>.FromArray((Integer)1, (Integer)2, (Integer)3);
        Assert.Equal(v, id.Apply(v));
    }

    [Fact]
    public void ComposeWithIdentity()
    {
        var m = Matrix<Integer>.FromArray(2, 2, [(Integer)1, (Integer)2, (Integer)3, (Integer)4]);
        var f = LinearMap<Integer>.FromMatrix(m);
        var id = LinearMap<Integer>.Identity(2);
        Assert.Equal(f, f.Compose(id));
        Assert.Equal(f, id.Compose(f));
    }

    [Fact]
    public void CompositionCorrespondsToMatrixMultiplication()
    {
        var A = Matrix<Integer>.FromArray(2, 2, [(Integer)1, (Integer)2, (Integer)3, (Integer)4]);
        var B = Matrix<Integer>.FromArray(2, 2, [(Integer)5, (Integer)6, (Integer)7, (Integer)8]);
        var fA = LinearMap<Integer>.FromMatrix(A);
        var fB = LinearMap<Integer>.FromMatrix(B);

        var v = Vector<Integer>.FromArray((Integer)1, (Integer)0);

        // (A ∘ B)(v) = A(B(v))
        Assert.Equal(fA.Apply(fB.Apply(v)), fA.Compose(fB).Apply(v));
    }

    [Fact]
    public void ZeroMapSendsEverythingToZero()
    {
        var z = LinearMap<Integer>.Zero(2, 3);
        var v = Vector<Integer>.FromArray((Integer)5, (Integer)7);
        Assert.Equal(Vector<Integer>.Zero(3), z.Apply(v));
    }
}
