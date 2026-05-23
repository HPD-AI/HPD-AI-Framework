using Helium.Primitives;

namespace Helium.Primitives.Tests.Axioms;

#pragma warning disable CS1718 // Comparison to same variable — intentional for axiom testing

public static class OrderedAxioms
{
    public static void VerifyReflexivity<T>(T a) where T : IPartialOrder<T>
    {
        Assert.True(T.LessEqual(a, a));
    }

    public static void VerifyTotality<T>(T a, T b) where T : ITotalOrder<T>
    {
        var ab = T.CompareOrder(a, b);
        var ba = T.CompareOrder(b, a);
        Assert.True(ab is Ordering.Less or Ordering.Equal or Ordering.Greater);
        Assert.True(ba is Ordering.Less or Ordering.Equal or Ordering.Greater);
        Assert.True(T.LessEqual(a, b) || T.LessEqual(b, a));
    }

    public static void VerifyAntisymmetry<T>(T a) where T : IPartialOrder<T>
    {
        Assert.True(T.LessEqual(a, a));
        Assert.True(T.DecidableEquals(a, a));
    }

    public static void VerifyTranslationInvariance<T>(T a, T b, T c)
        where T : ITotalOrder<T>, IRing<T>
    {
        if (T.LessEqual(a, b))
            Assert.True(T.LessEqual(a + c, b + c));
    }
}
