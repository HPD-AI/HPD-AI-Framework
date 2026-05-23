namespace Helium.Hardware;

/// <summary>
/// Signed 128-bit hardware lane with two's-complement wraparound semantics.
/// This is not an arbitrary-precision exact integer.
/// </summary>
public readonly struct Int128 : IEquatable<Int128>, IComparable<Int128>, IFormattable
{
    public long Lo { get; }
    public long Hi { get; }

    public Int128(long lo, long hi = 0)
    {
        Lo = lo;
        Hi = hi;
    }

    public static Int128 FromSystem(System.Int128 value) =>
        new((long)value, (long)(value >> 64));

    public System.Int128 ToSystem() =>
        ((System.Int128)Hi << 64) | (ulong)Lo;

    public static Int128 Zero => new(0);
    public static Int128 One => new(1);

    public static Int128 operator +(Int128 left, Int128 right) =>
        FromSystem(left.ToSystem() + right.ToSystem());

    public static Int128 operator -(Int128 left, Int128 right) =>
        FromSystem(left.ToSystem() - right.ToSystem());

    public static Int128 operator -(Int128 value) =>
        FromSystem(-value.ToSystem());

    public static Int128 operator *(Int128 left, Int128 right) =>
        FromSystem(left.ToSystem() * right.ToSystem());

    public static bool operator ==(Int128 left, Int128 right) => left.Equals(right);
    public static bool operator !=(Int128 left, Int128 right) => !left.Equals(right);
    public static bool operator <(Int128 left, Int128 right) => left.ToSystem() < right.ToSystem();
    public static bool operator >(Int128 left, Int128 right) => left.ToSystem() > right.ToSystem();
    public static bool operator <=(Int128 left, Int128 right) => left.ToSystem() <= right.ToSystem();
    public static bool operator >=(Int128 left, Int128 right) => left.ToSystem() >= right.ToSystem();

    public int CompareTo(Int128 other) => ToSystem().CompareTo(other.ToSystem());
    public bool Equals(Int128 other) => Lo == other.Lo && Hi == other.Hi;
    public override bool Equals(object? obj) => obj is Int128 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Lo, Hi);
    public override string ToString() => ToSystem().ToString();
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        ToSystem().ToString(format, formatProvider);
}
