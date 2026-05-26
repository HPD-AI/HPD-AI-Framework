using Helium.Finance.Options;

namespace Helium.Finance.Scenarios;

public static class OptionScenarioGrid
{
    public static void EvaluateBlackScholes(
        BlackScholesInput input,
        ReadOnlySpan<OptionScenarioShock> shocks,
        Span<OptionScenarioResult> destination)
    {
        EnsureDestination(shocks.Length, destination);
        var baseGreeks = BlackScholes.PriceAndGreeks(input);

        for (var i = 0; i < shocks.Length; i++)
        {
            var shocked = shocks[i].Apply(input);
            destination[i] = new OptionScenarioResult(baseGreeks, BlackScholes.PriceAndGreeks(shocked));
        }
    }

    public static void EvaluateBlack76(
        Black76Input input,
        ReadOnlySpan<OptionScenarioShock> shocks,
        Span<OptionScenarioResult> destination)
    {
        EnsureDestination(shocks.Length, destination);
        var baseGreeks = Black76.PriceAndGreeks(input);

        for (var i = 0; i < shocks.Length; i++)
        {
            var shocked = shocks[i].Apply(input);
            destination[i] = new OptionScenarioResult(baseGreeks, Black76.PriceAndGreeks(shocked));
        }
    }

    public static void EvaluateBachelier(
        BachelierInput input,
        ReadOnlySpan<OptionScenarioShock> shocks,
        Span<OptionScenarioResult> destination)
    {
        EnsureDestination(shocks.Length, destination);
        var baseGreeks = Bachelier.PriceAndGreeks(input);

        for (var i = 0; i < shocks.Length; i++)
        {
            var shocked = shocks[i].Apply(input);
            destination[i] = new OptionScenarioResult(baseGreeks, Bachelier.PriceAndGreeks(shocked));
        }
    }

    public static void EvaluateBlackScholes(
        ReadOnlySpan<BlackScholesInput> inputs,
        OptionScenarioShock shock,
        Span<OptionScenarioResult> destination)
    {
        EnsureDestination(inputs.Length, destination);

        for (var i = 0; i < inputs.Length; i++)
            destination[i] = OptionScenarioEvaluator.EvaluateBlackScholes(inputs[i], shock);
    }

    public static void EvaluateBlack76(
        ReadOnlySpan<Black76Input> inputs,
        OptionScenarioShock shock,
        Span<OptionScenarioResult> destination)
    {
        EnsureDestination(inputs.Length, destination);

        for (var i = 0; i < inputs.Length; i++)
            destination[i] = OptionScenarioEvaluator.EvaluateBlack76(inputs[i], shock);
    }

    public static void EvaluateBachelier(
        ReadOnlySpan<BachelierInput> inputs,
        OptionScenarioShock shock,
        Span<OptionScenarioResult> destination)
    {
        EnsureDestination(inputs.Length, destination);

        for (var i = 0; i < inputs.Length; i++)
            destination[i] = OptionScenarioEvaluator.EvaluateBachelier(inputs[i], shock);
    }

    private static void EnsureDestination(int requiredLength, Span<OptionScenarioResult> destination)
    {
        if (destination.Length < requiredLength)
            throw new ArgumentException("Destination span must be at least as long as the scenario grid.", nameof(destination));
    }
}
