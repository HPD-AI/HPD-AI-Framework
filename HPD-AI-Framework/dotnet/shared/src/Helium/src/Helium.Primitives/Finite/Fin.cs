namespace Helium.Primitives;

/// <summary>
/// Bounded natural number key.
/// </summary>
public readonly struct Fin : IEquatable<Fin>, IDecidableEq<Fin>, ITotalOrder<Fin>, IFormattable
{
    public int Value { get; }
    public int Bound { get; }

    public Fin(int value, int bound)
    {
        if (bound <= 0)
            throw new ArgumentOutOfRangeException(nameof(bound), "Fin requires a positive bound.");
        if (value < 0 || value >= bound)
            throw new ArgumentOutOfRangeException(nameof(value), "Fin value must satisfy 0 <= value < bound.");

        Value = value;
        Bound = bound;
    }

    public static bool DecidableEquals(Fin left, Fin right) =>
        left.Value == right.Value && left.Bound == right.Bound;

    public static bool LessEqual(Fin left, Fin right)
    {
        RequireSameBound(left, right);
        return left.Value <= right.Value;
    }

    public static Ordering CompareOrder(Fin left, Fin right)
    {
        RequireSameBound(left, right);
        return left.Value < right.Value ? Ordering.Less :
            left.Value > right.Value ? Ordering.Greater :
            Ordering.Equal;
    }

    public bool Equals(Fin other) => Value == other.Value && Bound == other.Bound;
    public override bool Equals(object? obj) => obj is Fin other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Value, Bound);
    public static bool operator ==(Fin left, Fin right) => left.Equals(right);
    public static bool operator !=(Fin left, Fin right) => !left.Equals(right);
    public override string ToString() => $"{Value}/{Bound}";
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        $"{Value.ToString(format, formatProvider)}/{Bound.ToString(format, formatProvider)}";

    private static void RequireSameBound(Fin left, Fin right)
    {
        if (left.Bound != right.Bound)
            throw new InvalidOperationException("Cannot order Fin values with different bounds.");
    }
}
