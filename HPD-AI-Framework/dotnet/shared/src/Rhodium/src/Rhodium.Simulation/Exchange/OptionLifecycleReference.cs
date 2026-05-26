using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Simulation.Exchange;

public sealed class OptionLifecycleReference
{
    public OptionLifecycleReference(
        Price? price,
        OptionLifecycleReferenceSource source,
        string? blockReason = null)
    {
        if (!Enum.IsDefined(source))
            throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown option lifecycle reference source.");

        if (price is null && source != OptionLifecycleReferenceSource.None)
            throw new ArgumentException("Missing option lifecycle reference price must use reference source None.", nameof(source));

        if (price is not null && source == OptionLifecycleReferenceSource.None)
            throw new ArgumentException("Resolved option lifecycle reference price requires a non-None reference source.", nameof(source));

        if (price is null && string.IsNullOrWhiteSpace(blockReason))
            throw new ArgumentException("Missing option lifecycle reference price requires a block reason.", nameof(blockReason));

        if (price is not null && blockReason is not null)
            throw new ArgumentException("Resolved option lifecycle reference price cannot carry a block reason.", nameof(blockReason));

        Price = price;
        Source = source;
        BlockReason = blockReason;
    }

    public Price? Price { get; }
    public OptionLifecycleReferenceSource Source { get; }
    public string? BlockReason { get; }
}
