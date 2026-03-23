using Helium.Primitives;

namespace Helium.Primitives.Tests.Axioms;

public static class CommRingAxioms
{
    public static void VerifyMultiplicativeCommutativity<T>(T a, T b) where T : ICommRing<T>
    {
        Assert.Equal(a * b, b * a);
    }

    public static void VerifyAll<T>(T a, T b, T c) where T : ICommRing<T>
    {
        RingAxioms.VerifyAll(a, b, c);
        VerifyMultiplicativeCommutativity(a, b);
    }
}
