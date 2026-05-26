namespace Helium.Finance.Options;

public readonly record struct FiniteDifferenceGreekEstimate
{
    public FiniteDifferenceGreekEstimate(
        double price,
        double delta,
        double gamma,
        double vega)
    {
        ValidateFinite(price, nameof(price));
        ValidateFinite(delta, nameof(delta));
        ValidateFinite(gamma, nameof(gamma));
        ValidateFinite(vega, nameof(vega));

        Price = price;
        Delta = delta;
        Gamma = gamma;
        Vega = vega;
    }

    public double Price { get; }

    public double Delta { get; }

    public double Gamma { get; }

    public double Vega { get; }

    public void Deconstruct(
        out double price,
        out double delta,
        out double gamma,
        out double vega)
    {
        price = Price;
        delta = Delta;
        gamma = Gamma;
        vega = Vega;
    }

    private static void ValidateFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(parameterName, "Finite-difference Greek estimate must be finite.");
    }
}
