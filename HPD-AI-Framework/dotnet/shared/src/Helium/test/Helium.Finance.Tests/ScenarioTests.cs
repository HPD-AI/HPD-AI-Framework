using Helium.Finance.Options;
using Helium.Finance.Scenarios;

namespace Helium.Finance.Tests;

public class ScenarioTests
{
    [Fact]
    public void IdentityShockLeavesBlackScholesValuationUnchanged()
    {
        var input = new BlackScholesInput(OptionRight.Call, 100.0, 100.0, 1.0, 0.20, 0.03, 0.01);
        var result = OptionScenarioEvaluator.EvaluateBlackScholes(input, new OptionScenarioShock());

        AssertClose(0.0, result.PriceChange, 0.0);
        AssertClose(0.0, result.DeltaChange, 0.0);
        AssertClose(0.0, result.GammaChange, 0.0);
        AssertClose(0.0, result.VegaChange, 0.0);
    }

    [Fact]
    public void UpSpotShockIncreasesCallPrice()
    {
        var input = new BlackScholesInput(OptionRight.Call, 100.0, 100.0, 1.0, 0.20, 0.03, 0.01);
        var shock = new OptionScenarioShock(UnderlyingRelativeShift: 0.05);
        var result = OptionScenarioEvaluator.EvaluateBlackScholes(input, shock);

        Assert.True(result.PriceChange > 0.0, $"Expected positive price change, actual {result.PriceChange:R}.");
    }

    [Fact]
    public void UpVolShockIncreasesVanillaOptionPrices()
    {
        var call = new Black76Input(OptionRight.Call, 100.0, 100.0, 1.0, 0.20, 0.97);
        var put = new Black76Input(OptionRight.Put, 100.0, 100.0, 1.0, 0.20, 0.97);
        var shock = new OptionScenarioShock(VolatilityAbsoluteShift: 0.05);

        var callResult = OptionScenarioEvaluator.EvaluateBlack76(call, shock);
        var putResult = OptionScenarioEvaluator.EvaluateBlack76(put, shock);

        Assert.True(callResult.PriceChange > 0.0, $"Expected positive call price change, actual {callResult.PriceChange:R}.");
        Assert.True(putResult.PriceChange > 0.0, $"Expected positive put price change, actual {putResult.PriceChange:R}.");
    }

    [Fact]
    public void ShockClampsNegativeExpiryAndVolatilityToZero()
    {
        var input = new BlackScholesInput(OptionRight.Put, 100.0, 105.0, 0.25, 0.10, 0.03, 0.0);
        var shocked = new OptionScenarioShock(
            VolatilityAbsoluteShift: -1.0,
            TimeToExpiryAbsoluteShift: -10.0).Apply(input);

        AssertClose(0.0, shocked.TimeToExpiry, 0.0);
        AssertClose(0.0, shocked.Volatility, 0.0);
        AssertClose(5.0, BlackScholes.Price(shocked), 0.0);
    }

    [Fact]
    public void BlackScenarioShockRejectsNegativeShockedUnderlying()
    {
        var input = new BlackScholesInput(OptionRight.Call, 100.0, 100.0, 1.0, 0.20, 0.03, 0.01);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OptionScenarioShock(UnderlyingAbsoluteShift: -200.0).Apply(input));

