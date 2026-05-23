using Helium.Algebra;
using Helium.Primitives;

namespace Helium.Algebra.Tests;

public class FiniteSupportSeriesTests
{
    [Fact]
    public void Monomial_StoresSingleCoefficient()
    {
        var series = FiniteSupportSeries<Nat, Integer>.Monomial(new Nat(3), 7);

        Assert.Equal((Integer)7, series.Coefficient(new Nat(3)));
        Assert.Equal(Integer.Zero, series.Coefficient(new Nat(2)));
    }

    [Fact]
    public void FromTerms_SumsRepeatedExponentsAndDropsZero()
    {
        var series = FiniteSupportSeries<Nat, Integer>.FromTerms(
        [
            (new Nat(1), (Integer)2),
            (new Nat(1), (Integer)3),
            (new Nat(2), (Integer)0)
        ]);

        Assert.Equal((Integer)5, series.Coefficient(new Nat(1)));
        Assert.Equal(Integer.Zero, series.Coefficient(new Nat(2)));
        Assert.Single(series.Components);
    }

    [Fact]
    public void Addition_IsPointwise()
    {
        var left = FiniteSupportSeries<Nat, Integer>.FromTerms([(new Nat(0), (Integer)1), (new Nat(2), (Integer)3)]);
        var right = FiniteSupportSeries<Nat, Integer>.FromTerms([(new Nat(2), (Integer)4), (new Nat(3), (Integer)5)]);

        var sum = left + right;

        Assert.Equal((Integer)1, sum.Coefficient(new Nat(0)));
        Assert.Equal((Integer)7, sum.Coefficient(new Nat(2)));
        Assert.Equal((Integer)5, sum.Coefficient(new Nat(3)));
    }

    [Fact]
    public void Multiplication_IsFiniteConvolution()
    {
        var onePlusT = FiniteSupportSeries<Nat, Integer>.FromTerms([(new Nat(0), (Integer)1), (new Nat(1), (Integer)1)]);

        var square = onePlusT * onePlusT;

        Assert.Equal((Integer)1, square.Coefficient(new Nat(0)));
        Assert.Equal((Integer)2, square.Coefficient(new Nat(1)));
        Assert.Equal((Integer)1, square.Coefficient(new Nat(2)));
    }

    [Fact]
    public void RingIdentities_HoldForSamples()
    {
        var value = FiniteSupportSeries<Nat, Integer>.FromTerms([(new Nat(1), (Integer)2)]);

        Assert.Equal(value, value + FiniteSupportSeries<Nat, Integer>.Zero);
        Assert.Equal(value, value * FiniteSupportSeries<Nat, Integer>.One);
        Assert.Equal(FiniteSupportSeries<Nat, Integer>.Zero, value - value);
    }

    [Fact]
    public void RingLaws_HoldForFiniteSamples()
    {
        var a = FiniteSupportSeries<Nat, Integer>.FromTerms([(new Nat(0), (Integer)2), (new Nat(1), (Integer)3)]);
        var b = FiniteSupportSeries<Nat, Integer>.FromTerms([(new Nat(1), (Integer)(-1)), (new Nat(3), (Integer)4)]);
        var c = FiniteSupportSeries<Nat, Integer>.FromTerms([(new Nat(0), (Integer)5), (new Nat(2), (Integer)6)]);

        Assert.Equal((a + b) + c, a + (b + c));
        Assert.Equal(a + b, b + a);
        Assert.Equal((a * b) * c, a * (b * c));
        Assert.Equal(a * b, b * a);
        Assert.Equal(a * (b + c), (a * b) + (a * c));
    }

    [Fact]
    public void FromInt_CreatesConstantSeries()
    {
        var value = FiniteSupportSeries<Nat, Integer>.FromInt(5);

        Assert.Equal((Integer)5, value.Coefficient(new Nat(0)));
        Assert.Equal(Integer.Zero, value.Coefficient(new Nat(1)));
    }

    [Fact]
    public void TruncatedSeries_DropsTermsAtOrAboveBound()
    {
        var value = FiniteSupportSeries<Nat, Integer>.FromTerms(
        [
            (new Nat(0), (Integer)1),
            (new Nat(2), (Integer)2),
            (new Nat(3), (Integer)3)
        ]);

        var truncated = new TruncatedSeries<Nat, Integer>(value, new Nat(3));

        Assert.Equal((Integer)1, truncated.Coefficient(new Nat(0)));
        Assert.Equal((Integer)2, truncated.Coefficient(new Nat(2)));
        Assert.Equal(Integer.Zero, truncated.Coefficient(new Nat(3)));
    }

    [Fact]
    public void TruncatedSeries_RejectsDifferentBounds()
    {
        var value = FiniteSupportSeries<Nat, Integer>.One;
        var left = new TruncatedSeries<Nat, Integer>(value, new Nat(2));
        var right = new TruncatedSeries<Nat, Integer>(value, new Nat(3));

        Assert.Throws<InvalidOperationException>(() => left.Add(right));
        Assert.Throws<InvalidOperationException>(() => left.Multiply(right));
    }

    [Fact]
    public void TruncatedSeries_OperationsRetruncateResults()
    {
        var a = new TruncatedSeries<Nat, Integer>(
            FiniteSupportSeries<Nat, Integer>.FromTerms([(new Nat(1), (Integer)1), (new Nat(2), (Integer)1)]),
            new Nat(3));
        var b = new TruncatedSeries<Nat, Integer>(
            FiniteSupportSeries<Nat, Integer>.FromTerms([(new Nat(1), (Integer)1)]),
            new Nat(3));

        var product = a.Multiply(b);

        Assert.Equal((Integer)1, product.Coefficient(new Nat(2)));
        Assert.Equal(Integer.Zero, product.Coefficient(new Nat(3)));
    }
}
