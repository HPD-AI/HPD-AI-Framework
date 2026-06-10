namespace Rhodium.Primitives;

/// <summary>
/// Unique identifier for replay corporate actions.
/// </summary>
public readonly record struct CorporateActionId(long Value)
{
    private static long _next;

    public static CorporateActionId New() => new(Interlocked.Increment(ref _next));

    public static implicit operator CorporateActionId(long value) => new(value);

    public override string ToString() => Value.ToString();
}
