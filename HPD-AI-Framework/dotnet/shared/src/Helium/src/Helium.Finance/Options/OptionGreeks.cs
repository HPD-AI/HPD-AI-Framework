namespace Helium.Finance.Options;

public readonly record struct OptionGreeks
{
    public OptionGreeks(
        double price,
        double delta,
        double gamma,
        double vega,
        double theta,
        double rho)
    {
        ValidateFinite(price, nameof(price));
        ValidateFinite(delta, nameof(delta));
        ValidateFinite(gamma, nameof(gamma));
        ValidateFinite(vega, nameof(vega));
        ValidateFinite(theta, nameof(theta));
        ValidateFinite(rho, nameof(rho));

        Price = price;
        Delta = delta;
        Gamma = gamma;
        Vega = vega;
        Theta = theta;
        Rho = rho;
    }

    public double Price { get; }

    public double Delta { get; }

    public double Gamma { get; }

    public double Vega { get; }

    public double Theta { get; }

    public double Rho { get; }

    public void Deconstruct(
        out double price,
        out double delta,
        out double gamma,
        out double vega,
        out double theta,
        out double rho)
    {
        price = Price;
        delta = Delta;
        gamma = Gamma;
        vega = Vega;
        theta = Theta;
        rho = Rho;
    }

    private static void ValidateFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(parameterName, "Greek value must be finite.");
    }
}
