using Hast.Transformer.SimpleMemory;

namespace Helium.Hastlayer;

/// <summary>
/// Software-testable NTT polynomial multiply kernel over one 32-bit RNS lane.
/// Layout:
/// cell 0: transform length n, must be a power of two and already include zero padding
/// cell 1: prime modulus, 32-bit NTT-friendly lane
/// cell 2: primitive n-th root modulo prime
/// cells 3..3+n-1: A coefficients
/// cells 3+n..3+2n-1: B coefficients
/// cells 3+2n..3+3n-1: result coefficients, length n
/// </summary>
public class RnsNttPolyMulKernel
{
    public const int LengthCell = 0;
    public const int PrimeCell = 1;
    public const int RootCell = 2;
    public const int PayloadStartCell = 3;

    public static int RequiredCellCount(int transformLength)
    {
        if (transformLength < 0) throw new ArgumentOutOfRangeException(nameof(transformLength));
        if (transformLength == 0)
            return PayloadStartCell;

        return checked(PayloadStartCell + (3 * transformLength));
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
        var prime = memory.ReadUInt32(PrimeCell);
        var root = memory.ReadUInt32(RootCell);
        if (n == 0 || prime == 0 || root == 0)
            return;

        var aStart = PayloadStartCell;
        var bStart = aStart + n;
        var resultStart = bStart + n;

        CopyCells(memory, aStart, resultStart, n, prime);
        Transform(memory, resultStart, n, prime, root, inverse: false);
        Transform(memory, bStart, n, prime, root, inverse: false);

        for (var i = 0; i < n; i++)
        {
            var product = MulMod(memory.ReadUInt32(resultStart + i), memory.ReadUInt32(bStart + i), prime);
            memory.WriteUInt32(resultStart + i, product);
        }

        Transform(memory, resultStart, n, prime, root, inverse: true);
    }

    public static void ExecuteCore(SimpleMemory memory)
    {
        var n = (int)memory.ReadUInt32(LengthCell);
        var prime = memory.ReadUInt32(PrimeCell);
        var root = memory.ReadUInt32(RootCell);
        if (n == 0 || prime == 0 || root == 0)
            return;

        var aStart = PayloadStartCell;
        var bStart = aStart + n;
        var resultStart = bStart + n;

        CopyCells(memory, aStart, resultStart, n, prime);
        Transform(memory, resultStart, n, prime, root, inverse: false);
        Transform(memory, bStart, n, prime, root, inverse: false);

        for (var i = 0; i < n; i++)
        {
            var product = MulMod(memory.ReadUInt32(resultStart + i), memory.ReadUInt32(bStart + i), prime);
            memory.WriteUInt32(resultStart + i, product);
        }

        Transform(memory, resultStart, n, prime, root, inverse: true);
    }

    public uint[] ExecuteSoftware(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b, uint prime, uint primitiveRoot)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Input polynomials must have the same transform length.", nameof(b));
        if (a.IsEmpty)
            return [];
        if ((a.Length & (a.Length - 1)) != 0)
            throw new ArgumentException("Transform length must be a power of two.", nameof(a));
        if (prime == 0)
            throw new ArgumentOutOfRangeException(nameof(prime));
        if (primitiveRoot == 0)
            throw new ArgumentOutOfRangeException(nameof(primitiveRoot));

        var n = a.Length;
        var aStart = PayloadStartCell;
        var bStart = aStart + n;
        var resultStart = bStart + n;
        var memory = SimpleMemory32.CreateSoftwareMemory(RequiredCellCount(n));
        memory.WriteUInt32(LengthCell, checked((uint)n));
        memory.WriteUInt32(PrimeCell, prime);
        memory.WriteUInt32(RootCell, primitiveRoot);

        for (var i = 0; i < n; i++)
        {
            memory.WriteUInt32(aStart + i, a[i]);
            memory.WriteUInt32(bStart + i, b[i]);
        }

        Execute(memory);

