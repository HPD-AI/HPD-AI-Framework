using Helium.Finance.Options;

namespace Helium.Finance.Scenarios;

public static class OptionScenarioEvaluator
{
    public static OptionScenarioResult EvaluateBlackScholes(
        BlackScholesInput input,
        OptionScenarioShock shock)
    {
        var shocked = shock.Apply(input);
        return new OptionScenarioResult(
            BlackScholes.PriceAndGreeks(input),
            BlackScholes.PriceAndGreeks(shocked));
    }

    public static OptionScenarioResult EvaluateBlack76(
        Black76Input input,
        OptionScenarioShock shock)
    {
        var shocked = shock.Apply(input);
        return new OptionScenarioResult(
            Black76.PriceAndGreeks(input),
            Black76.PriceAndGreeks(shocked));
    }

    public static OptionScenarioResult EvaluateBachelier(
        BachelierInput input,
        OptionScenarioShock shock)
    {
        var shocked = shock.Apply(input);
        return new OptionScenarioResult(
            Bachelier.PriceAndGreeks(input),
            Bachelier.PriceAndGreeks(shocked));
    }
}
