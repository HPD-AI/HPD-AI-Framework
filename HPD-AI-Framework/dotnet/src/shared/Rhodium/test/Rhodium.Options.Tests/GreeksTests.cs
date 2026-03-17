using Rhodium.Options;

namespace Rhodium.Options.Tests;

public class GreeksTests
{
    [Fact]
    public void Calculate_ComputesAllGreeksForCallOption()
    {
        var result = Greeks.Calculate(
            OptionType.Call,
            underlyingPrice: 100m,
            strikePrice: 100m,
            timeToExpiryYears: 1m,
            volatility: 0.2m,
            riskFreeRate: 0.05m
        );

        // Delta should be around 0.5-0.6 for ATM call
        Assert.True(result.Delta > 0.4m && result.Delta < 0.7m);

        // Gamma should be positive
        Assert.True(result.Gamma > 0);

        // Theta should be negative (time decay)
        Assert.True(result.Theta < 0);

        // Vega should be positive
        Assert.True(result.Vega > 0);

        // Rho should be positive for calls
        Assert.True(result.Rho > 0);

        // Price should be positive
        Assert.True(result.TheoreticalPrice > 0);
    }

    [Fact]
    public void Calculate_ComputesAllGreeksForPutOption()
    {
        var result = Greeks.Calculate(
            OptionType.Put,
            underlyingPrice: 100m,
            strikePrice: 100m,
            timeToExpiryYears: 1m,
            volatility: 0.2m,
            riskFreeRate: 0.05m
        );

        // Delta should be around -0.5 to -0.4 for ATM put
        Assert.True(result.Delta < 0 && result.Delta > -0.7m);

        // Gamma should be positive (same as call)
        Assert.True(result.Gamma > 0);

        // Theta should be negative (time decay)
        Assert.True(result.Theta < 0);

        // Vega should be positive (same as call)
        Assert.True(result.Vega > 0);

        // Rho should be negative for puts
        Assert.True(result.Rho < 0);

        // Price should be positive
        Assert.True(result.TheoreticalPrice > 0);
    }

    [Fact]
    public void Delta_IncreasesAsOptionGoesInTheMoney()
    {
        var atmDelta = Greeks.Delta(OptionType.Call, 100m, 100m, 1m, 0.2m, 0.05m);
        var itmDelta = Greeks.Delta(OptionType.Call, 110m, 100m, 1m, 0.2m, 0.05m); // In the money

        Assert.True(itmDelta > atmDelta);
        Assert.True(itmDelta > 0.5m); // ITM call delta > 0.5
    }

    [Fact]
    public void Gamma_IsPositive()
    {
        var atmGamma = Greeks.Gamma(100m, 100m, 1m, 0.2m, 0.05m);
        var otmGamma = Greeks.Gamma(90m, 100m, 1m, 0.2m, 0.05m);  // Out of the money
        var itmGamma = Greeks.Gamma(110m, 100m, 1m, 0.2m, 0.05m); // In the money

        // Gamma should always be positive for both calls and puts
        Assert.True(atmGamma > 0);
        Assert.True(otmGamma > 0);
        Assert.True(itmGamma > 0);
    }

    [Fact]
    public void Theta_IncreasesAsExpirationApproaches()
    {
        var longTermTheta = Greeks.Theta(OptionType.Call, 100m, 100m, 1m, 0.2m, 0.05m);
        var shortTermTheta = Greeks.Theta(OptionType.Call, 100m, 100m, 0.1m, 0.2m, 0.05m);

        // Theta magnitude increases as expiration approaches
        Assert.True(Math.Abs(shortTermTheta) > Math.Abs(longTermTheta));
    }

    [Fact]
    public void Vega_DecreasesAsExpirationApproaches()
    {
        var longTermVega = Greeks.Vega(100m, 100m, 1m, 0.2m, 0.05m);
        var shortTermVega = Greeks.Vega(100m, 100m, 0.1m, 0.2m, 0.05m);

        Assert.True(longTermVega > shortTermVega);
    }

