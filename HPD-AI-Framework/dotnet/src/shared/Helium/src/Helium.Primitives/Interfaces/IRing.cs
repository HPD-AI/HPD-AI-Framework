using System.Numerics;

namespace Helium.Primitives;

/// <summary>
/// A ring: semiring with additive inverses (negation, subtraction).
/// </summary>
public interface IRing<T> :
    ISemiring<T>,
    IUnaryNegationOperators<T, T>,
    ISubtractionOperators<T, T, T>
    where T : IRing<T>
{
    /// <summary>
    /// Converts an integer to a ring element via the unique ring homomorphism ℤ → T.
    /// Default: O(|n|) by repeated addition. Concrete types override with O(1) casts.
    /// </summary>
    static virtual T FromInt(int n)
    {
        if (n == 0) return T.AdditiveIdentity;
        var result = T.AdditiveIdentity;
        if (n > 0)
            for (int i = 0; i < n; i++) result = result + T.MultiplicativeIdentity;
        else
            for (int i = 0; i > n; i--) result = result - T.MultiplicativeIdentity;
        return result;
    }
}
