using Helium.Primitives;

namespace Helium.Primitives.Tests.Axioms;

public static class AddGroupAxioms
{
    public static void VerifyAdditiveIdentity<T>(T a) where T : IAddGroup<T>
    {
        Assert.Equal(a, a + T.AdditiveIdentity);
        Assert.Equal(a, T.AdditiveIdentity + a);
    }

    public static void VerifyAdditiveAssociativity<T>(T a, T b, T c) where T : IAddGroup<T>
    {
        Assert.Equal((a + b) + c, a + (b + c));
    }

    public static void VerifyAdditiveInverse<T>(T a) where T : IAddGroup<T>
    {
        Assert.Equal(T.AdditiveIdentity, a + (-a));
        Assert.Equal(T.AdditiveIdentity, (-a) + a);
    }

    public static void VerifySubtraction<T>(T a, T b) where T : IAddGroup<T>
    {
        Assert.Equal(a + (-b), a - b);
    }

    public static void VerifyAdditiveCommutativity<T>(T a, T b) where T : IAddCommGroup<T>
    {
        Assert.Equal(a + b, b + a);
    }

    public static void VerifyAll<T>(T a, T b, T c) where T : IAddCommGroup<T>
    {
        VerifyAdditiveIdentity(a);
        VerifyAdditiveAssociativity(a, b, c);
        VerifyAdditiveInverse(a);
        VerifySubtraction(a, b);
        VerifyAdditiveCommutativity(a, b);
    }
}
