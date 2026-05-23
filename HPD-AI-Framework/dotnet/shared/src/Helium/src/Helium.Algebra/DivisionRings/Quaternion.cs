using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Exact Hamilton quaternions over an exact field.
/// </summary>
public readonly record struct Quaternion<R>(
    R Real,
    R I,
    R J,
    R K) :
    IDivisionRing<Quaternion<R>>,
    IStar<Quaternion<R>>,
    IDecidableEq<Quaternion<R>>,
    IFormattable
    where R : IField<R>, IDecidableEq<R>
{
    public static Quaternion<R> Zero => new(
        R.AdditiveIdentity,
        R.AdditiveIdentity,
        R.AdditiveIdentity,
        R.AdditiveIdentity);

    public static Quaternion<R> One => new(
        R.MultiplicativeIdentity,
        R.AdditiveIdentity,
        R.AdditiveIdentity,
        R.AdditiveIdentity);

    public static Quaternion<R> BasisI => new(
        R.AdditiveIdentity,
        R.MultiplicativeIdentity,
        R.AdditiveIdentity,
        R.AdditiveIdentity);

    public static Quaternion<R> BasisJ => new(
        R.AdditiveIdentity,
        R.AdditiveIdentity,
        R.MultiplicativeIdentity,
        R.AdditiveIdentity);

    public static Quaternion<R> BasisK => new(
        R.AdditiveIdentity,
        R.AdditiveIdentity,
        R.AdditiveIdentity,
        R.MultiplicativeIdentity);

    static Quaternion<R> System.Numerics.IAdditiveIdentity<Quaternion<R>, Quaternion<R>>.AdditiveIdentity => Zero;
    static Quaternion<R> System.Numerics.IMultiplicativeIdentity<Quaternion<R>, Quaternion<R>>.MultiplicativeIdentity => One;

    public static Quaternion<R> FromInt(int n) => new(
        R.FromInt(n),
        R.AdditiveIdentity,
        R.AdditiveIdentity,
        R.AdditiveIdentity);

    static Quaternion<R> IRing<Quaternion<R>>.FromInt(int n) => FromInt(n);

    public static Quaternion<R> operator +(Quaternion<R> left, Quaternion<R> right) =>
        new(left.Real + right.Real, left.I + right.I, left.J + right.J, left.K + right.K);

    public static Quaternion<R> operator -(Quaternion<R> left, Quaternion<R> right) =>
        new(left.Real - right.Real, left.I - right.I, left.J - right.J, left.K - right.K);

    public static Quaternion<R> operator -(Quaternion<R> value) =>
        new(-value.Real, -value.I, -value.J, -value.K);

    public static Quaternion<R> operator *(Quaternion<R> left, Quaternion<R> right)
    {
        var a = left.Real;
        var b = left.I;
        var c = left.J;
        var d = left.K;
        var e = right.Real;
        var f = right.I;
        var g = right.J;
        var h = right.K;

        return new(
            a * e - b * f - c * g - d * h,
            a * f + b * e + c * h - d * g,
            a * g - b * h + c * e + d * f,
            a * h + b * g - c * f + d * e);
    }

    public static Quaternion<R> operator /(Quaternion<R> left, Quaternion<R> right) =>
        left * Invert(right);

    public static Quaternion<R> Invert(Quaternion<R> value)
    {
        var norm = value.NormSquared();
        if (norm.Equals(R.AdditiveIdentity))
            return Zero;

        var invNorm = R.Invert(norm);
        return invNorm * Star(value);
    }

    public static Quaternion<R> Star(Quaternion<R> value) =>
        new(value.Real, -value.I, -value.J, -value.K);

    public R NormSquared() =>
        Real * Real + I * I + J * J + K * K;

    public bool IsZero =>
        Real.Equals(R.AdditiveIdentity) &&
        I.Equals(R.AdditiveIdentity) &&
        J.Equals(R.AdditiveIdentity) &&
        K.Equals(R.AdditiveIdentity);

    public static Quaternion<R> operator *(R scalar, Quaternion<R> value) =>
        new(scalar * value.Real, scalar * value.I, scalar * value.J, scalar * value.K);

    public static bool DecidableEquals(Quaternion<R> left, Quaternion<R> right) =>
        R.DecidableEquals(left.Real, right.Real) &&
        R.DecidableEquals(left.I, right.I) &&
        R.DecidableEquals(left.J, right.J) &&
        R.DecidableEquals(left.K, right.K);

    public override string ToString() => ToString(null, null);

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        if (IsZero)
            return "0";

        var parts = new List<string>();
        AppendPart(parts, Real, "", format, formatProvider);
        AppendPart(parts, I, "i", format, formatProvider);
        AppendPart(parts, J, "j", format, formatProvider);
        AppendPart(parts, K, "k", format, formatProvider);
        return string.Join(" + ", parts);
    }

    private static void AppendPart(List<string> parts, R value, string suffix, string? format, IFormatProvider? provider)
    {
        if (value.Equals(R.AdditiveIdentity))
            return;

        parts.Add($"{FormatElement(value, format, provider)}{suffix}");
    }

    private static string FormatElement(R value, string? format, IFormatProvider? provider) =>
        value is IFormattable f ? f.ToString(format, provider) : value.ToString() ?? "";
}
