namespace Helium.Hardware;

/// <summary>
/// Q31.32 fixed-point hardware lane. Arithmetic uses fixed-width wraparound
/// semantics and is not exact real arithmetic.
/// </summary>
public readonly struct Fix64 : IEquatable<Fix64>, IComparable<Fix64>, IFormattable
{
    public const int FractionalBits = 32;
    public const long OneRaw = 1L << FractionalBits;

    public long RawBits { get; }

    private Fix64(long rawBits) => RawBits = rawBits;

    public static Fix64 FromRawBits(long rawBits) => new(rawBits);

    public static Fix64 FromDouble(double value) =>
        new(unchecked((long)Math.Round(value * OneRaw)));

    public double ToDouble() => RawBits / (double)OneRaw;

    public static Fix64 Zero => new(0);
    public static Fix64 One => new(OneRaw);

    public static Fix64 operator +(Fix64 left, Fix64 right) =>
        new(unchecked(left.RawBits + right.RawBits));

    public static Fix64 operator -(Fix64 left, Fix64 right) =>
        new(unchecked(left.RawBits - right.RawBits));

    public static Fix64 operator -(Fix64 value) =>
        new(unchecked(-value.RawBits));

    public static Fix64 operator *(Fix64 left, Fix64 right) =>
        new(unchecked((long)(((System.Int128)left.RawBits * right.RawBits) >> FractionalBits)));

    public static bool operator ==(Fix64 left, Fix64 right) => left.Equals(right);
    public static bool operator !=(Fix64 left, Fix64 right) => !left.Equals(right);
    public static bool operator <(Fix64 left, Fix64 right) => left.RawBits < right.RawBits;
    public static bool operator >(Fix64 left, Fix64 right) => left.RawBits > right.RawBits;
    public static bool operator <=(Fix64 left, Fix64 right) => left.RawBits <= right.RawBits;
    public static bool operator >=(Fix64 left, Fix64 right) => left.RawBits >= right.RawBits;

    public int CompareTo(Fix64 other) => RawBits.CompareTo(other.RawBits);
    public bool Equals(Fix64 other) => RawBits == other.RawBits;
    public override bool Equals(object? obj) => obj is Fix64 other && Equals(other);
    public override int GetHashCode() => RawBits.GetHashCode();
    public override string ToString() => ToDouble().ToString();
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        ToDouble().ToString(format, formatProvider);
}
