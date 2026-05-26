namespace Helium.Finance.Options;

public readonly record struct FiniteDifferenceBumps(
    double Underlying,
    double Volatility)
{
    public static FiniteDifferenceBumps Default { get; } = new(1e-3, 1e-5);

    public FiniteDifferenceBumps Normalize()
    {
        if (this == default)
            return Default;

        if (!double.IsFinite(Underlying) || Underlying <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(Underlying), "Underlying finite-difference bump must be finite and positive.");

        if (!double.IsFinite(Volatility) || Volatility <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(Volatility), "Volatility finite-difference bump must be finite and positive.");

        return this;
    }
}
