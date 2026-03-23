using System.Numerics;
using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algorithms;

/// <summary>
/// Symbolic differentiation and integration for polynomials and formal power series.
/// Operates directly on canonical representations — no expression trees, no intermediate forms.
/// </summary>
public static class PolynomialCalculus
{
    /// <summary>
    /// Formal derivative of a univariate polynomial.
    /// For each term c*x^n, produces n*c*x^(n-1). Constant terms vanish.
    /// </summary>
    public static Polynomial<R> Derivative<R>(Polynomial<R> p) where R : IRing<R>
    {
        if (p.IsZero)
            return Polynomial<R>.Zero;

        var coeffs = new List<KeyValuePair<int, R>>();
        foreach (var exp in p.Support)
        {
            if (exp == 0)
                continue;

            var newCoeff = p[exp] * R.FromInt(exp);
            coeffs.Add(new(exp - 1, newCoeff));
        }

        if (coeffs.Count == 0)
            return Polynomial<R>.Zero;

        return Polynomial<R>.FromCoeffs(CoeffsToArray<R>(coeffs));
    }

    /// <summary>
    /// N-th derivative: applies the derivative operator n times.
    /// Returns the zero polynomial if n exceeds the degree.
    /// </summary>
    public static Polynomial<R> NthDerivative<R>(Polynomial<R> p, int n) where R : IRing<R>
    {
        if (n < 0)
            throw new ArgumentOutOfRangeException(nameof(n), "Order must be non-negative.");

        var result = p;
        for (int i = 0; i < n; i++)
        {
            result = Derivative(result);
            if (result.IsZero)
                break;
        }
        return result;
    }

    /// <summary>
    /// Formal antiderivative of a univariate polynomial over a field.
    /// For each term c*x^n, produces (c/(n+1))*x^(n+1). Constant of integration is zero.
    /// </summary>
    public static Polynomial<R> Integrate<R>(Polynomial<R> p) where R : IField<R>
    {
        if (p.IsZero)
            return Polynomial<R>.Zero;

        var coeffs = new List<KeyValuePair<int, R>>();
        foreach (var exp in p.Support)
        {
            var newCoeff = p[exp] * R.Invert(R.FromInt(exp + 1));
            coeffs.Add(new(exp + 1, newCoeff));
        }

        if (coeffs.Count == 0)
            return Polynomial<R>.Zero;

        return Polynomial<R>.FromCoeffs(CoeffsToArray<R>(coeffs));
    }

    /// <summary>
    /// Partial derivative of a multivariate polynomial with respect to a given variable.
    /// For each term with x_i^a_i, multiplies the coefficient by a_i and decrements the exponent.
    /// Terms where the variable does not appear vanish.
    /// </summary>
    public static MvPolynomial<R> PartialDerivative<R>(MvPolynomial<R> p, int variable)
        where R : ICommRing<R>
    {
        if (p.IsZero)
            return MvPolynomial<R>.Zero;

        var result = MvPolynomial<R>.Zero;
        foreach (var monomial in p.Support)
        {
            var exp = monomial[variable];
            if (exp == Integer.Zero)
                continue;

            int expInt = (int)(BigInteger)exp;
            var newCoeff = p[monomial] * R.FromInt(expInt);

            var newExponents = new List<KeyValuePair<int, Integer>>();
            foreach (var v in monomial.Variables)
            {
                if (v == variable)
                {
                    var newExp = monomial[v] - Integer.One;
                    if (newExp != Integer.Zero)
                        newExponents.Add(new(v, newExp));
                }
                else
                {
                    newExponents.Add(new(v, monomial[v]));
                }
            }

            var newMonomial = Monomial.FromExponents(newExponents);
            result = result + MvPolynomial<R>.Term(newMonomial, newCoeff);
        }

        return result;
    }

    /// <summary>
    /// Formal derivative of a formal power series.
    /// The n-th coefficient of f' is (n+1) * f.Coefficient(n+1).
    /// Lazy: transforms the generator function, not the series.
    /// </summary>
    public static FormalPowerSeries<R> FormalDerivative<R>(FormalPowerSeries<R> f)
        where R : IField<R>
    {
        return FormalPowerSeries<R>.FromGenerator(n =>
            f.Coefficient(n + 1) * R.FromInt(n + 1));
    }

    private static R[] CoeffsToArray<R>(List<KeyValuePair<int, R>> coeffs)
        where R : IRing<R>
    {
        if (coeffs.Count == 0)
            return [];

        int maxDeg = 0;
        foreach (var kv in coeffs)
            if (kv.Key > maxDeg) maxDeg = kv.Key;

        var result = new R[maxDeg + 1];
        Array.Fill(result, R.AdditiveIdentity);
        foreach (var kv in coeffs)
            result[kv.Key] = kv.Value;
        return result;
    }
}
