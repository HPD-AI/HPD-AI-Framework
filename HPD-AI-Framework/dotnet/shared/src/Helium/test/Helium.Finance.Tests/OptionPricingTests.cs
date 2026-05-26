using Helium.Finance.Options;

namespace Helium.Finance.Tests;

public class OptionPricingTests
{
    [Theory]
    [MemberData(nameof(Black76QuantLibFixtures))]
    public void Black76MatchesQuantLibSeedFixtures(OptionRight right, double strike, double forward, double stdDev, double discountFactor, double expected)
    {
        var input = new Black76Input(right, forward, strike, TimeToExpiry: 1.0, Volatility: stdDev, discountFactor);
        var actual = Black76.Price(input);

        AssertClose(expected, actual, 1e-12);
    }

    [Theory]
    [MemberData(nameof(BachelierQuantLibFixtures))]
    public void BachelierMatchesQuantLibSeedFixtures(OptionRight right, double strike, double forward, double stdDev, double discountFactor, double expected)
    {
        var input = new BachelierInput(right, forward, strike, TimeToExpiry: 1.0, NormalVolatility: stdDev, discountFactor);
        var actual = Bachelier.Price(input);

        AssertClose(expected, actual, 1e-12);
    }

    [Fact]
    public void Black76PutCallParityHolds()
    {
        var call = Black76.Price(new Black76Input(OptionRight.Call, 105.0, 100.0, 0.75, 0.22, 0.97));
        var put = Black76.Price(new Black76Input(OptionRight.Put, 105.0, 100.0, 0.75, 0.22, 0.97));

        AssertClose(0.97 * (105.0 - 100.0), call - put, 1e-12);
    }

    [Fact]
    public void BachelierPutCallParityHoldsWithNegativeForward()
    {
        var call = Bachelier.Price(new BachelierInput(OptionRight.Call, -1.0, -2.0, 0.75, 0.30, 0.99));
        var put = Bachelier.Price(new BachelierInput(OptionRight.Put, -1.0, -2.0, 0.75, 0.30, 0.99));

        AssertClose(0.99 * (-1.0 - -2.0), call - put, 1e-12);
    }

    [Fact]
    public void ZeroVolatilityReturnsDiscountedIntrinsic()
    {
        var call = Black76.Price(new Black76Input(OptionRight.Call, 110.0, 100.0, 1.0, 0.0, 0.95));
        var put = Bachelier.Price(new BachelierInput(OptionRight.Put, 90.0, 100.0, 1.0, 0.0, 0.95));

        AssertClose(9.5, call, 1e-15);
        AssertClose(9.5, put, 1e-15);
    }

    [Fact]
    public void Black76RejectsNonfiniteStandardDeviationAndDiscountedIntrinsic()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Black76.Price(new Black76Input(
            OptionRight.Call,
            Forward: 100.0,
            Strike: 100.0,
            TimeToExpiry: 4.0,
            Volatility: double.MaxValue,
            DiscountFactor: 1.0)));

        Assert.Throws<ArgumentOutOfRangeException>(() => Black76.Price(new Black76Input(
            OptionRight.Call,
            Forward: 2.0,
            Strike: 0.0,
            TimeToExpiry: 0.0,
            Volatility: 0.0,
            DiscountFactor: double.MaxValue)));
    }

    [Fact]
    public void Black76RejectsNonfiniteProjectedModelPrice()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Black76.Price(new Black76Input(
            OptionRight.Call,
            Forward: double.MaxValue,
            Strike: double.MaxValue,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            DiscountFactor: double.MaxValue)));
    }

    [Fact]
    public void BachelierRejectsNonfiniteStandardDeviationAndDiscountedIntrinsic()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Bachelier.Price(new BachelierInput(
            OptionRight.Call,
            Forward: 100.0,
            Strike: 100.0,
            TimeToExpiry: 4.0,
            NormalVolatility: double.MaxValue,
            DiscountFactor: 1.0)));

        Assert.Throws<ArgumentOutOfRangeException>(() => Bachelier.Price(new BachelierInput(
            OptionRight.Call,
            Forward: double.MaxValue,
            Strike: -double.MaxValue,
            TimeToExpiry: 0.0,
            NormalVolatility: 0.0,
            DiscountFactor: 1.0)));
    }

    [Fact]
    public void BachelierRejectsNonfiniteProjectedModelPrice()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Bachelier.Price(new BachelierInput(
            OptionRight.Call,
            Forward: 0.0,
            Strike: 0.0,
            TimeToExpiry: 1.0,
            NormalVolatility: 10.0,
            DiscountFactor: double.MaxValue)));
    }

    public static TheoryData<OptionRight, double, double, double, double, double> Black76QuantLibFixtures() => new()
    {
        { OptionRight.Call, 100.0, 100.0, 0.20, 1.0, 7.9655674554058038 },
        { OptionRight.Put,  100.0, 100.0, 0.20, 1.0, 7.9655674554058038 },
        { OptionRight.Call, 90.0,  100.0, 0.20, 1.0, 13.589108116054803 },
        { OptionRight.Put,  110.0, 100.0, 0.20, 1.0, 14.292010941409899 },
        { OptionRight.Call, 110.0, 100.0, 0.20, 1.0, 4.2920109414098846 },
        { OptionRight.Put,  90.0,  100.0, 0.20, 1.0, 3.5891081160548062 },
    };

    public static TheoryData<OptionRight, double, double, double, double, double> BachelierQuantLibFixtures() => new()
    {
        { OptionRight.Call, 100.0, 100.0, 20.0, 1.0, 7.9788456080286538 },
        { OptionRight.Put,  100.0, 100.0, 20.0, 1.0, 7.9788456080286538 },
        { OptionRight.Call, 90.0,  100.0, 20.0, 1.0, 13.955931148026121 },
        { OptionRight.Put,  110.0, 100.0, 20.0, 1.0, 13.955931148026121 },
        { OptionRight.Call, 110.0, 100.0, 20.0, 1.0, 3.9559311480261217 },
        { OptionRight.Put,  90.0,  100.0, 20.0, 1.0, 3.9559311480261217 },
    };

    private static void AssertClose(double expected, double actual, double tolerance) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}.");
}
