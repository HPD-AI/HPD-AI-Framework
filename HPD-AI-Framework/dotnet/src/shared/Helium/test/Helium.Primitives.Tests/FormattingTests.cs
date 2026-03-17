using Helium.Primitives;

namespace Helium.Primitives.Tests;

public class FormattingTests
{
    // --- Integer ---

    [Fact]
    public void Integer_DefaultFormat()
    {
        Assert.Equal("42", ((Integer)42).ToString());
        Assert.Equal("-7", ((Integer)(-7)).ToString());
        Assert.Equal("0", Integer.Zero.ToString());
    }

    [Fact]
    public void Integer_LatexFormat()
    {
        IFormattable i = (Integer)42;
        Assert.Equal("42", i.ToString("L", null));
    }

    [Fact]
    public void Integer_MathMLFormat()
    {
        IFormattable i = (Integer)42;
        Assert.Equal("<mn>42</mn>", i.ToString("M", null));
    }

    [Fact]
    public void Integer_LargeNumber()
    {
        var big = Integer.Parse("123456789012345678901234567890");
        Assert.Equal("123456789012345678901234567890", big.ToString());
    }

    // --- Rational ---

    [Fact]
    public void Rational_DefaultFormat()
    {
        Assert.Equal("3/4", Rational.Create((Integer)3, (Integer)4).ToString());
        Assert.Equal("5", ((Rational)5).ToString());
        Assert.Equal("-2/3", Rational.Create((Integer)(-2), (Integer)3).ToString());
        Assert.Equal("0", Rational.Zero.ToString());
    }

    [Fact]
    public void Rational_LatexFormat()
    {
        IFormattable r = Rational.Create((Integer)3, (Integer)4);
        Assert.Equal(@"\frac{3}{4}", r.ToString("L", null));
    }

    [Fact]
    public void Rational_LatexFormat_WholeNumber()
    {
        IFormattable r = (Rational)5;
        Assert.Equal("5", r.ToString("L", null));
    }

    [Fact]
    public void Rational_LatexFormat_Negative()
    {
        IFormattable r = Rational.Create((Integer)(-2), (Integer)3);
        Assert.Equal(@"-\frac{2}{3}", r.ToString("L", null));
    }

    [Fact]
    public void Rational_MathMLFormat()
    {
        IFormattable r = Rational.Create((Integer)3, (Integer)4);
        Assert.Equal("<mfrac><mn>3</mn><mn>4</mn></mfrac>", r.ToString("M", null));
    }

    // --- Complex ---

    [Fact]
    public void Complex_PureReal()
    {
        Assert.Equal("5", new Complex(5, 0).ToString());
    }

    [Fact]
    public void Complex_PureImaginary()
    {
        Assert.Equal("i", new Complex(0, 1).ToString());
        Assert.Equal("-i", new Complex(0, -1).ToString());
        Assert.Equal("3i", new Complex(0, 3).ToString());
        Assert.Equal("-3i", new Complex(0, -3).ToString());
    }

    [Fact]
    public void Complex_General()
    {
        Assert.Equal("3 + 4i", new Complex(3, 4).ToString());
        Assert.Equal("3 - 4i", new Complex(3, -4).ToString());
        Assert.Equal("1 + i", new Complex(1, 1).ToString());
        Assert.Equal("1 - i", new Complex(1, -1).ToString());
    }

    [Fact]
    public void Complex_Zero()
    {
        Assert.Equal("0", Complex.Zero.ToString());
    }
}
