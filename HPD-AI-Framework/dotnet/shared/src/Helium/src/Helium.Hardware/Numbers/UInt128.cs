namespace Helium.Hardware;

/// <summary>
/// Unsigned 128-bit hardware lane with wraparound semantics.
/// This is not an arbitrary-precision exact integer.
/// </summary>
public readonly struct UInt128 : IEquatable<UInt128>, IComparable<UInt128>, IFormattable
{
    public ulong Lo { get; }
    public ulong Hi { get; }

    public UInt128(ulong lo, ulong hi = 0)
    {
        Lo = lo;
        Hi = hi;
    }

    public static UInt128 FromSystem(System.UInt128 value) =>
        new((ulong)value, (ulong)(value >> 64));

    public System.UInt128 ToSystem() => ((System.UInt128)Hi << 64) | Lo;

    public static UInt128 Zero => new(0);
    public static UInt128 One => new(1);

    public static UInt128 operator +(UInt128 left, UInt128 right) =>
        FromSystem(left.ToSystem() + right.ToSystem());

    public static UInt128 operator -(UInt128 left, UInt128 right) =>
        FromSystem(left.ToSystem() - right.ToSystem());

    public static UInt128 operator *(UInt128 left, UInt128 right) =>
        FromSystem(left.ToSystem() * right.ToSystem());

    public static bool operator ==(UInt128 left, UInt128 right) => left.Equals(right);
    public static bool operator !=(UInt128 left, UInt128 right) => !left.Equals(right);
    public static bool operator <(UInt128 left, UInt128 right) => left.ToSystem() < right.ToSystem();
    public static bool operator >(UInt128 left, UInt128 right) => left.ToSystem() > right.ToSystem();
    public static bool operator <=(UInt128 left, UInt128 right) => left.ToSystem() <= right.ToSystem();
    public static bool operator >=(UInt128 left, UInt128 right) => left.ToSystem() >= right.ToSystem();

    public int CompareTo(UInt128 other) => ToSystem().CompareTo(other.ToSystem());
    public bool Equals(UInt128 other) => Lo == other.Lo && Hi == other.Hi;
    public override bool Equals(object? obj) => obj is UInt128 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Lo, Hi);
    public override string ToString() => ToSystem().ToString();
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        ToSystem().ToString(format, formatProvider);
}
