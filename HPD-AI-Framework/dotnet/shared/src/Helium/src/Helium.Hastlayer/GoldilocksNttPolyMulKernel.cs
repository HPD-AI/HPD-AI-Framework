using Hast.Transformer.SimpleMemory;

namespace Helium.Hastlayer;

/// <summary>
/// NTT polynomial multiply kernel over the Goldilocks prime.
/// Layout:
/// cell 0: transform length n, must be a power of two and already include zero padding
/// cell 1: reserved
/// cell 2: reserved
/// cells 3..3+(2n)-1: A coefficients, two cells per coefficient
/// cells 3+(2n)..3+(4n)-1: B coefficients, two cells per coefficient
/// cells 3+(4n)..3+(6n)-1: result coefficients, length n, two cells each
/// </summary>
public class GoldilocksNttPolyMulKernel
{
    public const ulong Prime = GoldilocksPolyMulKernel.Prime;
    public const ulong PrimitiveGenerator = 7UL;
    public const int LengthCell = 0;
    public const int Reserved0Cell = 1;
    public const int Reserved1Cell = 2;
    public const int PayloadStartCell = 3;

    public static int RequiredCellCount(int transformLength)
    {
        if (transformLength < 0) throw new ArgumentOutOfRangeException(nameof(transformLength));
        if (transformLength == 0)
            return PayloadStartCell;

        return checked(PayloadStartCell + (6 * transformLength));
    }

    public virtual void Execute(SimpleMemory32 memory)
    {
        ExecuteCore(memory);
    }

    public virtual void Execute(SimpleMemory memory)
    {
        ExecuteCore(memory);
    }

    public static void ExecuteCore(ICellMemory32 memory)
    {
        var n = (int)memory.ReadUInt32(LengthCell);
        if (n == 0)
            return;

        var root = RootForLength(n);
        var aStart = PayloadStartCell;
        var bStart = aStart + (2 * n);
        var resultStart = bStart + (2 * n);

        CopyCells(memory, aStart, resultStart, n);
        Transform(memory, resultStart, n, root, inverse: false);
        Transform(memory, bStart, n, root, inverse: false);

        for (var i = 0; i < n; i++)
        {
            var product = MulMod(
                SimpleMemoryLayout.ReadUInt64(memory, resultStart + (i * 2)),
                SimpleMemoryLayout.ReadUInt64(memory, bStart + (i * 2)));
            SimpleMemoryLayout.WriteUInt64(memory, resultStart + (i * 2), product);
        }

        Transform(memory, resultStart, n, root, inverse: true);
    }

    public static void ExecuteCore(SimpleMemory memory)
    {
        var n = (int)memory.ReadUInt32(LengthCell);
        if (n == 0)
            return;

        var root = RootForLength(n);
        var aStart = PayloadStartCell;
        var bStart = aStart + (2 * n);
        var resultStart = bStart + (2 * n);

        CopyCells(memory, aStart, resultStart, n);
        Transform(memory, resultStart, n, root, inverse: false);
        Transform(memory, bStart, n, root, inverse: false);

        for (var i = 0; i < n; i++)
        {
            var product = MulMod(ReadUInt64(memory, resultStart + (i * 2)), ReadUInt64(memory, bStart + (i * 2)));
            WriteUInt64(memory, resultStart + (i * 2), product);
        }

        Transform(memory, resultStart, n, root, inverse: true);
    }

    public ulong[] ExecuteSoftware(ReadOnlySpan<ulong> a, ReadOnlySpan<ulong> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Input polynomials must have the same transform length.", nameof(b));
        if (a.IsEmpty)
            return [];
        if ((a.Length & (a.Length - 1)) != 0)
            throw new ArgumentException("Transform length must be a power of two.", nameof(a));

        var n = a.Length;
        var aStart = PayloadStartCell;
        var bStart = aStart + checked(2 * n);
        var resultStart = bStart + checked(2 * n);
        var memory = SimpleMemory32.CreateSoftwareMemory(RequiredCellCount(n));
        memory.WriteUInt32(LengthCell, checked((uint)n));

        for (var i = 0; i < n; i++)
        {
            SimpleMemoryLayout.WriteUInt64(memory, aStart + (i * 2), a[i]);
            SimpleMemoryLayout.WriteUInt64(memory, bStart + (i * 2), b[i]);
        }

        Execute(memory);

        var result = new ulong[n];
        for (var i = 0; i < result.Length; i++)
            result[i] = SimpleMemoryLayout.ReadUInt64(memory, resultStart + (i * 2));
        return result;
    }

    private static void CopyCells(ICellMemory32 memory, int sourceStart, int targetStart, int length)
    {
        for (var i = 0; i < length; i++)
            SimpleMemoryLayout.WriteUInt64(
                memory,
                targetStart + (i * 2),
                ReduceInput(SimpleMemoryLayout.ReadUInt64(memory, sourceStart + (i * 2))));
    }

    private static void CopyCells(SimpleMemory memory, int sourceStart, int targetStart, int length)
    {
        for (var i = 0; i < length; i++)
            WriteUInt64(memory, targetStart + (i * 2), ReduceInput(ReadUInt64(memory, sourceStart + (i * 2))));
    }

