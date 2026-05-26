using Helium.Finance.Options;
using Helium.Finance.Validated;
using Helium.Validated;

namespace Helium.Finance.Tests;

public class ValidatedFinanceTests
{
    [Fact]
    public void NamedConversionCreatesContainingInterval()
    {
        var interval = ValidatedFinance.ToInterval(100.0, 0.01);

        Assert.True(interval.Contains(99.99));
        Assert.True(interval.Contains(100.01));
    }

    [Fact]
    public void ValidatedDiscountFactorContainsPointCalculation()
    {
        var rate = new Interval(0.049, 0.051);
        var time = new Interval(1.99, 2.01);

        var discount = ValidatedFinance.DiscountFactor(rate, time);

        Assert.True(discount.Contains(Math.Exp(-0.05 * 2.0)));
    }

    [Fact]
    public void PutCallParityIntervalContainsBlack76Parity()
    {
        var call = Black76.Price(new Black76Input(OptionRight.Call, 105.0, 100.0, 1.0, 0.20, 0.97));
        var put = Black76.Price(new Black76Input(OptionRight.Put, 105.0, 100.0, 1.0, 0.20, 0.97));

        var contains = ValidatedFinance.ContainsPutCallParity(
            ValidatedFinance.ToInterval(call, 1e-12),
            ValidatedFinance.ToInterval(put, 1e-12),
            Interval.Point(0.97),
            Interval.Point(105.0),
            Interval.Point(100.0));

        Assert.True(contains);
    }

    [Fact]
    public void Black76NoArbitrageBoundsContainModelPrice()
    {
        var input = new Black76InputWithoutVolatility(OptionRight.Call, 100.0, 100.0, 1.0, 0.95);
        var price = Black76.Price(input.WithVolatility(0.25));

        var bounds = NoArbitrageBounds.Black76(input);

        Assert.True(bounds.Contains(price));
        Assert.True(ValidatedFinance.ProvesNonNegative(bounds.LowerPrice));
    }

    [Fact]
    public void BachelierNoArbitrageBoundsContainModelPriceWithNegativeForward()
    {
        var input = new BachelierInputWithoutVolatility(OptionRight.Put, -1.0, 0.5, 1.0, 0.98);
        var price = Bachelier.Price(input.WithNormalVolatility(12.0));

        var bounds = NoArbitrageBounds.Bachelier(input);

        Assert.True(bounds.Contains(price));
    }

    [Fact]
    public void NoArbitrageBoundsRejectInconsistentIntervals()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NoArbitrageBounds(
            new Interval(-0.01, 0.0),
            new Interval(0.0, 1.0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NoArbitrageBounds(
            Interval.Point(2.0),
            Interval.Point(1.0)));
    }
}
