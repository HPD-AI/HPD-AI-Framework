namespace Helium.Finance.Options;

public readonly record struct BlackScholesInputWithoutVolatility(
    OptionRight Right,
    double Spot,
    double Strike,
    double TimeToExpiry,
    double RiskFreeRate,
    double DividendYield = 0.0)
{
    public BlackScholesInput WithVolatility(double volatility) =>
        new(Right, Spot, Strike, TimeToExpiry, volatility, RiskFreeRate, DividendYield);
}
