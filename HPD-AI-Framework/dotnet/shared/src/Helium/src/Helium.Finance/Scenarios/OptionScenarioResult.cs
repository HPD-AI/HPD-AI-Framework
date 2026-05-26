using Helium.Finance.Options;

namespace Helium.Finance.Scenarios;

public readonly record struct OptionScenarioResult(
    OptionGreeks Base,
    OptionGreeks Scenario)
{
    public double PriceChange => Difference(Scenario.Price, Base.Price);

    public double DeltaChange => Difference(Scenario.Delta, Base.Delta);

    public double GammaChange => Difference(Scenario.Gamma, Base.Gamma);

    public double VegaChange => Difference(Scenario.Vega, Base.Vega);

    public double ThetaChange => Difference(Scenario.Theta, Base.Theta);

    public double RhoChange => Difference(Scenario.Rho, Base.Rho);

    private static double Difference(double scenario, double baseline)
    {
        var value = scenario - baseline;
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(scenario), "Scenario change must be finite.");

        return value;
    }
}
