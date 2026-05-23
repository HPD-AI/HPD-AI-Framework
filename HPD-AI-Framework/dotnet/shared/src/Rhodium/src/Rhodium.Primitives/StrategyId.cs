namespace Rhodium.Primitives;

/// <summary>
/// Stable identity for a registered strategy inside one RhodiumRuntime.
/// </summary>
public readonly struct StrategyId : IEquatable<StrategyId>
{
    private static int _next;

    public readonly int Value;

    public StrategyId(int value) => Value = value;

    public static StrategyId New() => new(Interlocked.Increment(ref _next));

    public bool Equals(StrategyId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is StrategyId other && Equals(other);
    public override int GetHashCode() => Value;
    public override string ToString() => $"Strategy({Value})";

    public static bool operator ==(StrategyId left, StrategyId right) => left.Equals(right);
    public static bool operator !=(StrategyId left, StrategyId right) => !left.Equals(right);
}
