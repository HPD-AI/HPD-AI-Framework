namespace Rhodium.Primitives;

/// <summary>
/// Unique identifier for replay settlement receivables.
/// </summary>
public readonly record struct SettlementId(long Value)
{
    private static long _next;

    public static SettlementId New() => new(Interlocked.Increment(ref _next));

    public static implicit operator SettlementId(long value) => new(value);

    public override string ToString() => Value.ToString();
}
