using Rhodium.Primitives;

namespace Rhodium.Simulation.Exchange;

public sealed record OptionLifecycleRequest
{
    public OptionLifecycleRequest(
        InstrumentContract contract,
        Qty quantity,
        OptionLifecycleReference reference,
        Instant now,
        SimulationOptionAssignmentInput? assignmentInput = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(reference);

        if (contract.Payoff is not PayoffTerms.Option)
            throw new ArgumentException($"Option lifecycle request requires an option contract, but {contract.Instrument} has payoff {contract.Payoff.GetType().Name}.", nameof(contract));

        if (quantity.IsZero)
            throw new ArgumentException("Option lifecycle request requires a nonzero position quantity.", nameof(quantity));

        if (quantity.IsPositive && assignmentInput is not null)
            throw new ArgumentException("Option assignment input is only valid for short option lifecycle requests.", nameof(assignmentInput));

        Contract = contract;
        Quantity = quantity;
        Reference = reference;
        Now = now;
        AssignmentInput = assignmentInput;
    }

    public InstrumentContract Contract { get; }
    public Qty Quantity { get; }
    public OptionLifecycleReference Reference { get; }
    public Instant Now { get; }
    public SimulationOptionAssignmentInput? AssignmentInput { get; }
}
