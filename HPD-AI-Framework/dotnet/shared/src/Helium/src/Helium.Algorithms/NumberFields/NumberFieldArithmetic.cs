using Helium.Algebra;
using Helium.Primitives;

namespace Helium.Algorithms;

/// <summary>
/// Element-level operations on number fields that require the Q-vector-space structure of K:
/// Norm, Trace, CharacteristicPolynomial, and MinimalPolynomial.
///
/// These are not part of IField&lt;T&gt; because they depend on the degree and the action of
/// multiplication on the basis {1, α, ..., α^{n-1}}.
///
/// All operations build the multiplication matrix M_a (whose (i,j)-th column is the
/// coordinate vector of αʲ · a mod f expressed in the basis {1, α, ..., α^{n-1}}),
/// then delegate to existing algorithms.
/// </summary>
public static class NumberFieldArithmetic
{
    /// <summary>
    /// Build the multiplication-by-a matrix M_a over Q.
    /// Column j = coordinate vector of (αʲ · a) mod f.
    /// </summary>
    private static Matrix<Rational> MultiplicationMatrix(NumberFieldElement a)
    {
        var field = a.Field;
        int n = field.Degree;
        var f = field.DefiningPolynomial;
        var data = new Rational[n * n];

        // Start with p₀ = a.Value (= α⁰ · a mod f, already reduced).
        var current = a.Value;

        for (int j = 0; j < n; j++)
        {
            // Write coefficients of current into column j (row i = coeff of αⁱ).
            for (int i = 0; i < n; i++)
                data[i * n + j] = current[i];

            // current ← α · current mod f (multiply by x, then reduce).
            if (j < n - 1)
                current = (Polynomial<Rational>.X * current).DivMod(f).Remainder;
        }

        return Matrix<Rational>.FromArray(n, n, data);
    }

    /// <summary>
    /// Norm: N_{K/Q}(a) = det(M_a). Element of Q.
    /// </summary>
    public static Rational Norm(NumberFieldElement a)
    {
        var m = MultiplicationMatrix(a);
        return Determinant.ComputeOverField(m);
    }

    /// <summary>
    /// Trace: Tr_{K/Q}(a) = sum of diagonal of M_a. Element of Q.
    /// </summary>
    public static Rational Trace(NumberFieldElement a)
    {
        int n = a.Field.Degree;
        var m = MultiplicationMatrix(a);
        var trace = Rational.Zero;
        for (int i = 0; i < n; i++)
            trace = trace + m[i, i];
        return trace;
    }

    /// <summary>
    /// Characteristic polynomial: char_poly(a) = det(xI - M_a). Monic polynomial of degree n in Q[x].
    /// </summary>
    public static Polynomial<Rational> CharPoly(NumberFieldElement a)
    {
        var m = MultiplicationMatrix(a);
        return CharacteristicPolynomial.Compute(m);
    }

    /// <summary>
    /// Minimal polynomial: the monic polynomial of least degree in Q[x] satisfied by a.
    /// Equals the squarefree part of CharPoly: min_poly = char_poly / gcd(char_poly, char_poly').
    /// </summary>
    public static Polynomial<Rational> MinPoly(NumberFieldElement a)
    {
        var cp = CharPoly(a);
        var cpPrime = PolynomialCalculus.Derivative<Rational>(cp);

        if (cpPrime.IsZero)
            return cp;

        var g = cp.Gcd(cpPrime);
        if (g.IsZero || g.Degree == 0)
            return cp;

        return cp.DivMod(g).Quotient;
    }
}
