namespace Helium.Hardware;

/// <summary>
/// Q15.16 fixed-point hardware lane. Arithmetic uses fixed-width wraparound
/// semantics and is not exact real arithmetic.
/// </summary>
public readonly struct Fix32 : IEquatable<Fix32>, IComparable<Fix32>, IFormattable
{
    public const int FractionalBits = 16;
    public const int OneRaw = 1 << FractionalBits;

    public int RawBits { get; }

    private Fix32(int rawBits) => RawBits = rawBits;

    public static Fix32 FromRawBits(int rawBits) => new(rawBits);

    public static Fix32 FromDouble(double value) =>
        new(unchecked((int)Math.Round(value * OneRaw)));

    public double ToDouble() => RawBits / (double)OneRaw;

    public static Fix32 Zero => new(0);
    public static Fix32 One => new(OneRaw);

    public static Fix32 operator +(Fix32 left, Fix32 right) =>
        new(unchecked(left.RawBits + right.RawBits));

    public static Fix32 operator -(Fix32 left, Fix32 right) =>
        new(unchecked(left.RawBits - right.RawBits));

    public static Fix32 operator -(Fix32 value) =>
        new(unchecked(-value.RawBits));

    public static Fix32 operator *(Fix32 left, Fix32 right) =>
        new(unchecked((int)(((long)left.RawBits * right.RawBits) >> FractionalBits)));

    public static bool operator ==(Fix32 left, Fix32 right) => left.Equals(right);
    public static bool operator !=(Fix32 left, Fix32 right) => !left.Equals(right);
    public static bool operator <(Fix32 left, Fix32 right) => left.RawBits < right.RawBits;
    public static bool operator >(Fix32 left, Fix32 right) => left.RawBits > right.RawBits;
    public static bool operator <=(Fix32 left, Fix32 right) => left.RawBits <= right.RawBits;
    public static bool operator >=(Fix32 left, Fix32 right) => left.RawBits >= right.RawBits;

    public int CompareTo(Fix32 other) => RawBits.CompareTo(other.RawBits);
    public bool Equals(Fix32 other) => RawBits == other.RawBits;
    public override bool Equals(object? obj) => obj is Fix32 other && Equals(other);
    public override int GetHashCode() => RawBits.GetHashCode();
    public override string ToString() => ToDouble().ToString();
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        ToDouble().ToString(format, formatProvider);
}
