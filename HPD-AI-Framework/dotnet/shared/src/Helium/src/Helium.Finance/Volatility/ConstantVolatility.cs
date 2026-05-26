namespace Helium.Finance.Volatility;

public readonly record struct ConstantVolatility
{
    public ConstantVolatility(double volatility)
    {
        if (!double.IsFinite(volatility) || volatility < 0.0)
            throw new ArgumentOutOfRangeException(nameof(volatility), "Volatility must be finite and nonnegative.");

        Volatility = volatility;
    }

    public double Volatility { get; }

    public double Value(double time) => Value(time, double.NaN);

    public double Value(double time, double strike)
    {
        if (!double.IsFinite(time) || time < 0.0)
            throw new ArgumentOutOfRangeException(nameof(time), "Time must be finite and nonnegative.");

        return Volatility;
    }
}
