namespace Helium.Finance.Options;

public readonly record struct Black76InputWithoutVolatility(
    OptionRight Right,
    double Forward,
    double Strike,
    double TimeToExpiry,
    double DiscountFactor = 1.0)
{
    public Black76Input WithVolatility(double volatility) =>
        new(Right, Forward, Strike, TimeToExpiry, volatility, DiscountFactor);
}