        var black76 = new Black76Input(OptionRight.Call, 100.0, 100.0, 1.0, 0.20, 0.97);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OptionScenarioShock(UnderlyingAbsoluteShift: -200.0).Apply(black76));
    }

    [Fact]
    public void ScenarioShockRejectsNonfiniteShockedUnderlyingArithmetic()
    {
        var blackScholes = new BlackScholesInput(OptionRight.Call, double.MaxValue, 100.0, 1.0, 0.20, 0.03, 0.01);
        var black76 = new Black76Input(OptionRight.Call, double.MaxValue, 100.0, 1.0, 0.20, 0.97);
        var bachelier = new BachelierInput(OptionRight.Call, double.MaxValue, 100.0, 1.0, 0.20, 0.97);
        var shock = new OptionScenarioShock(UnderlyingRelativeShift: 1.0);

        Assert.Throws<ArgumentOutOfRangeException>(() => shock.Apply(blackScholes));
        Assert.Throws<ArgumentOutOfRangeException>(() => shock.Apply(black76));
        Assert.Throws<ArgumentOutOfRangeException>(() => shock.Apply(bachelier));
    }

    [Fact]
    public void Black76RateShockAdjustsDiscountFactor()
    {
        var input = new Black76Input(OptionRight.Call, 100.0, 100.0, 2.0, 0.20, 0.95);
        var shocked = new OptionScenarioShock(RiskFreeRateAbsoluteShift: 0.01).Apply(input);
        var expectedDiscount = input.DiscountFactor * Math.Exp(-0.01 * input.TimeToExpiry);

        AssertClose(expectedDiscount, shocked.DiscountFactor, 1e-12);
    }

    [Fact]
    public void RateShockRejectsNonfiniteDiscountFactor()
    {
        var input = new Black76Input(OptionRight.Call, 100.0, 100.0, 2.0, 0.20, 0.95);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OptionScenarioShock(RiskFreeRateAbsoluteShift: -1_000.0).Apply(input));

        var bachelier = new BachelierInput(OptionRight.Call, -0.25, 0.10, 2.0, 0.30, 0.99);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OptionScenarioShock(RiskFreeRateAbsoluteShift: -1_000.0).Apply(bachelier));
    }

    [Fact]
    public void BachelierScenarioSupportsNegativeForward()
    {
        var input = new BachelierInput(OptionRight.Call, -0.25, 0.10, 0.5, 0.30, 0.99);
        var shock = new OptionScenarioShock(UnderlyingAbsoluteShift: 0.20, VolatilityAbsoluteShift: 0.05);
        var result = OptionScenarioEvaluator.EvaluateBachelier(input, shock);

        Assert.True(double.IsFinite(result.Scenario.Price));
        Assert.True(result.PriceChange > 0.0);
    }

    [Fact]
    public void ScenarioShockRejectsInvalidConstructionAndMutation()
    {
        var shock = new OptionScenarioShock();

        Assert.Throws<ArgumentOutOfRangeException>(() => new OptionScenarioShock(
            UnderlyingRelativeShift: -1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OptionScenarioShock(
            UnderlyingAbsoluteShift: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OptionScenarioShock(
            VolatilityAbsoluteShift: double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OptionScenarioShock(
            RiskFreeRateAbsoluteShift: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OptionScenarioShock(
            DividendYieldAbsoluteShift: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OptionScenarioShock(
            TimeToExpiryAbsoluteShift: double.NaN));

        Assert.Throws<ArgumentOutOfRangeException>(() => shock with { UnderlyingRelativeShift = -1.0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => shock with { VolatilityAbsoluteShift = double.NaN });
    }

    [Fact]
    public void ScenarioGridAcrossShocksMatchesScalarBlackScholes()
    {
        var input = new BlackScholesInput(OptionRight.Call, 100.0, 100.0, 1.0, 0.20, 0.03, 0.01);
        OptionScenarioShock[] shocks =
        [
            new(UnderlyingRelativeShift: -0.05),
            new(),
            new(UnderlyingRelativeShift: 0.05, VolatilityAbsoluteShift: 0.02)
        ];
        Span<OptionScenarioResult> destination = stackalloc OptionScenarioResult[shocks.Length];

        OptionScenarioGrid.EvaluateBlackScholes(input, shocks, destination);

        for (var i = 0; i < shocks.Length; i++)
        {
            var scalar = OptionScenarioEvaluator.EvaluateBlackScholes(input, shocks[i]);
            AssertClose(scalar.PriceChange, destination[i].PriceChange, 0.0);
            AssertClose(scalar.Scenario.Price, destination[i].Scenario.Price, 0.0);
        }
    }

    [Fact]
    public void ScenarioGridAcrossInputsMatchesScalarBlack76()
    {
        Black76Input[] inputs =
        [
            new(OptionRight.Call, 100.0, 95.0, 0.5, 0.20, 0.98),
            new(OptionRight.Put, 100.0, 105.0, 1.0, 0.25, 0.96),
            new(OptionRight.Call, 100.0, 110.0, 1.5, 0.30, 0.94)
        ];
        var shock = new OptionScenarioShock(UnderlyingRelativeShift: 0.03, VolatilityAbsoluteShift: 0.01);
        Span<OptionScenarioResult> destination = stackalloc OptionScenarioResult[inputs.Length];

        OptionScenarioGrid.EvaluateBlack76(inputs, shock, destination);

        for (var i = 0; i < inputs.Length; i++)
        {
            var scalar = OptionScenarioEvaluator.EvaluateBlack76(inputs[i], shock);
            AssertClose(scalar.PriceChange, destination[i].PriceChange, 0.0);
            AssertClose(scalar.Scenario.Price, destination[i].Scenario.Price, 0.0);
        }
    }

    [Fact]
    public void ScenarioGridRejectsShortDestination()
    {
        var input = new BachelierInput(OptionRight.Call, 0.0, 0.1, 0.5, 0.30, 0.99);
        OptionScenarioShock[] shocks = [new(), new(UnderlyingAbsoluteShift: 0.1)];
        var destination = new OptionScenarioResult[1];

        Assert.Throws<ArgumentException>(() => OptionScenarioGrid.EvaluateBachelier(input, shocks, destination));
    }

    [Fact]
    public void ScenarioResultRejectsNonfiniteChangeProjection()
    {
        var result = new OptionScenarioResult(
            new OptionGreeks(-double.MaxValue, 0.0, 0.0, 0.0, 0.0, 0.0),
            new OptionGreeks(double.MaxValue, 0.0, 0.0, 0.0, 0.0, 0.0));

        Assert.Throws<ArgumentOutOfRangeException>(() => result.PriceChange);
    }

    private static void AssertClose(double expected, double actual, double tolerance) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}.");
}