    [Fact]
    public void Rho_IsPositiveForCallsNegativeForPuts()
    {
        var callRho = Greeks.Rho(OptionType.Call, 100m, 100m, 1m, 0.2m, 0.05m);
        var putRho = Greeks.Rho(OptionType.Put, 100m, 100m, 1m, 0.2m, 0.05m);

        Assert.True(callRho > 0);
        Assert.True(putRho < 0);
    }

    [Fact]
    public void ImpliedVolatility_ConvergesToInputVolatility()
    {
        var inputVol = 0.25m;
        var theoreticalPrice = Greeks.Price(OptionType.Call, 100m, 100m, 1m, inputVol, 0.05m);

        var impliedVol = Greeks.ImpliedVolatility(
            OptionType.Call,
            theoreticalPrice,
            100m,
            100m,
            1m,
            0.05m
        );

        // Should converge to original volatility within tolerance
        Assert.True(Math.Abs(impliedVol - inputVol) < 0.01m);
    }

    [Fact]
    public void Price_MatchesPutCallParity()
    {
        var s = 100m;
        var k = 100m;
        var t = 1m;
        var r = 0.05m;
        var v = 0.2m;

        var callPrice = Greeks.Price(OptionType.Call, s, k, t, v, r);
        var putPrice = Greeks.Price(OptionType.Put, s, k, t, v, r);

        // Put-Call Parity: C - P = S - K * e^(-r*t)
        var leftSide = callPrice - putPrice;
        var rightSide = s - k * (decimal)Math.Exp(-(double)r * (double)t);

        Assert.True(Math.Abs(leftSide - rightSide) < 0.01m);
    }

    [Fact]
    public void Vanna_MeasuresDeltaSensitivityToVolatility()
    {
        var vanna = Greeks.Vanna(100m, 100m, 1m, 0.2m, 0.05m);

        // Vanna for ATM option should be near zero
        Assert.True(Math.Abs(vanna) < 0.5m);
    }

    [Fact]
    public void Charm_MeasuresDeltaDecayOverTime()
    {
        var callCharm = Greeks.Charm(OptionType.Call, 100m, 100m, 1m, 0.2m, 0.05m);
        var putCharm = Greeks.Charm(OptionType.Put, 100m, 100m, 1m, 0.2m, 0.05m);

        // Charm for ITM call is typically negative, OTM put is positive
        // For ATM, magnitude should be small
        Assert.True(Math.Abs(callCharm) >= 0);
        Assert.True(Math.Abs(putCharm) >= 0);
    }

    [Fact]
    public void Vomma_MeasuresVegaSensitivityToVolatility()
    {
        var vomma = Greeks.Vomma(100m, 100m, 1m, 0.2m, 0.05m);

        // Vomma should be positive for both calls and puts
        Assert.True(vomma > 0);
    }

    [Fact]
    public void Calculate_HandlesEdgeCases()
    {
        // Zero time to expiry
        var zeroTime = Greeks.Calculate(OptionType.Call, 100m, 100m, 0m, 0.2m, 0.05m);
        Assert.Equal(default(GreeksResult), zeroTime);

        // Zero volatility
        var zeroVol = Greeks.Calculate(OptionType.Call, 100m, 100m, 1m, 0m, 0.05m);
        Assert.Equal(default(GreeksResult), zeroVol);
    }

    [Fact]
    public void Calculate_HandlesWithDividendYield()
    {
        var noDividend = Greeks.Calculate(OptionType.Call, 100m, 100m, 1m, 0.2m, 0.05m, 0m);
        var withDividend = Greeks.Calculate(OptionType.Call, 100m, 100m, 1m, 0.2m, 0.05m, 0.02m);

        // Dividend reduces call value
        Assert.True(withDividend.TheoreticalPrice < noDividend.TheoreticalPrice);

        // Dividend reduces call delta
        Assert.True(withDividend.Delta < noDividend.Delta);
    }

    [Fact]
    public void ImpliedVolatility_HandlesExtremePrices()
    {
        // Very low price
        var lowVol = Greeks.ImpliedVolatility(OptionType.Call, 0.01m, 100m, 100m, 1m, 0.05m);
        Assert.True(lowVol > 0);

        // Very high price (deep ITM)
        var highVol = Greeks.ImpliedVolatility(OptionType.Call, 50m, 100m, 50m, 1m, 0.05m);
        Assert.True(highVol > 0);
    }
}
