using System.Numerics;
using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Dense univariate polynomial over a ring R.
/// Coefficients are stored contiguously from degree 0 upward and normalized by
/// trimming trailing zero coefficients. This is exact host algebra, not a
/// hardware buffer.
/// </summary>
public readonly struct DensePolynomial<R> :
    ICommRing<DensePolynomial<R>>,
    IEquatable<DensePolynomial<R>>,
    IFormattable
    where R : IRing<R>
{
    private readonly R[]? _coeffs;

    private ReadOnlySpan<R> Coeffs => _coeffs ?? [];

    private DensePolynomial(R[] coeffs)
    {
        _coeffs = coeffs.Length == 0 ? null : coeffs;
    }

    public static DensePolynomial<R> Zero => new([]);

    public static DensePolynomial<R> One => C(R.MultiplicativeIdentity);

    public static DensePolynomial<R> X => Monomial(1, R.MultiplicativeIdentity);

    public static DensePolynomial<R> C(R value) => Monomial(0, value);

    public static DensePolynomial<R> Monomial(int degree, R coefficient)
    {
        if (degree < 0 || coefficient.Equals(R.AdditiveIdentity))
            return Zero;

        var coeffs = new R[degree + 1];
        Array.Fill(coeffs, R.AdditiveIdentity);
        coeffs[degree] = coefficient;
        return new(coeffs);
    }

    public static DensePolynomial<R> FromCoeffs(params ReadOnlySpan<R> coefficients) =>
        new(Normalize(coefficients));

    public static DensePolynomial<R> FromSparse(SparsePolynomial<R> polynomial)
    {
        if (polynomial.IsZero)
            return Zero;

        var coeffs = new R[polynomial.Degree + 1];
        Array.Fill(coeffs, R.AdditiveIdentity);
        foreach (var degree in polynomial.Support)
            coeffs[degree] = polynomial[degree];
        return new(coeffs);
    }

    public SparsePolynomial<R> ToSparse() => SparsePolynomial<R>.FromCoeffs(Coeffs);

    public ReadOnlySpan<R> Coefficients => Coeffs;

    public R this[int degree] =>
        degree < 0 || degree >= Coeffs.Length ? R.AdditiveIdentity : Coeffs[degree];

    public bool IsZero => Coeffs.Length == 0;

    public int Degree => Coeffs.Length - 1;

    public R LeadingCoefficient => IsZero ? R.AdditiveIdentity : Coeffs[^1];

    static DensePolynomial<R> IAdditiveIdentity<DensePolynomial<R>, DensePolynomial<R>>.AdditiveIdentity => Zero;

    static DensePolynomial<R> IMultiplicativeIdentity<DensePolynomial<R>, DensePolynomial<R>>.MultiplicativeIdentity => One;

    public static DensePolynomial<R> operator +(DensePolynomial<R> left, DensePolynomial<R> right)
    {
        var leftCoeffs = left.Coeffs;
        var rightCoeffs = right.Coeffs;
        var length = Math.Max(leftCoeffs.Length, rightCoeffs.Length);
        if (length == 0)
            return Zero;

        var result = new R[length];
        for (int i = 0; i < length; i++)
            result[i] = left[i] + right[i];
        return new(Normalize(result));
    }

    public static DensePolynomial<R> operator -(DensePolynomial<R> left, DensePolynomial<R> right)
    {
        var leftCoeffs = left.Coeffs;
        var rightCoeffs = right.Coeffs;
        var length = Math.Max(leftCoeffs.Length, rightCoeffs.Length);
        if (length == 0)
            return Zero;

        var result = new R[length];
        for (int i = 0; i < length; i++)
            result[i] = left[i] - right[i];
        return new(Normalize(result));
    }

    public static DensePolynomial<R> operator -(DensePolynomial<R> polynomial)
    {
        var coeffs = polynomial.Coeffs;
        if (coeffs.Length == 0)
            return Zero;

        var result = new R[coeffs.Length];
        for (int i = 0; i < coeffs.Length; i++)
            result[i] = -coeffs[i];
        return new(result);
    }

    public static DensePolynomial<R> operator *(DensePolynomial<R> left, DensePolynomial<R> right)
    {
        var leftCoeffs = left.Coeffs;
        var rightCoeffs = right.Coeffs;
        if (leftCoeffs.Length == 0 || rightCoeffs.Length == 0)
            return Zero;

        var result = new R[leftCoeffs.Length + rightCoeffs.Length - 1];
        Array.Fill(result, R.AdditiveIdentity);

        for (int i = 0; i < leftCoeffs.Length; i++)
        for (int j = 0; j < rightCoeffs.Length; j++)
            result[i + j] = result[i + j] + leftCoeffs[i] * rightCoeffs[j];

        return new(Normalize(result));
    }

    public static bool operator ==(DensePolynomial<R> left, DensePolynomial<R> right) =>
        left.Coeffs.SequenceEqual(right.Coeffs);

    public static bool operator !=(DensePolynomial<R> left, DensePolynomial<R> right) => !(left == right);

    public bool Equals(DensePolynomial<R> other) => this == other;

    public override bool Equals(object? obj) => obj is DensePolynomial<R> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var coeff in Coeffs)
            hash.Add(coeff);
        return hash.ToHashCode();
    }

    public override string ToString() => ToString(null, null);

    public string ToString(string? format, IFormatProvider? provider) =>
        ToSparse().ToString(format, provider);

    private static R[] Normalize(ReadOnlySpan<R> coefficients)
    {
        var length = coefficients.Length;
        while (length > 0 && coefficients[length - 1].Equals(R.AdditiveIdentity))
            length--;

        if (length == 0)
            return [];

        var result = new R[length];
        for (int i = 0; i < length; i++)
            result[i] = coefficients[i];
        return result;
    }
}
