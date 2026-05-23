using System.Globalization;

namespace HPD.ML.Backends.Pjrt;

/// <summary>
/// Brain floating-point value with the standard 1-sign, 8-exponent, 7-mantissa bit layout.
/// This is a hardware dtype for XLA tensors, not an exact algebraic scalar.
/// </summary>
public readonly struct BFloat16 : IEquatable<BFloat16>, IFormattable
{
    public BFloat16(ushort bits) => Bits = bits;

    public ushort Bits { get; }

    public static BFloat16 FromBits(ushort bits) => new(bits);

    public static BFloat16 FromSingle(float value)
    {
        var bits = BitConverter.SingleToUInt32Bits(value);
        var lsb = (bits >> 16) & 1U;
        var roundingBias = 0x7FFFU + lsb;
        return new BFloat16((ushort)((bits + roundingBias) >> 16));
    }

    public float ToSingle() => BitConverter.UInt32BitsToSingle((uint)Bits << 16);

    public bool Equals(BFloat16 other) => Bits == other.Bits;
    public override bool Equals(object? obj) => obj is BFloat16 other && Equals(other);
    public override int GetHashCode() => Bits.GetHashCode();

    public override string ToString() => ToSingle().ToString(CultureInfo.InvariantCulture);

    public string ToString(string? format, IFormatProvider? formatProvider) =>
        ToSingle().ToString(format, formatProvider);

    public static explicit operator BFloat16(float value) => FromSingle(value);
    public static explicit operator float(BFloat16 value) => value.ToSingle();
    public static bool operator ==(BFloat16 left, BFloat16 right) => left.Equals(right);
    public static bool operator !=(BFloat16 left, BFloat16 right) => !left.Equals(right);
}
