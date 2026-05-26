namespace Helium.Finance.Options;

public readonly record struct BachelierInputWithoutVolatility(
    OptionRight Right,
    double Forward,
    double Strike,
    double TimeToExpiry,
    double DiscountFactor = 1.0)
{
    public BachelierInput WithNormalVolatility(double normalVolatility) =>
        new(Right, Forward, Strike, TimeToExpiry, normalVolatility, DiscountFactor);
}
