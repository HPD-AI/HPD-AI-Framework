using Helium.Primitives;

namespace Helium.Primitives.Tests.Axioms;

#pragma warning disable CS1718 // Comparison to same variable — intentional for axiom testing

public static class OrderedAxioms
{
    public static void VerifyReflexivity<T>(T a) where T : IOrdered<T>
    {
        Assert.True(a <= a);
    }

    public static void VerifyTotality<T>(T a, T b) where T : IOrdered<T>
    {
        Assert.True(a <= b || b <= a);
    }

    public static void VerifyAntisymmetry<T>(T a) where T : IOrdered<T>, IEquatable<T>
    {
        Assert.True(a <= a);
        Assert.Equal(a, a);
    }

    public static void VerifyTranslationInvariance<T>(T a, T b, T c)
        where T : IOrdered<T>, IRing<T>
    {
        if (a <= b)
            Assert.True((a + c) <= (b + c));
    }
}
