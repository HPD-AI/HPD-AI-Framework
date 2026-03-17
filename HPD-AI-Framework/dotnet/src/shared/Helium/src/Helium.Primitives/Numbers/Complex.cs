using System.Numerics;

namespace Helium.Primitives;

/// <summary>
/// Complex number as a pair of doubles. Implements field operations and conjugation (IStar).
/// Not IOrdered — the complex field has no compatible total order.
/// </summary>
public readonly record struct Complex(double Re, double Im) :
    IField<Complex>,
    IStar<Complex>,
    ICharP<Complex>,
    IFormattable,
    IParsable<Complex>,
    ISpanParsable<Complex>
{
    // --- Identity elements ---

    public static Complex Zero => new(0.0, 0.0);
    public static Complex One => new(1.0, 0.0);
    public static Complex I => new(0.0, 1.0);

    static Complex IAdditiveIdentity<Complex, Complex>.AdditiveIdentity => Zero;
    static Complex IMultiplicativeIdentity<Complex, Complex>.MultiplicativeIdentity => One;

    // --- Arithmetic operators ---

    public static Complex operator +(Complex left, Complex right) =>
        new(left.Re + right.Re, left.Im + right.Im);

    public static Complex operator -(Complex left, Complex right) =>
        new(left.Re - right.Re, left.Im - right.Im);

    public static Complex operator *(Complex left, Complex right) =>
        new(left.Re * right.Re - left.Im * right.Im,
            left.Re * right.Im + left.Im * right.Re);

    public static Complex operator /(Complex left, Complex right) =>
        left * Invert(right);

    public static Complex operator -(Complex value) =>
        new(-value.Re, -value.Im);

    // --- IField ---

    public static Complex Invert(Complex a)
    {
        var norm = a.Re * a.Re + a.Im * a.Im;
        if (norm == 0.0)
            return Zero;
        return new(a.Re / norm, -a.Im / norm);
    }

    // --- IStar (conjugation) ---

    public static Complex Star(Complex a) => new(a.Re, -a.Im);

    // --- IRing.FromInt override ---

    static Complex IRing<Complex>.FromInt(int n) => new((double)n, 0.0);

    // --- ICharP ---

    public static int Characteristic => 0;

    // --- Equality (IEEE double semantics: NaN != NaN) ---

    public bool Equals(Complex other) =>
        Re == other.Re && Im == other.Im;

    public override int GetHashCode() => HashCode.Combine(Re, Im);

    public override string ToString() => ToString(null, null);

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        if (format == "M")
            return FormatMathML();
        return FormatComplex();
    }

    private string FormatComplex()
    {
        if (Im == 0.0) return $"{Re}";
        if (Re == 0.0) return FormatImaginary(Im);

        if (Im > 0)
            return $"{Re} + {FormatPositiveImaginary(Im)}";
        return $"{Re} - {FormatPositiveImaginary(-Im)}";
    }

    private static string FormatImaginary(double im)
    {
        if (im == 1.0) return "i";
        if (im == -1.0) return "-i";
        return $"{im}i";
    }

    private static string FormatPositiveImaginary(double absIm)
    {
        if (absIm == 1.0) return "i";
        return $"{absIm}i";
    }

    private string FormatMathML()
    {
        if (Im == 0.0) return $"<mn>{Re}</mn>";
        if (Re == 0.0)
        {
            if (Im == 1.0) return "<mi>i</mi>";
            if (Im == -1.0) return "<mo>-</mo><mi>i</mi>";
            if (Im < 0) return $"<mo>-</mo><mn>{-Im}</mn><mi>i</mi>";
            return $"<mn>{Im}</mn><mi>i</mi>";
        }
        if (Im == 1.0) return $"<mn>{Re}</mn><mo>+</mo><mi>i</mi>";
        if (Im == -1.0) return $"<mn>{Re}</mn><mo>-</mo><mi>i</mi>";
        if (Im < 0) return $"<mn>{Re}</mn><mo>-</mo><mn>{-Im}</mn><mi>i</mi>";
        return $"<mn>{Re}</mn><mo>+</mo><mn>{Im}</mn><mi>i</mi>";
    }

    // --- Helpers ---

    public double NormSquared => Re * Re + Im * Im;
    public double Magnitude => Math.Sqrt(NormSquared);
    public bool IsZero => Re == 0.0 && Im == 0.0;

    public static implicit operator Complex(double re) => new(re, 0.0);

    // --- Parsing (IParsable / ISpanParsable) ---

    public static Complex Parse(string s, IFormatProvider? provider) =>
        Parse(s.AsSpan(), provider);

    public static bool TryParse(string? s, IFormatProvider? provider, out Complex result)
    {
        if (s is null)
        {
            result = Zero;
            return false;
        }

        return TryParse(s.AsSpan(), provider, out result);
    }

    public static Complex Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        if (TryParse(s, provider, out var result))
            return result;
        throw new FormatException("Invalid complex literal.");
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Complex result)
    {
        s = Trim(s);
        if (s.Length == 0)
        {
            result = Zero;
            return false;
        }

        // We accept: a, bi, a+bi, a-bi, i, -i (whitespace ignored)
        var t = RemoveWhitespace(s);

        if (t.Length == 0)
        {
            result = Zero;
            return false;
        }

        if (t[^1] != 'i')
        {
            if (double.TryParse(t, provider, out var re))
            {
                result = new Complex(re, 0.0);
                return true;
            }

            result = Zero;
            return false;
        }

        var body = t.AsSpan()[..^1];
        if (body.Length == 0 || body.SequenceEqual("+".AsSpan()))
        {
            result = I;
            return true;
        }

        if (body.SequenceEqual("-".AsSpan()))
        {
            result = new Complex(0.0, -1.0);
            return true;
        }

        // Find separator between real and imaginary parts, if any.
        int sep = LastPlusMinus(body);
        if (sep > 0)
        {
            var reSpan = body[..sep];
            var imSpan = body[sep..];

            if (!double.TryParse(reSpan, provider, out var re))
            {
                result = Zero;
                return false;
            }

            double im;
            if (imSpan.SequenceEqual("+".AsSpan()))
                im = 1.0;
            else if (imSpan.SequenceEqual("-".AsSpan()))
                im = -1.0;
            else if (!double.TryParse(imSpan, provider, out im))
            {
                result = Zero;
                return false;
            }

            result = new Complex(re, im);
            return true;
        }

        // Pure imaginary.
        if (body.SequenceEqual("+".AsSpan()))
        {
            result = I;
            return true;
        }

        if (body.SequenceEqual("-".AsSpan()))
        {
            result = new Complex(0.0, -1.0);
            return true;
        }

        if (!double.TryParse(body, provider, out var imag))
        {
            result = Zero;
            return false;
        }

        result = new Complex(0.0, imag);
        return true;
    }

    private static int LastPlusMinus(ReadOnlySpan<char> s)
    {
        for (int i = s.Length - 1; i >= 1; i--)
        {
            if (s[i] is '+' or '-')
                return i;
        }
        return -1;
    }

    private static ReadOnlySpan<char> Trim(ReadOnlySpan<char> s)
    {
        int start = 0;
        while (start < s.Length && char.IsWhiteSpace(s[start]))
            start++;

        int end = s.Length - 1;
        while (end >= start && char.IsWhiteSpace(s[end]))
            end--;

        return s[start..(end + 1)];
    }

    private static string RemoveWhitespace(ReadOnlySpan<char> s)
    {
        int whitespaceCount = 0;
        foreach (var c in s)
            if (char.IsWhiteSpace(c))
                whitespaceCount++;

        if (whitespaceCount == 0)
            return s.ToString();

        var buffer = new char[s.Length - whitespaceCount];
        int j = 0;
        foreach (var c in s)
            if (!char.IsWhiteSpace(c))
                buffer[j++] = c;

        return new string(buffer);
    }
}
