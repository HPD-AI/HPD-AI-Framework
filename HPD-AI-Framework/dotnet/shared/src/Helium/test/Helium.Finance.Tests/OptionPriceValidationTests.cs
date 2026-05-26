using Helium.Finance.Options;

namespace Helium.Finance.Tests;

public class OptionPriceValidationTests
{
    [Fact]
    public void Black76ModelPricePassesNoArbitrageValidation()
    {
        var input = new Black76Input(OptionRight.Call, 100.0, 95.0, 1.0, 0.22, 0.97);
        var result = OptionPriceValidation.ValidateBlack76Price(
            new Black76InputWithoutVolatility(input.Right, input.Forward, input.Strike, input.TimeToExpiry, input.DiscountFactor),
            Black76.Price(input));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Black76PriceBelowIntrinsicReturnsDiagnostic()
    {
        var result = OptionPriceValidation.ValidateBlack76Price(
            new Black76InputWithoutVolatility(OptionRight.Call, 110.0, 100.0, 1.0, 0.95),
            marketPrice: 1.0);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == OptionPriceDiagnosticCode.BelowLowerBound);
    }

    [Fact]
    public void Black76PriceAboveUpperBoundReturnsDiagnostic()
    {
        var result = OptionPriceValidation.ValidateBlack76Price(
            new Black76InputWithoutVolatility(OptionRight.Put, 100.0, 100.0, 1.0, 0.95),
            marketPrice: 200.0);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == OptionPriceDiagnosticCode.AboveUpperBound);
    }

    [Fact]
    public void BlackScholesModelPricePassesNoArbitrageValidation()
    {
        var input = new BlackScholesInput(OptionRight.Call, 100.0, 95.0, 1.0, 0.22, 0.03, 0.01);
        var result = OptionPriceValidation.ValidateBlackScholesPrice(
            new BlackScholesInputWithoutVolatility(input.Right, input.Spot, input.Strike, input.TimeToExpiry, input.RiskFreeRate, input.DividendYield),
            BlackScholes.Price(input));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void BlackScholesPriceOutsideBoundsReturnsDiagnostic()
    {
        var input = new BlackScholesInputWithoutVolatility(OptionRight.Call, 100.0, 90.0, 1.0, 0.03, 0.01);
        var belowIntrinsic = OptionPriceValidation.ValidateBlackScholesPrice(input, marketPrice: 1.0);
        var aboveUpper = OptionPriceValidation.ValidateBlackScholesPrice(input, marketPrice: 200.0);

        Assert.Contains(belowIntrinsic.Diagnostics, diagnostic => diagnostic.Code == OptionPriceDiagnosticCode.BelowLowerBound);
        Assert.Contains(aboveUpper.Diagnostics, diagnostic => diagnostic.Code == OptionPriceDiagnosticCode.AboveUpperBound);
    }

    [Fact]
    public void BachelierAllowsNegativeForwardWithinBounds()
    {
        var input = new BachelierInput(OptionRight.Call, -0.25, 0.10, 0.5, 0.30, 0.99);
        var result = OptionPriceValidation.ValidateBachelierPrice(
            new BachelierInputWithoutVolatility(input.Right, input.Forward, input.Strike, input.TimeToExpiry, input.DiscountFactor),
            Bachelier.Price(input));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void NonFiniteMarketPriceReturnsDiagnostic()
    {
        var result = OptionPriceValidation.ValidateBachelierPrice(
            new BachelierInputWithoutVolatility(OptionRight.Call, 0.0, 0.0, 1.0, 1.0),
            double.NaN);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == OptionPriceDiagnosticCode.NonFinitePrice);
    }

    [Fact]
    public void Black76InvalidNoArbitrageInputReturnsDiagnostics()
    {
        var result = OptionPriceValidation.ValidateBlack76Price(
            new Black76InputWithoutVolatility(OptionRight.Call, double.NaN, -1.0, -0.5, 0.0),
            marketPrice: 1.0);

        Assert.False(result.IsValid);
        Assert.Equal(4, result.Diagnostics.Count(diagnostic => diagnostic.Code == OptionPriceDiagnosticCode.InvalidInput));
    }

    [Fact]
    public void BachelierInvalidNoArbitrageInputReturnsDiagnostics()
    {
        var result = OptionPriceValidation.ValidateBachelierPrice(
            new BachelierInputWithoutVolatility(OptionRight.Call, double.NaN, double.PositiveInfinity, -0.5, double.NaN),
            marketPrice: 1.0);

        Assert.False(result.IsValid);
        Assert.Equal(4, result.Diagnostics.Count(diagnostic => diagnostic.Code == OptionPriceDiagnosticCode.InvalidInput));
    }

    [Fact]
    public void NoArbitrageBoundsRejectNonFiniteInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NoArbitrageBounds.Black76(new Black76InputWithoutVolatility(OptionRight.Call, double.NaN, 100.0, 1.0, 0.95)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NoArbitrageBounds.BlackScholes(new BlackScholesInputWithoutVolatility(OptionRight.Call, double.NaN, 100.0, 1.0, 0.03, 0.01)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NoArbitrageBounds.Bachelier(new BachelierInputWithoutVolatility(OptionRight.Call, 0.0, double.NaN, 1.0, 0.95)));
    }

    [Fact]
    public void NoArbitrageBoundsRejectNonfiniteProjectedBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NoArbitrageBounds.Black76(new Black76InputWithoutVolatility(
                OptionRight.Call,
                Forward: double.MaxValue,
                Strike: 0.0,
                TimeToExpiry: 1.0,
                DiscountFactor: 2.0)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NoArbitrageBounds.BlackScholes(new BlackScholesInputWithoutVolatility(
                OptionRight.Call,
                Spot: double.MaxValue,
                Strike: 0.0,
                TimeToExpiry: 1.0,
                RiskFreeRate: 0.0,
                DividendYield: -1_000.0)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NoArbitrageBounds.Bachelier(new BachelierInputWithoutVolatility(
                OptionRight.Call,
                Forward: double.MaxValue,
                Strike: -double.MaxValue,
                TimeToExpiry: 1.0,
                DiscountFactor: 1.0)));
    }

    [Fact]
    public void NoArbitrageBoundsContainmentRejectsNonfinitePrices()
    {
        var bachelier = NoArbitrageBounds.Bachelier(new BachelierInputWithoutVolatility(
            OptionRight.Call,
            Forward: 0.0,
            Strike: 0.0,
            TimeToExpiry: 1.0,
            DiscountFactor: 1.0));

        Assert.True(double.IsPositiveInfinity(bachelier.UpperPrice.Hi));
        Assert.False(bachelier.Contains(double.PositiveInfinity));
        Assert.False(bachelier.Contains(double.NaN));
        Assert.True(bachelier.Contains(1.0));
    }

    [Fact]
    public void PriceValidationReportsNonfiniteProjectedBounds()
    {
        var black = OptionPriceValidation.ValidateBlack76Price(
            new Black76InputWithoutVolatility(
                OptionRight.Call,
                Forward: double.MaxValue,
                Strike: 0.0,
                TimeToExpiry: 1.0,
                DiscountFactor: 2.0),
            marketPrice: 1.0);
        var blackScholes = OptionPriceValidation.ValidateBlackScholesPrice(
            new BlackScholesInputWithoutVolatility(
                OptionRight.Call,
                Spot: double.MaxValue,
                Strike: 0.0,
                TimeToExpiry: 1.0,
                RiskFreeRate: 0.0,
                DividendYield: -1_000.0),
            marketPrice: 1.0);
        var bachelier = OptionPriceValidation.ValidateBachelierPrice(
            new BachelierInputWithoutVolatility(
                OptionRight.Call,
                Forward: double.MaxValue,
                Strike: -double.MaxValue,
                TimeToExpiry: 1.0,
                DiscountFactor: 1.0),
            marketPrice: 1.0);

        Assert.False(black.IsValid);
        Assert.False(blackScholes.IsValid);
        Assert.False(bachelier.IsValid);
        Assert.Contains(black.Diagnostics, diagnostic => diagnostic.Code == OptionPriceDiagnosticCode.NonFiniteBound);
        Assert.Contains(blackScholes.Diagnostics, diagnostic => diagnostic.Code == OptionPriceDiagnosticCode.NonFiniteBound);
        Assert.Contains(bachelier.Diagnostics, diagnostic => diagnostic.Code == OptionPriceDiagnosticCode.NonFiniteBound);
    }

    [Fact]
    public void PutCallParityPassesForConsistentPrices()
    {
        var call = Black76.Price(new Black76Input(OptionRight.Call, 105.0, 100.0, 1.0, 0.25, 0.97));
        var put = Black76.Price(new Black76Input(OptionRight.Put, 105.0, 100.0, 1.0, 0.25, 0.97));

        var result = OptionPriceValidation.ValidatePutCallParity(105.0, 100.0, 0.97, call, put);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void BlackScholesPutCallParityPassesForConsistentPrices()
    {
        const double spot = 100.0;
        const double strike = 97.0;
        const double time = 1.25;
        const double volatility = 0.23;
        const double rate = 0.04;
        const double dividendYield = 0.01;

        var call = BlackScholes.Price(new BlackScholesInput(
            OptionRight.Call,
            spot,
            strike,
            time,
            volatility,
            rate,
            dividendYield));
        var put = BlackScholes.Price(new BlackScholesInput(
            OptionRight.Put,
            spot,
            strike,
            time,
            volatility,
            rate,
            dividendYield));

        var result = OptionPriceValidation.ValidateBlackScholesPutCallParity(
            spot,
            strike,
            time,
            rate,
            dividendYield,
            call,
            put);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void PutCallParityViolationReturnsDiagnostic()
    {
        var result = OptionPriceValidation.ValidatePutCallParity(
            forward: 105.0,
            strike: 100.0,
            discountFactor: 0.97,
            callPrice: 12.0,
            putPrice: 3.0,
            tolerance: 1e-12);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == OptionPriceDiagnosticCode.PutCallParityViolation);
    }

    [Fact]
    public void BlackScholesPutCallParityViolationReturnsDiagnostic()
    {
        var result = OptionPriceValidation.ValidateBlackScholesPutCallParity(
            spot: 100.0,
            strike: 97.0,
            timeToExpiry: 1.25,
            riskFreeRate: 0.04,
            dividendYield: 0.01,
            callPrice: 12.0,
            putPrice: 3.0,
            tolerance: 1e-12);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == OptionPriceDiagnosticCode.PutCallParityViolation);
    }

    [Fact]
    public void PutCallParityReportsNonfiniteProjection()
    {
        var result = OptionPriceValidation.ValidatePutCallParity(
            forward: double.MaxValue,
            strike: -double.MaxValue,
            discountFactor: 1.0,
            callPrice: 1.0,
            putPrice: 1.0);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == OptionPriceDiagnosticCode.NonFiniteBound);
    }

    [Fact]
    public void BlackScholesPutCallParityReportsNonfiniteProjection()
    {
        var result = OptionPriceValidation.ValidateBlackScholesPutCallParity(
            spot: double.MaxValue,
            strike: 100.0,
            timeToExpiry: 1.0,
            riskFreeRate: 0.0,
            dividendYield: -1_000.0,
            callPrice: 1.0,
            putPrice: 1.0);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == OptionPriceDiagnosticCode.NonFiniteBound);
    }

    [Fact]
    public void PriceValidationReportsInvalidTolerance()
    {
        var black = OptionPriceValidation.ValidateBlack76Price(
            new Black76InputWithoutVolatility(OptionRight.Call, 100.0, 100.0, 1.0, 0.95),
            marketPrice: 10.0,
            tolerance: -1.0);
        var blackScholes = OptionPriceValidation.ValidateBlackScholesPrice(
            new BlackScholesInputWithoutVolatility(OptionRight.Call, 100.0, 100.0, 1.0, 0.03, 0.01),
            marketPrice: 10.0,
            tolerance: double.NaN);
        var bachelier = OptionPriceValidation.ValidateBachelierPrice(
            new BachelierInputWithoutVolatility(OptionRight.Call, 0.0, 0.0, 1.0, 1.0),
            marketPrice: 0.1,
            tolerance: double.NaN);
        var parity = OptionPriceValidation.ValidatePutCallParity(
            forward: 100.0,
            strike: 100.0,
            discountFactor: 0.95,
            callPrice: 1.0,
            putPrice: 1.0,
            tolerance: double.PositiveInfinity);
        var blackScholesParity = OptionPriceValidation.ValidateBlackScholesPutCallParity(
            spot: 100.0,
            strike: 100.0,
            timeToExpiry: 1.0,
            riskFreeRate: 0.03,
            dividendYield: 0.01,
            callPrice: 1.0,
            putPrice: 1.0,
            tolerance: double.PositiveInfinity);

        Assert.Contains(black.Diagnostics, diagnostic => diagnostic.Code == OptionPriceDiagnosticCode.InvalidTolerance);
        Assert.Contains(blackScholes.Diagnostics, diagnostic => diagnostic.Code == OptionPriceDiagnosticCode.InvalidTolerance);
        Assert.Contains(bachelier.Diagnostics, diagnostic => diagnostic.Code == OptionPriceDiagnosticCode.InvalidTolerance);
        Assert.Contains(parity.Diagnostics, diagnostic => diagnostic.Code == OptionPriceDiagnosticCode.InvalidTolerance);
        Assert.Contains(blackScholesParity.Diagnostics, diagnostic => diagnostic.Code == OptionPriceDiagnosticCode.InvalidTolerance);
    }

    [Fact]
    public void OptionPriceValidationResultSnapshotsDiagnostics()
    {
        var diagnostics = new List<OptionPriceDiagnostic>
        {
            new(OptionPriceDiagnosticCode.NonFinitePrice, "bad price")
        };

        var result = new OptionPriceValidationResult(diagnostics);
        diagnostics.Clear();

        Assert.False(result.IsValid);
        Assert.Single(result.Diagnostics);
        Assert.Equal(OptionPriceDiagnosticCode.NonFinitePrice, result.Diagnostics[0].Code);
    }

    [Fact]
    public void OptionPriceValidationResultRejectsMalformedDiagnostics()
    {
        Assert.Throws<ArgumentException>(() => new OptionPriceValidationResult([default]));
    }

    [Fact]
    public void OptionPriceDiagnosticRejectsEmptyMessage()
    {
        Assert.Throws<ArgumentException>(() => new OptionPriceDiagnostic(
            OptionPriceDiagnosticCode.NonFinitePrice,
            ""));
    }

    [Fact]
    public void OptionPriceDiagnosticRejectsInvalidCode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OptionPriceDiagnostic(
            (OptionPriceDiagnosticCode)999,
            "bad code"));
    }
}
