using System.Runtime.CompilerServices;
using System.Text;

namespace Helium.Hardware;

/// <summary>
/// Unsigned 4096-bit hardware lane with wraparound semantics.
/// This is not an arbitrary-precision exact integer.
/// </summary>
public struct UInt4096 : IEquatable<UInt4096>, IComparable<UInt4096>, IFormattable
{
    private UInt4096Limbs _limbs;

    public UInt4096(ulong value)
    {
        _limbs = default;
        _limbs[0] = value;
    }

    public UInt4096(ReadOnlySpan<ulong> limbs)
    {
        _limbs = default;
        var count = Math.Min(limbs.Length, LimbCount);
        for (var i = 0; i < count; i++)
            _limbs[i] = limbs[i];
    }

    public const int LimbCount = 64;

    public readonly ulong this[int index]
    {
        get
        {
            if ((uint)index >= LimbCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _limbs[index];
        }
    }

    public static UInt4096 Zero => new(0UL);

    public static UInt4096 One => new(1UL);

    public static UInt4096 operator +(UInt4096 left, UInt4096 right)
    {
        var result = new UInt4096();
        ulong carry = 0;
        for (var i = 0; i < LimbCount; i++)
        {
            var sum = (System.UInt128)left._limbs[i] + right._limbs[i] + carry;
            result._limbs[i] = (ulong)sum;
            carry = (ulong)(sum >> 64);
        }

        return result;
    }

    public static UInt4096 operator -(UInt4096 left, UInt4096 right)
    {
        var result = new UInt4096();
        ulong borrow = 0;
        for (var i = 0; i < LimbCount; i++)
        {
            var subtrahend = (System.UInt128)right._limbs[i] + borrow;
            borrow = (System.UInt128)left._limbs[i] < subtrahend ? 1UL : 0UL;
            result._limbs[i] = (ulong)((System.UInt128)left._limbs[i] - subtrahend);
        }

        return result;
    }

    public static UInt4096 operator *(UInt4096 left, UInt4096 right)
    {
        var result = new UInt4096();
        for (var i = 0; i < LimbCount; i++)
        {
            ulong carry = 0;
            for (var j = 0; i + j < LimbCount; j++)
            {
                var product = (System.UInt128)left._limbs[i] * right._limbs[j] + result._limbs[i + j] + carry;
                result._limbs[i + j] = (ulong)product;
                carry = (ulong)(product >> 64);
            }
        }

        return result;
    }

    public static bool operator ==(UInt4096 left, UInt4096 right) => left.Equals(right);
    public static bool operator !=(UInt4096 left, UInt4096 right) => !left.Equals(right);
    public static bool operator <(UInt4096 left, UInt4096 right) => left.CompareTo(right) < 0;
    public static bool operator >(UInt4096 left, UInt4096 right) => left.CompareTo(right) > 0;
    public static bool operator <=(UInt4096 left, UInt4096 right) => left.CompareTo(right) <= 0;
    public static bool operator >=(UInt4096 left, UInt4096 right) => left.CompareTo(right) >= 0;

    public readonly int CompareTo(UInt4096 other)
    {
        for (var i = LimbCount - 1; i >= 0; i--)
        {
            var comparison = _limbs[i].CompareTo(other._limbs[i]);
            if (comparison != 0)
                return comparison;
        }

        return 0;
    }

    public readonly bool Equals(UInt4096 other)
    {
        for (var i = 0; i < LimbCount; i++)
            if (_limbs[i] != other._limbs[i])
                return false;
        return true;
    }

    public override readonly bool Equals(object? obj) => obj is UInt4096 other && Equals(other);

    public override readonly int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_limbs[0]);
        hash.Add(_limbs[1]);
        hash.Add(_limbs[62]);
        hash.Add(_limbs[63]);
        return hash.ToHashCode();
    }

    public override readonly string ToString()
    {
        var sb = new StringBuilder(2 + 16 * LimbCount);
        sb.Append("0x");
        for (var i = LimbCount - 1; i >= 0; i--)
            sb.Append(_limbs[i].ToString("X16"));
        return sb.ToString();
    }

    public readonly string ToString(string? format, IFormatProvider? formatProvider) =>
        format switch
        {
            "X" or "x" => ToString(),
            _ => ToString()
        };
}

[InlineArray(UInt4096.LimbCount)]
internal struct UInt4096Limbs
{
    private ulong _element0;
}
