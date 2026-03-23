namespace Rhodium.Options;

/// <summary>
/// Option type.
/// </summary>
public enum OptionType { Call, Put }

/// <summary>
/// Complete Greeks result.
/// </summary>
public readonly record struct GreeksResult(
    decimal Delta,
    decimal Gamma,
    decimal Theta,
    decimal Vega,
    decimal Rho,
    decimal TheoreticalPrice
);

/// <summary>
/// Options Greeks calculations using Black-Scholes model.
/// </summary>
public static class Greeks
{
    /// <summary>
    /// Calculate all Greeks at once.
    /// </summary>
    public static GreeksResult Calculate(
        OptionType type,
        decimal underlyingPrice,
        decimal strikePrice,
        decimal timeToExpiryYears,
        decimal volatility,
        decimal riskFreeRate,
        decimal dividendYield = 0)
    {
        var s = (double)underlyingPrice;
        var k = (double)strikePrice;
        var t = (double)timeToExpiryYears;
        var v = (double)volatility;
        var r = (double)riskFreeRate;
        var q = (double)dividendYield;

        if (t <= 0 || v <= 0) return default;

        var sqrtT = Math.Sqrt(t);
        var d1 = (Math.Log(s / k) + (r - q + v * v / 2) * t) / (v * sqrtT);
        var d2 = d1 - v * sqrtT;

        var nd1 = NormCdf(type == OptionType.Call ? d1 : -d1);
        var nd2 = NormCdf(type == OptionType.Call ? d2 : -d2);
        var npd1 = NormPdf(d1);

        var expQt = Math.Exp(-q * t);
        var expRt = Math.Exp(-r * t);

        // Price
        double price;
        if (type == OptionType.Call)
            price = s * expQt * NormCdf(d1) - k * expRt * NormCdf(d2);
        else
            price = k * expRt * NormCdf(-d2) - s * expQt * NormCdf(-d1);

        // Delta
        double delta;
        if (type == OptionType.Call)
            delta = expQt * NormCdf(d1);
        else
            delta = -expQt * NormCdf(-d1);

        // Gamma (same for call and put)
        var gamma = expQt * npd1 / (s * v * sqrtT);

        // Theta
        double theta;
        var term1 = -s * expQt * npd1 * v / (2 * sqrtT);
        if (type == OptionType.Call)
            theta = term1 - r * k * expRt * NormCdf(d2) + q * s * expQt * NormCdf(d1);
        else
            theta = term1 + r * k * expRt * NormCdf(-d2) - q * s * expQt * NormCdf(-d1);
        theta /= 365; // Per day

        // Vega (same for call and put)
        var vega = s * expQt * npd1 * sqrtT / 100; // Per 1% vol change

        // Rho
        double rho;
        if (type == OptionType.Call)
            rho = k * t * expRt * NormCdf(d2) / 100; // Per 1% rate change
        else
            rho = -k * t * expRt * NormCdf(-d2) / 100;

        return new GreeksResult(
            (decimal)delta,
            (decimal)gamma,
            (decimal)theta,
            (decimal)vega,
            (decimal)rho,
            (decimal)price
        );
    }

    /// <summary>
    /// Delta - rate of change of option price with respect to underlying price.
    /// </summary>
    public static decimal Delta(
        OptionType type,
        decimal underlyingPrice,
        decimal strikePrice,
        decimal timeToExpiryYears,
        decimal volatility,
        decimal riskFreeRate,
        decimal dividendYield = 0) =>
        Calculate(type, underlyingPrice, strikePrice, timeToExpiryYears, volatility, riskFreeRate, dividendYield).Delta;

    /// <summary>
    /// Gamma - rate of change of delta with respect to underlying price.
    /// </summary>
    public static decimal Gamma(
        decimal underlyingPrice,
        decimal strikePrice,
        decimal timeToExpiryYears,
        decimal volatility,
        decimal riskFreeRate,
        decimal dividendYield = 0) =>
        Calculate(OptionType.Call, underlyingPrice, strikePrice, timeToExpiryYears, volatility, riskFreeRate, dividendYield).Gamma;

    /// <summary>
    /// Theta - rate of change of option price with respect to time (time decay).
    /// </summary>
    public static decimal Theta(
        OptionType type,
        decimal underlyingPrice,
        decimal strikePrice,
        decimal timeToExpiryYears,
        decimal volatility,
        decimal riskFreeRate,
        decimal dividendYield = 0) =>
        Calculate(type, underlyingPrice, strikePrice, timeToExpiryYears, volatility, riskFreeRate, dividendYield).Theta;

    /// <summary>
    /// Vega - rate of change of option price with respect to volatility.
    /// </summary>
    public static decimal Vega(
        decimal underlyingPrice,
        decimal strikePrice,
        decimal timeToExpiryYears,
        decimal volatility,
        decimal riskFreeRate,
        decimal dividendYield = 0) =>
        Calculate(OptionType.Call, underlyingPrice, strikePrice, timeToExpiryYears, volatility, riskFreeRate, dividendYield).Vega;

    /// <summary>
    /// Rho - rate of change of option price with respect to interest rate.
    /// </summary>
    public static decimal Rho(
        OptionType type,
        decimal underlyingPrice,
        decimal strikePrice,
        decimal timeToExpiryYears,
        decimal volatility,
        decimal riskFreeRate,
        decimal dividendYield = 0) =>
        Calculate(type, underlyingPrice, strikePrice, timeToExpiryYears, volatility, riskFreeRate, dividendYield).Rho;

