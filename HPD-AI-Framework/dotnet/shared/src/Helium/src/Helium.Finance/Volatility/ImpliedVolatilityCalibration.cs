using Helium.Finance.Options;

namespace Helium.Finance.Volatility;

public static class ImpliedVolatilityCalibration
{
    public static VolatilityCalibrationResult CalibrateBlack76(
        IReadOnlyList<Black76VolatilityQuote> quotes,
        ImpliedVolatilityOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(quotes);

        var snapshot = quotes.ToArray();
        if (snapshot.Length == 0)
            throw new ArgumentException("Calibration requires at least one quote.", nameof(quotes));

        var points = new VolatilityCalibrationPoint[snapshot.Length];
        for (var i = 0; i < snapshot.Length; i++)
        {
            var quote = snapshot[i];
            var result = Black76.ImpliedVolatility(quote.Input, quote.MarketPrice, options);
            points[i] = new VolatilityCalibrationPoint(
                quote.TimeToExpiry,
                quote.Strike,
                quote.MarketPrice,
                result);
        }

        return new VolatilityCalibrationResult(points);
    }

    public static VolatilityCalibrationResult CalibrateBachelier(
        IReadOnlyList<BachelierVolatilityQuote> quotes,
        ImpliedVolatilityOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(quotes);

        var snapshot = quotes.ToArray();
        if (snapshot.Length == 0)
            throw new ArgumentException("Calibration requires at least one quote.", nameof(quotes));

        var points = new VolatilityCalibrationPoint[snapshot.Length];
        for (var i = 0; i < snapshot.Length; i++)
        {
            var quote = snapshot[i];
            var result = Bachelier.ImpliedVolatility(quote.Input, quote.MarketPrice, options);
            points[i] = new VolatilityCalibrationPoint(
                quote.TimeToExpiry,
                quote.Strike,
                quote.MarketPrice,
                result);
        }

        return new VolatilityCalibrationResult(points);
    }
}
