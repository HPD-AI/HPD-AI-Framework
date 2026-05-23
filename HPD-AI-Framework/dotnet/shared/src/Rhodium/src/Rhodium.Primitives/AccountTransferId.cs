namespace Rhodium.Primitives;

/// <summary>
/// Unique identifier for replay account and custody transfers.
/// </summary>
public readonly record struct AccountTransferId(long Value)
{
    private static long _next;

    public static AccountTransferId New() => new(Interlocked.Increment(ref _next));

    public static implicit operator AccountTransferId(long value) => new(value);

    public override string ToString() => Value.ToString();
}
