namespace Helium.Validated;

/// <summary>
/// A closed real interval [Lo, Hi]. Operations are containment-preserving:
/// if the inputs contain the true values, the output contains the true result.
/// </summary>
public readonly struct Interval : IEquatable<Interval>, IFormattable
{
    public double Lo { get; }
    public double Hi { get; }

    public Interval(double lo, double hi)
    {
        if (double.IsNaN(lo) || double.IsNaN(hi))
            throw new ArgumentException("Interval endpoints must not be NaN.");

        if (lo <= hi)
        {
            Lo = lo;
            Hi = hi;
        }
        else
        {
            Lo = hi;
            Hi = lo;
        }
    }

    public static Interval Point(double value) => new(value, value);

    public bool IsPoint => Lo.Equals(Hi);
    public double Width => Hi - Lo;
    public double Midpoint => (Lo + Hi) * 0.5;
    public bool Contains(double value) => Lo <= value && value <= Hi;
    public bool ContainsZero => Lo <= 0.0 && 0.0 <= Hi;
    public bool Overlaps(Interval other) => Lo <= other.Hi && other.Lo <= Hi;

    public static Interval operator +(Interval a, Interval b) =>
        Expand(new Interval(a.Lo + b.Lo, a.Hi + b.Hi));

    public static Interval operator -(Interval a, Interval b) =>
        Expand(new Interval(a.Lo - b.Hi, a.Hi - b.Lo));

    public static Interval operator -(Interval a) =>
        new(-a.Hi, -a.Lo);

    public static Interval operator *(Interval a, Interval b)
    {
        var p0 = a.Lo * b.Lo;
        var p1 = a.Lo * b.Hi;
        var p2 = a.Hi * b.Lo;
        var p3 = a.Hi * b.Hi;
        return Expand(new Interval(
            Math.Min(Math.Min(p0, p1), Math.Min(p2, p3)),
            Math.Max(Math.Max(p0, p1), Math.Max(p2, p3))));
    }

    public static Interval Divide(Interval a, Interval b)
    {
        if (b.ContainsZero)
            throw new ArithmeticException("Cannot divide by an interval containing zero.");

        return a * new Interval(1.0 / b.Hi, 1.0 / b.Lo);
    }

    internal static Interval Around(double value, double radius)
    {
        if (radius < 0.0 || double.IsNaN(radius))
            throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be non-negative.");
        return Expand(new Interval(value - radius, value + radius));
    }

    private static Interval Expand(Interval x)
    {
        var lo = Math.BitDecrement(x.Lo);
        var hi = Math.BitIncrement(x.Hi);
        return new Interval(lo, hi);
    }

    public bool Equals(Interval other) => Lo.Equals(other.Lo) && Hi.Equals(other.Hi);
    public override bool Equals(object? obj) => obj is Interval other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Lo, Hi);
    public static bool operator ==(Interval a, Interval b) => a.Equals(b);
    public static bool operator !=(Interval a, Interval b) => !a.Equals(b);

    public override string ToString() => ToString(null, null);

    public string ToString(string? format, IFormatProvider? provider) =>
        format switch
        {
            "L" => $"[{Lo.ToString(null, provider)}, {Hi.ToString(null, provider)}]",
            "M" => $"<mfenced open='[' close=']'><mrow><mn>{Lo}</mn></mrow><mo>,</mo><mrow><mn>{Hi}</mn></mrow></mfenced>",
            _ => $"[{Lo.ToString(format, provider)}, {Hi.ToString(format, provider)}]"
        };
}
