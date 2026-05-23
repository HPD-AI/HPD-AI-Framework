using Helium.Primitives;

namespace Helium.Primitives.Tests.Axioms;

public static class DivisionRingAxioms
{
    public static void VerifyMultiplicativeInverse<T>(T a) where T : IDivisionRing<T>
    {
        if (!a.Equals(T.AdditiveIdentity))
        {
            Assert.Equal(T.MultiplicativeIdentity, a * T.Invert(a));
            Assert.Equal(T.MultiplicativeIdentity, T.Invert(a) * a);
        }
    }

    public static void VerifyDoubleInversion<T>(T a) where T : IDivisionRing<T>
    {
        if (!a.Equals(T.AdditiveIdentity))
            Assert.Equal(a, T.Invert(T.Invert(a)));
    }

    public static void VerifyAll<T>(T a, T b, T c) where T : IDivisionRing<T>
    {
        RingAxioms.VerifyAll(a, b, c);
        VerifyMultiplicativeInverse(a);
        VerifyMultiplicativeInverse(b);
        VerifyDoubleInversion(a);
    }
}