        var result = new uint[n];
        for (var i = 0; i < result.Length; i++)
            result[i] = memory.ReadUInt32(resultStart + i);
        return result;
    }

    private static void CopyCells(ICellMemory32 memory, int sourceStart, int targetStart, int length, uint prime)
    {
        for (var i = 0; i < length; i++)
            memory.WriteUInt32(targetStart + i, memory.ReadUInt32(sourceStart + i) % prime);
    }

    private static void CopyCells(SimpleMemory memory, int sourceStart, int targetStart, int length, uint prime)
    {
        for (var i = 0; i < length; i++)
            memory.WriteUInt32(targetStart + i, memory.ReadUInt32(sourceStart + i) % prime);
    }

    private static void Transform(ICellMemory32 memory, int start, int n, uint prime, uint primitiveRoot, bool inverse)
    {
        BitReverse(memory, start, n);
        var root = inverse ? PowMod(primitiveRoot, prime - 2U, prime) : primitiveRoot;

        for (var len = 2; len <= n; len <<= 1)
        {
            var wLen = PowMod(root, (uint)(n / len), prime);
            for (var i = 0; i < n; i += len)
            {
                var w = 1U;
                var half = len >> 1;
                for (var j = 0; j < half; j++)
                {
                    var leftIndex = start + i + j;
                    var rightIndex = leftIndex + half;
                    var u = memory.ReadUInt32(leftIndex);
                    var v = MulMod(memory.ReadUInt32(rightIndex), w, prime);
                    memory.WriteUInt32(leftIndex, AddMod(u, v, prime));
                    memory.WriteUInt32(rightIndex, SubMod(u, v, prime));
                    w = MulMod(w, wLen, prime);
                }
            }
        }

        if (inverse)
        {
            var nInv = PowMod((uint)n, prime - 2U, prime);
            for (var i = 0; i < n; i++)
                memory.WriteUInt32(start + i, MulMod(memory.ReadUInt32(start + i), nInv, prime));
        }
    }

    private static void Transform(SimpleMemory memory, int start, int n, uint prime, uint primitiveRoot, bool inverse)
    {
        BitReverse(memory, start, n);
        var root = inverse ? PowMod(primitiveRoot, prime - 2U, prime) : primitiveRoot;

        for (var len = 2; len <= n; len <<= 1)
        {
            var wLen = PowMod(root, (uint)(n / len), prime);
            for (var i = 0; i < n; i += len)
            {
                var w = 1U;
                var half = len >> 1;
                for (var j = 0; j < half; j++)
                {
                    var leftIndex = start + i + j;
                    var rightIndex = leftIndex + half;
                    var u = memory.ReadUInt32(leftIndex);
                    var v = MulMod(memory.ReadUInt32(rightIndex), w, prime);
                    memory.WriteUInt32(leftIndex, AddMod(u, v, prime));
                    memory.WriteUInt32(rightIndex, SubMod(u, v, prime));
                    w = MulMod(w, wLen, prime);
                }
            }
        }

        if (inverse)
        {
            var nInv = PowMod((uint)n, prime - 2U, prime);
            for (var i = 0; i < n; i++)
                memory.WriteUInt32(start + i, MulMod(memory.ReadUInt32(start + i), nInv, prime));
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
                var left = memory.ReadUInt32(start + i);
                var right = memory.ReadUInt32(start + j);
                memory.WriteUInt32(start + i, right);
                memory.WriteUInt32(start + j, left);
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
                var left = memory.ReadUInt32(start + i);
                var right = memory.ReadUInt32(start + j);
                memory.WriteUInt32(start + i, right);
                memory.WriteUInt32(start + j, left);
            }
        }
    }

    private static uint AddMod(uint left, uint right, uint prime)
    {
        var sum = (ulong)left + right;
        if (sum >= prime)
            sum -= prime;
        return (uint)sum;
    }

    private static uint SubMod(uint left, uint right, uint prime) =>
        left >= right ? left - right : (uint)(prime - (right - left));

    private static uint MulMod(uint left, uint right, uint prime) =>
        (uint)(((ulong)left * right) % prime);

    private static uint PowMod(uint value, uint exponent, uint prime)
    {
        var result = 1U;
        var baseValue = value % prime;
        while (exponent != 0)
        {
            if ((exponent & 1U) != 0)
                result = MulMod(result, baseValue, prime);
            baseValue = MulMod(baseValue, baseValue, prime);
            exponent >>= 1;
        }

        return result;
    }
}
