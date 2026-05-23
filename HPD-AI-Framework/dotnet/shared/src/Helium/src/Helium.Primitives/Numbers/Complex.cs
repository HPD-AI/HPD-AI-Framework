using System.Numerics;

namespace Helium.Primitives;

/// <summary>
/// Exact complex numbers over an exact field.
/// </summary>
public readonly struct Complex<R> :
    IField<Complex<R>>,
    IStar<Complex<R>>,
    IEquatable<Complex<R>>,
    IDecidableEq<Complex<R>>,
    IFormattable
    where R : IField<R>, IDecidableEq<R>
{
    public R Re { get; }
    public R Im { get; }

    public Complex(R re, R im)
    {
        Re = re;
        Im = im;
    }

    public static Complex<R> Zero => new(R.AdditiveIdentity, R.AdditiveIdentity);
    public static Complex<R> One => new(R.MultiplicativeIdentity, R.AdditiveIdentity);
    public static Complex<R> I => new(R.AdditiveIdentity, R.MultiplicativeIdentity);

    static Complex<R> IAdditiveIdentity<Complex<R>, Complex<R>>.AdditiveIdentity => Zero;
    static Complex<R> IMultiplicativeIdentity<Complex<R>, Complex<R>>.MultiplicativeIdentity => One;

    public static Complex<R> FromInt(int n) => new(R.FromInt(n), R.AdditiveIdentity);

    static Complex<R> IRing<Complex<R>>.FromInt(int n) => FromInt(n);

    public static Complex<R> operator +(Complex<R> left, Complex<R> right) =>
        new(left.Re + right.Re, left.Im + right.Im);

    public static Complex<R> operator -(Complex<R> left, Complex<R> right) =>
        new(left.Re - right.Re, left.Im - right.Im);

    public static Complex<R> operator -(Complex<R> value) =>
        new(-value.Re, -value.Im);

    public static Complex<R> operator *(Complex<R> left, Complex<R> right) =>
        new(left.Re * right.Re - left.Im * right.Im,
            left.Re * right.Im + left.Im * right.Re);

    public static Complex<R> operator /(Complex<R> left, Complex<R> right) =>
        left * Invert(right);

    public static Complex<R> Invert(Complex<R> a)
    {
        var norm = a.Re * a.Re + a.Im * a.Im;
        if (norm.Equals(R.AdditiveIdentity))
            return Zero;

        var invNorm = R.Invert(norm);
        return new(a.Re * invNorm, -a.Im * invNorm);
    }

    public static Complex<R> Star(Complex<R> a) => new(a.Re, -a.Im);

    public bool Equals(Complex<R> other) => Re.Equals(other.Re) && Im.Equals(other.Im);
    public override bool Equals(object? obj) => obj is Complex<R> other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Re, Im);
    public static bool operator ==(Complex<R> left, Complex<R> right) => left.Equals(right);
    public static bool operator !=(Complex<R> left, Complex<R> right) => !left.Equals(right);
    public static bool DecidableEquals(Complex<R> left, Complex<R> right) =>
        R.DecidableEquals(left.Re, right.Re) && R.DecidableEquals(left.Im, right.Im);

    public bool IsZero => Re.Equals(R.AdditiveIdentity) && Im.Equals(R.AdditiveIdentity);

    public override string ToString() => ToString(null, null);

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        if (format == "M")
            return FormatMathML(formatProvider);
        return FormatDefault(format, formatProvider);
    }

    private string FormatDefault(string? format, IFormatProvider? provider)
    {
        if (Im.Equals(R.AdditiveIdentity))
            return FormatElement(Re, format, provider);

        if (Re.Equals(R.AdditiveIdentity))
            return FormatImaginary(Im, format, provider);

        if (IsNegativeLike(Im))
            return $"{FormatElement(Re, format, provider)} - {FormatPositiveImaginary(-Im, format, provider)}";

        return $"{FormatElement(Re, format, provider)} + {FormatPositiveImaginary(Im, format, provider)}";
    }

    private string FormatMathML(IFormatProvider? provider)
    {
        if (Im.Equals(R.AdditiveIdentity))
            return FormatElement(Re, "M", provider);

        if (Re.Equals(R.AdditiveIdentity))
            return FormatImaginaryMathML(Im, provider);

        var re = FormatElement(Re, "M", provider);
        if (IsNegativeLike(Im))
            return $"{re}<mo>-</mo>{FormatPositiveImaginaryMathML(-Im, provider)}";

        return $"{re}<mo>+</mo>{FormatPositiveImaginaryMathML(Im, provider)}";
    }

    private static string FormatImaginary(R im, string? format, IFormatProvider? provider)
    {
        if (im.Equals(R.MultiplicativeIdentity))
            return "i";
        if (im.Equals(-R.MultiplicativeIdentity))
            return "-i";
        return $"{FormatElement(im, format, provider)}i";
    }

    private static string FormatPositiveImaginary(R im, string? format, IFormatProvider? provider)
    {
        if (im.Equals(R.MultiplicativeIdentity))
            return "i";
        return $"{FormatElement(im, format, provider)}i";
    }

    private static string FormatImaginaryMathML(R im, IFormatProvider? provider)
    {
        if (im.Equals(R.MultiplicativeIdentity))
            return "<mi>i</mi>";
        if (im.Equals(-R.MultiplicativeIdentity))
            return "<mo>-</mo><mi>i</mi>";
        if (IsNegativeLike(im))
            return $"<mo>-</mo>{FormatPositiveImaginaryMathML(-im, provider)}";
        return FormatPositiveImaginaryMathML(im, provider);
    }

    private static string FormatPositiveImaginaryMathML(R im, IFormatProvider? provider)
    {
        if (im.Equals(R.MultiplicativeIdentity))
            return "<mi>i</mi>";
        return $"{FormatElement(im, "M", provider)}<mi>i</mi>";
    }

    private static string FormatElement(R value, string? format, IFormatProvider? provider) =>
        value is IFormattable f ? f.ToString(format, provider) : value.ToString() ?? "";

    private static bool IsNegativeLike(R value) =>
        value.ToString()?.StartsWith("-", StringComparison.Ordinal) == true;
}
