using Helium.Primitives;

namespace Helium.Primitives.Tests.Axioms;

/// <summary>
/// Generic axiom tests for IRing. Call with any implementing type and sample values.
/// </summary>
public static class RingAxioms
{
    public static void VerifyAdditiveIdentity<T>(T a) where T : IRing<T>
    {
        Assert.Equal(a, a + T.AdditiveIdentity);
        Assert.Equal(a, T.AdditiveIdentity + a);
    }

    public static void VerifyMultiplicativeIdentity<T>(T a) where T : IRing<T>
    {
        Assert.Equal(a, a * T.MultiplicativeIdentity);
        Assert.Equal(a, T.MultiplicativeIdentity * a);
    }

    public static void VerifyZeroAbsorption<T>(T a) where T : IRing<T>
    {
        Assert.Equal(T.AdditiveIdentity, a * T.AdditiveIdentity);
        Assert.Equal(T.AdditiveIdentity, T.AdditiveIdentity * a);
    }

    public static void VerifyAdditiveAssociativity<T>(T a, T b, T c) where T : IRing<T>
    {
        Assert.Equal((a + b) + c, a + (b + c));
    }

    public static void VerifyAdditiveCommutativity<T>(T a, T b) where T : IRing<T>
    {
        Assert.Equal(a + b, b + a);
    }

    public static void VerifyMultiplicativeAssociativity<T>(T a, T b, T c) where T : IRing<T>
    {
        Assert.Equal((a * b) * c, a * (b * c));
    }

    public static void VerifyLeftDistributivity<T>(T a, T b, T c) where T : IRing<T>
    {
        Assert.Equal(a * (b + c), a * b + a * c);
    }

    public static void VerifyRightDistributivity<T>(T a, T b, T c) where T : IRing<T>
    {
        Assert.Equal((a + b) * c, a * c + b * c);
    }

    public static void VerifyAdditiveInverse<T>(T a) where T : IRing<T>
    {
        Assert.Equal(T.AdditiveIdentity, a + (-a));
        Assert.Equal(T.AdditiveIdentity, (-a) + a);
    }

    public static void VerifyDoubleNegation<T>(T a) where T : IRing<T>
    {
        Assert.Equal(a, -(-a));
    }

    public static void VerifySubtraction<T>(T a, T b) where T : IRing<T>
    {
        Assert.Equal(a + (-b), a - b);
    }

    public static void VerifyAll<T>(T a, T b, T c) where T : IRing<T>
    {
        VerifyAdditiveIdentity(a);
        VerifyMultiplicativeIdentity(a);
        VerifyZeroAbsorption(a);
        VerifyAdditiveAssociativity(a, b, c);
        VerifyAdditiveCommutativity(a, b);
        VerifyMultiplicativeAssociativity(a, b, c);
        VerifyLeftDistributivity(a, b, c);
        VerifyRightDistributivity(a, b, c);
        VerifyAdditiveInverse(a);
        VerifyDoubleNegation(a);
        VerifySubtraction(a, b);
    }
}
