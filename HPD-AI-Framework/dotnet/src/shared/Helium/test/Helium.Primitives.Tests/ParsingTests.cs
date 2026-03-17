using Helium.Primitives;

namespace Helium.Primitives.Tests;

public class ParsingTests
{
    [Fact]
    public void Integer_Parse_Valid()
    {
        Assert.Equal((Integer)42, Integer.Parse("42", null));
        Assert.Equal((Integer)(-7), Integer.Parse("-7", null));
        Assert.Equal(Integer.Zero, Integer.Parse("0", null));
        Assert.Equal((Integer)42, Integer.Parse("   42   ", null));
    }

    [Fact]
    public void Integer_Parse_Large()
    {
        var big = Integer.Parse("123456789012345678901234567890", null);
        Assert.Equal("123456789012345678901234567890", big.ToString());
    }

    [Fact]
    public void Integer_TryParse_Invalid()
    {
        Assert.False(Integer.TryParse("", null, out _));
        Assert.False(Integer.TryParse("abc", null, out _));
        Assert.False(Integer.TryParse("3.14", null, out _));
    }

    [Fact]
    public void Rational_Parse_Valid()
    {
        Assert.Equal(Rational.Create((Integer)3, (Integer)4), Rational.Parse("3/4", null));
        Assert.Equal(Rational.Create((Integer)(-2), (Integer)3), Rational.Parse("-2/3", null));
        Assert.Equal(Rational.Create((Integer)5, (Integer)1), Rational.Parse("5", null));
        Assert.Equal(Rational.Zero, Rational.Parse("0", null));

        // Reduction + sign normalization.
        Assert.Equal(Rational.Create((Integer)3, (Integer)2), Rational.Parse("6/4", null));
        Assert.Equal(Rational.Create((Integer)3, (Integer)2), Rational.Parse("-6/-4", null));

        // Whitespace tolerance.
        Assert.Equal(Rational.Create((Integer)3, (Integer)4), Rational.Parse("  3/4  ", null));
    }

    [Fact]
    public void Rational_Parse_DenominatorZero_IsZero()
    {
        Assert.Equal(Rational.Zero, Rational.Parse("3/0", null));
    }

    [Fact]
    public void Rational_TryParse_Invalid()
    {
        Assert.False(Rational.TryParse("", null, out _));
        Assert.False(Rational.TryParse("abc", null, out _));
        Assert.False(Rational.TryParse("3/", null, out _));
        Assert.False(Rational.TryParse("/4", null, out _));
    }

    // --- Complex parsing ---

    [Fact]
    public void Complex_Parse_PureReal()
    {
        Assert.Equal(new Complex(3.0, 0.0), Complex.Parse("3", null));
        Assert.Equal(new Complex(-7.0, 0.0), Complex.Parse("-7", null));
        Assert.Equal(new Complex(0.0, 0.0), Complex.Parse("0", null));
    }

    [Fact]
    public void Complex_Parse_PureImaginary()
    {
        Assert.Equal(new Complex(0.0, 1.0), Complex.Parse("i", null));
        Assert.Equal(new Complex(0.0, -1.0), Complex.Parse("-i", null));
        Assert.Equal(new Complex(0.0, 3.0), Complex.Parse("3i", null));
        Assert.Equal(new Complex(0.0, -3.0), Complex.Parse("-3i", null));
    }

    [Fact]
    public void Complex_Parse_General()
    {
        Assert.Equal(new Complex(3.0, 4.0), Complex.Parse("3+4i", null));
        Assert.Equal(new Complex(3.0, -4.0), Complex.Parse("3-4i", null));
        Assert.Equal(new Complex(1.0, 1.0), Complex.Parse("1+i", null));
        Assert.Equal(new Complex(1.0, -1.0), Complex.Parse("1-i", null));
    }

    [Fact]
    public void Complex_Parse_WhitespaceTolerance()
    {
        Assert.Equal(new Complex(3.0, 4.0), Complex.Parse("3 + 4i", null));
        Assert.Equal(new Complex(3.0, -4.0), Complex.Parse("3 - 4i", null));
        Assert.Equal(new Complex(3.0, 4.0), Complex.Parse("  3 + 4i  ", null));
    }

    [Fact]
    public void Complex_TryParse_Invalid()
    {
        Assert.False(Complex.TryParse("", null, out _));
        Assert.False(Complex.TryParse(null, null, out _));
        Assert.False(Complex.TryParse("abc", null, out _));
    }

    // --- Roundtrip tests ---

    [Fact]
    public void Integer_Roundtrip_FormatParse()
    {
        var values = new[] { (Integer)0, (Integer)42, (Integer)(-7), Integer.Parse("123456789012345678901234567890") };
        foreach (var v in values)
        {
            var s = v.ToString();
            var parsed = Integer.Parse(s, null);
            Assert.Equal(v, parsed);
        }
    }

    [Fact]
    public void Rational_Roundtrip_FormatParse()
    {
        var values = new[]
        {
            Rational.Zero,
            (Rational)5,
            Rational.Create((Integer)3, (Integer)4),
            Rational.Create((Integer)(-2), (Integer)3),
        };
        foreach (var v in values)
        {
            var s = v.ToString();
            var parsed = Rational.Parse(s, null);
            Assert.Equal(v, parsed);
        }
    }

    // --- Complex LaTeX display ---

    [Fact]
    public void Complex_LatexFormat()
    {
        // LaTeX is same as default for Complex (no special LaTeX rendering)
        IFormattable c = new Complex(3, 4);
        Assert.Equal("3 + 4i", c.ToString("L", null));

        IFormattable neg = new Complex(3, -4);
        Assert.Equal("3 - 4i", neg.ToString("L", null));

        IFormattable pure = new Complex(0, 1);
        Assert.Equal("i", pure.ToString("L", null));
    }

    // --- Complex MathML display (regression for sign fix) ---

    [Fact]
    public void Complex_MathML_NegativeImaginary()
    {
        IFormattable c = new Complex(3, -4);
        Assert.Equal("<mn>3</mn><mo>-</mo><mn>4</mn><mi>i</mi>", c.ToString("M", null));
    }

    [Fact]
    public void Complex_MathML_NegativeUnitImaginary()
    {
        IFormattable c = new Complex(3, -1);
        Assert.Equal("<mn>3</mn><mo>-</mo><mi>i</mi>", c.ToString("M", null));
    }

    [Fact]
    public void Complex_MathML_PositiveUnitImaginary()
    {
        IFormattable c = new Complex(3, 1);
        Assert.Equal("<mn>3</mn><mo>+</mo><mi>i</mi>", c.ToString("M", null));
    }

    [Fact]
    public void Complex_MathML_PureNegativeImaginary()
    {
        IFormattable c = new Complex(0, -3);
        Assert.Equal("<mo>-</mo><mn>3</mn><mi>i</mi>", c.ToString("M", null));
    }
}

