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
        Assert.Equal(Rational.Create((Integer)3, (Integer)2), Rational.Parse("6/4", null));
        Assert.Equal(Rational.Create((Integer)3, (Integer)2), Rational.Parse("-6/-4", null));
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
}
