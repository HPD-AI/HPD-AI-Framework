namespace Rhodium.Primitives;

/// <summary>
/// Stable identifier for replay-applied financing charges.
/// </summary>
public readonly record struct FinancingChargeId(long Value)
{
    private static long _next;

    public static FinancingChargeId New() => new(Interlocked.Increment(ref _next));

    public static implicit operator FinancingChargeId(long value) => new(value);
}
