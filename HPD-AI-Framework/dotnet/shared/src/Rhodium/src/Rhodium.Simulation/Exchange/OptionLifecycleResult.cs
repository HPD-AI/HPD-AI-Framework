namespace Rhodium.Simulation.Exchange;

public sealed class OptionLifecycleResult
{
    public OptionLifecycleResult(IReadOnlyList<OptionLifecycleOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        if (outcomes.Count == 0)
            throw new ArgumentException("Option lifecycle result must contain at least one outcome.", nameof(outcomes));

        var snapshot = new OptionLifecycleOutcome[outcomes.Count];
        var hasBlock = false;
        int? sign = null;
        for (var i = 0; i < outcomes.Count; i++)
        {
            if (outcomes[i] is null)
                throw new ArgumentException("Option lifecycle result cannot contain null outcomes.", nameof(outcomes));

            snapshot[i] = outcomes[i];
            hasBlock |= outcomes[i] is OptionLifecycleOutcome.Block;
            var outcomeSign = Math.Sign(outcomes[i].Quantity.Value);
            if (sign is null)
            {
                sign = outcomeSign;
            }
            else if (sign.Value != outcomeSign)
            {
                throw new ArgumentException("Option lifecycle result outcomes must all have the same quantity sign.", nameof(outcomes));
            }
        }

        if (hasBlock && snapshot.Length != 1)
            throw new ArgumentException("Blocked option lifecycle result cannot contain settlement outcomes.", nameof(outcomes));

        Outcomes = snapshot;
        IsComplete = !hasBlock;
    }

    public IReadOnlyList<OptionLifecycleOutcome> Outcomes { get; }
    public bool IsComplete { get; }
}
