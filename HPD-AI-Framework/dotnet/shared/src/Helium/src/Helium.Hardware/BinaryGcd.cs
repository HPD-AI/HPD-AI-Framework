namespace Helium.Hardware;

/// <summary>
/// Division-free binary GCD (Stein's algorithm) for hardware-friendly integer lanes.
/// Uses shifts, subtraction, and comparison only.
/// </summary>
public static class BinaryGcd
{
    public static ulong Compute(ulong left, ulong right)
    {
        if (left == 0) return right;
        if (right == 0) return left;

        var shift = System.Numerics.BitOperations.TrailingZeroCount(left | right);
        left >>= System.Numerics.BitOperations.TrailingZeroCount(left);

        do
        {
            right >>= System.Numerics.BitOperations.TrailingZeroCount(right);
            if (left > right)
                (left, right) = (right, left);
            right -= left;
        }
        while (right != 0);

        return left << shift;
    }

    public static UInt256 Compute(UInt256 left, UInt256 right)
    {
        if (left == UInt256.Zero) return right;
        if (right == UInt256.Zero) return left;

        var shift = CommonTrailingZeroCount(left, right);
        left = ShiftRight(left, TrailingZeroCount(left));

        do
        {
            right = ShiftRight(right, TrailingZeroCount(right));
            if (left > right)
                (left, right) = (right, left);
            right -= left;
        }
        while (right != UInt256.Zero);

        return ShiftLeft(left, shift);
    }

    private static int CommonTrailingZeroCount(UInt256 left, UInt256 right) =>
        TrailingZeroCount(Or(left, right));

    private static int TrailingZeroCount(UInt256 value)
    {
        if (value == UInt256.Zero)
            return 256;

        if (value.L0 != 0)
            return System.Numerics.BitOperations.TrailingZeroCount(value.L0);
        if (value.L1 != 0)
            return 64 + System.Numerics.BitOperations.TrailingZeroCount(value.L1);
        if (value.L2 != 0)
            return 128 + System.Numerics.BitOperations.TrailingZeroCount(value.L2);
        return 192 + System.Numerics.BitOperations.TrailingZeroCount(value.L3);
    }

    private static UInt256 Or(UInt256 left, UInt256 right) =>
        new(left.L0 | right.L0, left.L1 | right.L1, left.L2 | right.L2, left.L3 | right.L3);

    private static UInt256 ShiftRight(UInt256 value, int count)
    {
        if (count <= 0) return value;
        if (count >= 256) return UInt256.Zero;

        Span<ulong> source = [value.L0, value.L1, value.L2, value.L3];
        Span<ulong> result = stackalloc ulong[4];
        var wordShift = count / 64;
        var bitShift = count % 64;

        for (var i = 0; i < 4 - wordShift; i++)
        {
            var sourceIndex = i + wordShift;
            result[i] = source[sourceIndex] >> bitShift;
            if (bitShift != 0 && sourceIndex + 1 < 4)
                result[i] |= source[sourceIndex + 1] << (64 - bitShift);
        }

        return new UInt256(result[0], result[1], result[2], result[3]);
    }

    private static UInt256 ShiftLeft(UInt256 value, int count)
    {
        if (count <= 0) return value;
        if (count >= 256) return UInt256.Zero;

        Span<ulong> source = [value.L0, value.L1, value.L2, value.L3];
        Span<ulong> result = stackalloc ulong[4];
        var wordShift = count / 64;
        var bitShift = count % 64;

        for (var i = wordShift; i < 4; i++)
        {
            var sourceIndex = i - wordShift;
            result[i] = source[sourceIndex] << bitShift;
            if (bitShift != 0 && sourceIndex > 0)
                result[i] |= source[sourceIndex - 1] >> (64 - bitShift);
        }

        return new UInt256(result[0], result[1], result[2], result[3]);
    }
}
