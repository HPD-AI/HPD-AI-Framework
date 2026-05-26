using Helium.Finance.Options;

namespace Helium.Finance.Volatility;

public readonly record struct Black76VolatilityQuote(
    OptionRight Right,
    double Forward,
    double Strike,
    double TimeToExpiry,
    double DiscountFactor,
    double MarketPrice)
{
    public Black76InputWithoutVolatility Input =>
        new(Right, Forward, Strike, TimeToExpiry, DiscountFactor);
}