    /// <summary>
    /// Implied Volatility - solve for volatility given market price.
    /// </summary>
    public static decimal ImpliedVolatility(
        OptionType type,
        decimal marketPrice,
        decimal underlyingPrice,
        decimal strikePrice,
        decimal timeToExpiryYears,
        decimal riskFreeRate,
        decimal dividendYield = 0,
        decimal tolerance = 0.0001m,
        int maxIterations = 100)
    {
        // Newton-Raphson method
        var vol = 0.2m; // Initial guess
        for (int i = 0; i < maxIterations; i++)
        {
            var result = Calculate(type, underlyingPrice, strikePrice, timeToExpiryYears, vol, riskFreeRate, dividendYield);
            var diff = result.TheoreticalPrice - marketPrice;
            if (Math.Abs(diff) < tolerance) return vol;
            if (result.Vega == 0) break;
            vol -= diff / (result.Vega * 100); // Vega is per 1% vol
            if (vol <= 0) vol = 0.01m;
            if (vol > 5) vol = 5m;
        }
        return vol;
    }

    /// <summary>
    /// Theoretical option price using Black-Scholes.
    /// </summary>
    public static decimal Price(
        OptionType type,
        decimal underlyingPrice,
        decimal strikePrice,
        decimal timeToExpiryYears,
        decimal volatility,
        decimal riskFreeRate,
        decimal dividendYield = 0) =>
        Calculate(type, underlyingPrice, strikePrice, timeToExpiryYears, volatility, riskFreeRate, dividendYield).TheoreticalPrice;

    // ==================== SECOND-ORDER GREEKS ====================

    /// <summary>
    /// Vanna - sensitivity of delta to volatility (or vega to underlying price).
    /// </summary>
    public static decimal Vanna(
        decimal underlyingPrice,
        decimal strikePrice,
        decimal timeToExpiryYears,
        decimal volatility,
        decimal riskFreeRate,
        decimal dividendYield = 0)
    {
        var s = (double)underlyingPrice;
        var k = (double)strikePrice;
        var t = (double)timeToExpiryYears;
        var v = (double)volatility;
        var r = (double)riskFreeRate;
        var q = (double)dividendYield;

        if (t <= 0 || v <= 0) return 0;

        var sqrtT = Math.Sqrt(t);
        var d1 = (Math.Log(s / k) + (r - q + v * v / 2) * t) / (v * sqrtT);
        var d2 = d1 - v * sqrtT;

        var vanna = -Math.Exp(-q * t) * NormPdf(d1) * d2 / v;
        return (decimal)vanna;
    }

    /// <summary>
    /// Charm - rate of change of delta over time (delta decay).
    /// </summary>
    public static decimal Charm(
        OptionType type,
        decimal underlyingPrice,
        decimal strikePrice,
        decimal timeToExpiryYears,
        decimal volatility,
        decimal riskFreeRate,
        decimal dividendYield = 0)
    {
        var s = (double)underlyingPrice;
        var k = (double)strikePrice;
        var t = (double)timeToExpiryYears;
        var v = (double)volatility;
        var r = (double)riskFreeRate;
        var q = (double)dividendYield;

        if (t <= 0 || v <= 0) return 0;

        var sqrtT = Math.Sqrt(t);
        var d1 = (Math.Log(s / k) + (r - q + v * v / 2) * t) / (v * sqrtT);
        var d2 = d1 - v * sqrtT;

        var term = 2 * (r - q) * t - d2 * v * sqrtT;
        var charm = q * Math.Exp(-q * t) * NormCdf(type == OptionType.Call ? d1 : -d1)
                    - Math.Exp(-q * t) * NormPdf(d1) * term / (2 * t * v * sqrtT);
        if (type == OptionType.Put) charm = -charm;

        return (decimal)charm / 365; // Per day
    }

    /// <summary>
    /// Vomma (Volga) - sensitivity of vega to volatility.
    /// </summary>
    public static decimal Vomma(
        decimal underlyingPrice,
        decimal strikePrice,
        decimal timeToExpiryYears,
        decimal volatility,
        decimal riskFreeRate,
        decimal dividendYield = 0)
    {
        var s = (double)underlyingPrice;
        var k = (double)strikePrice;
        var t = (double)timeToExpiryYears;
        var v = (double)volatility;
        var r = (double)riskFreeRate;
        var q = (double)dividendYield;

        if (t <= 0 || v <= 0) return 0;

        var sqrtT = Math.Sqrt(t);
        var d1 = (Math.Log(s / k) + (r - q + v * v / 2) * t) / (v * sqrtT);
        var d2 = d1 - v * sqrtT;

        var vega = s * Math.Exp(-q * t) * NormPdf(d1) * sqrtT;
        var vomma = vega * d1 * d2 / v;

        return (decimal)vomma / 100; // Per 1% vol change
    }

    // ==================== HELPERS ====================

    private static double NormCdf(double x)
    {
        const double a1 = 0.254829592;
        const double a2 = -0.284496736;
        const double a3 = 1.421413741;
        const double a4 = -1.453152027;
        const double a5 = 1.061405429;
        const double p = 0.3275911;

        var sign = x < 0 ? -1 : 1;
        x = Math.Abs(x) / Math.Sqrt(2);
        var t = 1.0 / (1.0 + p * x);
        var y = 1.0 - ((((a5 * t + a4) * t + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);
        return 0.5 * (1.0 + sign * y);
    }

    private static double NormPdf(double x) => Math.Exp(-x * x / 2) / Math.Sqrt(2 * Math.PI);
}
