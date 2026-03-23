using Helium.Primitives;

namespace Helium.Primitives.Tests.Axioms;

public static class FieldAxioms
{
    public static void VerifyMultiplicativeInverse<T>(T a) where T : IField<T>
    {
        if (!a.Equals(T.AdditiveIdentity))
        {
            Assert.Equal(T.MultiplicativeIdentity, a * T.Invert(a));
            Assert.Equal(T.MultiplicativeIdentity, T.Invert(a) * a);
        }
    }

    public static void VerifyDoubleInversion<T>(T a) where T : IField<T>
    {
        if (!a.Equals(T.AdditiveIdentity))
            Assert.Equal(a, T.Invert(T.Invert(a)));
    }

    public static void VerifyInvertZero<T>() where T : IField<T>
    {
        Assert.Equal(T.AdditiveIdentity, T.Invert(T.AdditiveIdentity));
    }

    public static void VerifyDivisionIsMultByInverse<T>(T a, T b) where T : IField<T>
    {
        if (!b.Equals(T.AdditiveIdentity))
            Assert.Equal(a * T.Invert(b), a / b);
    }

    public static void VerifyAll<T>(T a, T b, T c) where T : IField<T>
    {
        CommRingAxioms.VerifyAll(a, b, c);
        VerifyMultiplicativeInverse(a);
        VerifyMultiplicativeInverse(b);
        VerifyDoubleInversion(a);
        VerifyInvertZero<T>();
        VerifyDivisionIsMultByInverse(a, b);
    }
}
