using Helium.Finance.Options;

namespace Helium.Finance.Volatility;

public readonly record struct BachelierVolatilityQuote(
    OptionRight Right,
    double Forward,
    double Strike,
    double TimeToExpiry,
    double DiscountFactor,
    double MarketPrice)
{
    public BachelierInputWithoutVolatility Input =>
        new(Right, Forward, Strike, TimeToExpiry, DiscountFactor);
}
