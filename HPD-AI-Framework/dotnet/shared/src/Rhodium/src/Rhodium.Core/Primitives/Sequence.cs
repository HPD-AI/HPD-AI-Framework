namespace Rhodium.Primitives;

/// <summary>
/// Monotonically increasing sequence number for event ordering.
/// Essential for event sourcing, audit trails, and replay.
/// </summary>
public readonly record struct Sequence(ulong Value) : IComparable<Sequence>
{
    public static readonly Sequence Zero = new(0);

    public Sequence Next() => new(Value + 1);

    public int CompareTo(Sequence other) => Value.CompareTo(other.Value);

    public static bool operator >(Sequence a, Sequence b) => a.Value > b.Value;
    public static bool operator <(Sequence a, Sequence b) => a.Value < b.Value;
    public static bool operator >=(Sequence a, Sequence b) => a.Value >= b.Value;
    public static bool operator <=(Sequence a, Sequence b) => a.Value <= b.Value;

    public override string ToString() => Value.ToString();
}
