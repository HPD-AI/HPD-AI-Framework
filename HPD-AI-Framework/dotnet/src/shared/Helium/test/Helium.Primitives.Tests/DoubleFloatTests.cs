using Helium.Primitives;

namespace Helium.Primitives.Tests;

public class DoubleFloatTests
{
    private static T FromInt<T>(int n) where T : IRing<T> => T.FromInt(n);

    // --- Double identity elements ---

    [Fact]
    public void Double_AdditiveIdentity() =>
        Assert.Equal(new Double(3.5), Double.Zero + new Double(3.5));

    [Fact]
    public void Double_MultiplicativeIdentity() =>
        Assert.Equal(new Double(7.0), Double.One * new Double(7.0));

    // --- Double arithmetic ---

    [Fact]
    public void Double_Add() =>
        Assert.Equal(new Double(4.0), new Double(1.5) + new Double(2.5));

    [Fact]
    public void Double_Sub() =>
        Assert.Equal(new Double(3.0), new Double(5.0) - new Double(2.0));

    [Fact]
    public void Double_Mul() =>
        Assert.Equal(new Double(12.0), new Double(3.0) * new Double(4.0));

    [Fact]
    public void Double_Div() =>
        Assert.Equal(new Double(3.5), new Double(7.0) / new Double(2.0));

    [Fact]
    public void Double_Negate() =>
        Assert.Equal(new Double(-4.0), -new Double(4.0));

    // --- Double.Invert ---

    [Fact]
    public void Double_Invert_Normal() =>
        Assert.Equal(new Double(0.25), Double.Invert(new Double(4.0)));

    [Fact]
    public void Double_Invert_Zero() =>
        Assert.Equal(Double.Zero, Double.Invert(Double.Zero)); // total function

    [Fact]
    public void Double_Invert_One() =>
        Assert.Equal(Double.One, Double.Invert(Double.One));

    // --- Double.IsZero ---

    [Fact]
    public void Double_IsZero_True() =>
        Assert.True(new Double(0.0).IsZero);

    [Fact]
    public void Double_IsZero_False() =>
        Assert.False(new Double(1.0).IsZero);

    // --- Double equality ---

    [Fact]
    public void Double_Equality() =>
        Assert.True(new Double(3.14) == new Double(3.14));

    [Fact]
    public void Double_Inequality() =>
        Assert.True(new Double(1.0) != new Double(2.0));

    // --- Double characteristic ---

    [Fact]
    public void Double_Characteristic_Zero() =>
        Assert.Equal(0, Double.Characteristic);

    // --- Double conversions ---

    [Fact]
    public void Double_ImplicitFromDouble()
    {
        Double d = 2.718;
        Assert.Equal(new Double(2.718), d);
    }

    [Fact]
    public void Double_ExplicitToDouble()
    {
        var d = new Double(1.5);
        Assert.Equal(1.5, (double)d);
    }

    [Fact]
    public void Double_FromInt() =>
        Assert.Equal(new Double(7.0), FromInt<Double>(7));

    // --- Float ---

    [Fact]
    public void Float_AdditiveIdentity() =>
        Assert.Equal(new Float(3.0f), Float.Zero + new Float(3.0f));

    [Fact]
    public void Float_Mul() =>
        Assert.Equal(new Float(6.0f), new Float(3.0f) * new Float(2.0f));

    [Fact]
    public void Float_Invert_Zero() =>
        Assert.Equal(Float.Zero, Float.Invert(Float.Zero)); // total function

    [Fact]
    public void Float_Invert_Normal() =>
        Assert.Equal(new Float(0.5f), Float.Invert(new Float(2.0f)));

    [Fact]
    public void Float_FromInt() =>
        Assert.Equal(new Float(5.0f), FromInt<Float>(5));

    [Fact]
    public void Float_IsZero_True() =>
        Assert.True(new Float(0.0f).IsZero);

    [Fact]
    public void Float_IsZero_False() =>
        Assert.False(new Float(1.0f).IsZero);

    [Fact]
    public void Float_Characteristic_Zero() =>
        Assert.Equal(0, Float.Characteristic);

    [Fact]
    public void Float_ImplicitFromFloat()
    {
        Float f = 1.5f;
        Assert.Equal(new Float(1.5f), f);
    }

    [Fact]
    public void Float_ExplicitToFloat()
    {
        var f = new Float(2.5f);
        Assert.Equal(2.5f, (float)f);
    }
}
