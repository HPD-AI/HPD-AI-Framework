namespace Helium.Hardware;

/// <summary>
/// Unsigned 256-bit hardware lane with wraparound semantics.
/// This is not an arbitrary-precision exact integer.
/// </summary>
public readonly struct UInt256 : IEquatable<UInt256>, IComparable<UInt256>, IFormattable
{
    public ulong L0 { get; }
    public ulong L1 { get; }
    public ulong L2 { get; }
    public ulong L3 { get; }

    public UInt256(ulong l0, ulong l1 = 0, ulong l2 = 0, ulong l3 = 0)
    {
        L0 = l0;
        L1 = l1;
        L2 = l2;
        L3 = l3;
    }

    public static UInt256 Zero => new(0);
    public static UInt256 One => new(1);

    public static UInt256 operator +(UInt256 left, UInt256 right)
    {
        var l0 = AddWithCarry(left.L0, right.L0, 0, out var carry0);
        var l1 = AddWithCarry(left.L1, right.L1, carry0, out var carry1);
        var l2 = AddWithCarry(left.L2, right.L2, carry1, out var carry2);
        var l3 = AddWithCarry(left.L3, right.L3, carry2, out _);
        return new UInt256(l0, l1, l2, l3);
    }

    public static UInt256 operator -(UInt256 left, UInt256 right)
    {
        var l0 = SubWithBorrow(left.L0, right.L0, 0, out var borrow0);
        var l1 = SubWithBorrow(left.L1, right.L1, borrow0, out var borrow1);
        var l2 = SubWithBorrow(left.L2, right.L2, borrow1, out var borrow2);
        var l3 = SubWithBorrow(left.L3, right.L3, borrow2, out _);
        return new UInt256(l0, l1, l2, l3);
    }

    public static UInt256 operator *(UInt256 left, UInt256 right)
    {
        Span<ulong> result = stackalloc ulong[4];
        ReadOnlySpan<ulong> a = [left.L0, left.L1, left.L2, left.L3];
        ReadOnlySpan<ulong> b = [right.L0, right.L1, right.L2, right.L3];

        for (int i = 0; i < 4; i++)
        {
            ulong carry = 0;
            for (int j = 0; j + i < 4; j++)
            {
                var product = (System.UInt128)a[i] * b[j] + result[i + j] + carry;
                result[i + j] = (ulong)product;
                carry = (ulong)(product >> 64);
            }
        }

        return new UInt256(result[0], result[1], result[2], result[3]);
    }

    public static bool operator ==(UInt256 left, UInt256 right) => left.Equals(right);
    public static bool operator !=(UInt256 left, UInt256 right) => !left.Equals(right);
    public static bool operator <(UInt256 left, UInt256 right) => left.CompareTo(right) < 0;
    public static bool operator >(UInt256 left, UInt256 right) => left.CompareTo(right) > 0;
    public static bool operator <=(UInt256 left, UInt256 right) => left.CompareTo(right) <= 0;
    public static bool operator >=(UInt256 left, UInt256 right) => left.CompareTo(right) >= 0;

    public int CompareTo(UInt256 other)
    {
        var c3 = L3.CompareTo(other.L3);
        if (c3 != 0) return c3;
        var c2 = L2.CompareTo(other.L2);
        if (c2 != 0) return c2;
        var c1 = L1.CompareTo(other.L1);
        return c1 != 0 ? c1 : L0.CompareTo(other.L0);
    }

    public bool Equals(UInt256 other) =>
        L0 == other.L0 && L1 == other.L1 && L2 == other.L2 && L3 == other.L3;

    public override bool Equals(object? obj) => obj is UInt256 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(L0, L1, L2, L3);
    public override string ToString() => $"0x{L3:X16}{L2:X16}{L1:X16}{L0:X16}";
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        format switch
        {
            "X" or "x" => ToString(),
            _ => ToString()
        };

    private static ulong AddWithCarry(ulong left, ulong right, ulong carryIn, out ulong carryOut)
    {
        var sum = (System.UInt128)left + right + carryIn;
        carryOut = (ulong)(sum >> 64);
        return (ulong)sum;
    }

    private static ulong SubWithBorrow(ulong left, ulong right, ulong borrowIn, out ulong borrowOut)
    {
        var subtrahend = (System.UInt128)right + borrowIn;
        borrowOut = (System.UInt128)left < subtrahend ? 1UL : 0UL;
        return (ulong)((System.UInt128)left - subtrahend);
    }
}