    private static void Transform(ICellMemory32 memory, int start, int n, ulong primitiveRoot, bool inverse)
    {
        BitReverse(memory, start, n);
        var root = inverse ? PowMod(primitiveRoot, Prime - 2UL) : primitiveRoot;

        for (var len = 2; len <= n; len <<= 1)
        {
            var wLen = PowMod(root, (ulong)(n / len));
            for (var i = 0; i < n; i += len)
            {
                var w = 1UL;
                var half = len >> 1;
                for (var j = 0; j < half; j++)
                {
                    var leftIndex = start + ((i + j) * 2);
                    var rightIndex = leftIndex + (half * 2);
                    var u = SimpleMemoryLayout.ReadUInt64(memory, leftIndex);
                    var v = MulMod(SimpleMemoryLayout.ReadUInt64(memory, rightIndex), w);
                    SimpleMemoryLayout.WriteUInt64(memory, leftIndex, AddMod(u, v));
                    SimpleMemoryLayout.WriteUInt64(memory, rightIndex, SubMod(u, v));
                    w = MulMod(w, wLen);
                }
            }
        }

        if (inverse)
        {
            var nInv = PowMod((ulong)n, Prime - 2UL);
            for (var i = 0; i < n; i++)
                SimpleMemoryLayout.WriteUInt64(
                    memory,
                    start + (i * 2),
                    MulMod(SimpleMemoryLayout.ReadUInt64(memory, start + (i * 2)), nInv));
        }
    }

    private static void Transform(SimpleMemory memory, int start, int n, ulong primitiveRoot, bool inverse)
    {
        BitReverse(memory, start, n);
        var root = inverse ? PowMod(primitiveRoot, Prime - 2UL) : primitiveRoot;

        for (var len = 2; len <= n; len <<= 1)
        {
            var wLen = PowMod(root, (ulong)(n / len));
            for (var i = 0; i < n; i += len)
            {
                var w = 1UL;
                var half = len >> 1;
                for (var j = 0; j < half; j++)
                {
                    var leftIndex = start + ((i + j) * 2);
                    var rightIndex = leftIndex + (half * 2);
                    var u = ReadUInt64(memory, leftIndex);
                    var v = MulMod(ReadUInt64(memory, rightIndex), w);
                    WriteUInt64(memory, leftIndex, AddMod(u, v));
                    WriteUInt64(memory, rightIndex, SubMod(u, v));
                    w = MulMod(w, wLen);
                }
            }
        }

        if (inverse)
        {
            var nInv = PowMod((ulong)n, Prime - 2UL);
            for (var i = 0; i < n; i++)
                WriteUInt64(memory, start + (i * 2), MulMod(ReadUInt64(memory, start + (i * 2)), nInv));
        }
    }

    private static void BitReverse(ICellMemory32 memory, int start, int n)
    {
        var j = 0;
        for (var i = 1; i < n; i++)
        {
            var bit = n >> 1;
            while ((j & bit) != 0)
            {
                j ^= bit;
                bit >>= 1;
            }

            j ^= bit;
            if (i < j)
            {
                var leftIndex = start + (i * 2);
                var rightIndex = start + (j * 2);
                var left = SimpleMemoryLayout.ReadUInt64(memory, leftIndex);
                var right = SimpleMemoryLayout.ReadUInt64(memory, rightIndex);
                SimpleMemoryLayout.WriteUInt64(memory, leftIndex, right);
                SimpleMemoryLayout.WriteUInt64(memory, rightIndex, left);
            }
        }
    }

    private static void BitReverse(SimpleMemory memory, int start, int n)
    {
        var j = 0;
        for (var i = 1; i < n; i++)
        {
            var bit = n >> 1;
            while ((j & bit) != 0)
            {
                j ^= bit;
                bit >>= 1;
            }

            j ^= bit;
            if (i < j)
            {
                var leftIndex = start + (i * 2);
                var rightIndex = start + (j * 2);
                var left = ReadUInt64(memory, leftIndex);
                var right = ReadUInt64(memory, rightIndex);
                WriteUInt64(memory, leftIndex, right);
                WriteUInt64(memory, rightIndex, left);
            }
        }
    }

    private static ulong RootForLength(int length) =>
        PowMod(PrimitiveGenerator, (Prime - 1UL) / (ulong)length);

    private static ulong PowMod(ulong value, ulong exponent)
    {
        var result = 1UL;
        var baseValue = ReduceInput(value);
        while (exponent != 0)
        {
            if ((exponent & 1UL) != 0)
                result = MulMod(result, baseValue);
            baseValue = MulMod(baseValue, baseValue);
            exponent >>= 1;
        }

        return result;
    }

    private static ulong MulMod(ulong left, ulong right)
    {
        left = ReduceInput(left);
        var result = 0UL;

        for (var bit = 0; bit < 64; bit++)
        {
            if ((right & 1UL) != 0)
                result = AddMod(result, left);

            right >>= 1;
            if (right == 0)
                break;

            left = AddMod(left, left);
        }

        return result;
    }

    private static ulong ReduceInput(ulong value)
    {
        if (value >= Prime)
            return value - Prime;

        return value;
    }

    private static ulong AddMod(ulong left, ulong right)
    {
        var sum = unchecked(left + right);
        if (sum < left)
            sum = AddPower64Residue(sum);

        if (sum >= Prime)
            sum -= Prime;

        return sum;
    }

    private static ulong SubMod(ulong left, ulong right) =>
        left >= right ? left - right : Prime - (right - left);

    private static ulong AddPower64Residue(ulong value)
    {
        const ulong residue = 0xFFFFFFFFUL;
        var sum = unchecked(value + residue);
        if (sum < value)
            sum = unchecked(sum + residue);

        if (sum >= Prime)
            sum -= Prime;

        return sum;
    }

    private static ulong ReadUInt64(SimpleMemory memory, int cellIndex)
    {
        var low = memory.ReadUInt32(cellIndex);
        var high = memory.ReadUInt32(cellIndex + 1);
        return ((ulong)high << 32) | low;
    }

    private static void WriteUInt64(SimpleMemory memory, int cellIndex, ulong value)
    {
        memory.WriteUInt32(cellIndex, unchecked((uint)value));
        memory.WriteUInt32(cellIndex + 1, unchecked((uint)(value >> 32)));
    }
}
