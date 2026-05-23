using Hast.Transformer.SimpleMemory;

namespace Helium.Hastlayer;

/// <summary>
/// Software-testable modular polynomial multiply kernel over one RNS lane.
/// This is the baseline memory protocol for the later FPGA NTT implementation.
/// Layout:
/// cell 0: n, number of input coefficients for A and B
/// cell 1: prime modulus, 32-bit lane
/// cell 2: reserved
/// cells 3..3+n-1: A coefficients
/// cells 3+n..3+2n-1: B coefficients
/// cells 3+2n..3+4n-2: result coefficients, length 2n-1
/// </summary>
public class RnsPolyMulKernel
{
    public const int LengthCell = 0;
    public const int PrimeCell = 1;
    public const int ReservedCell = 2;
    public const int PayloadStartCell = 3;

    public static int RequiredCellCount(int coefficientCount)
    {
        if (coefficientCount < 0) throw new ArgumentOutOfRangeException(nameof(coefficientCount));
        if (coefficientCount == 0)
            return PayloadStartCell;

        var inputCells = checked(coefficientCount * 2);
        var resultCells = checked((2 * coefficientCount) - 1);
        return checked(PayloadStartCell + inputCells + resultCells);
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
        var n = memory.ReadUInt32(LengthCell);
        var prime = memory.ReadUInt32(PrimeCell);
        if (n == 0 || prime == 0)
            return;

        var aStart = PayloadStartCell;
        var bStart = aStart + (int)n;
        var resultStart = bStart + (int)n;
        var resultLength = checked((int)((2 * n) - 1));

        for (var i = 0; i < resultLength; i++)
            memory.WriteUInt32(resultStart + i, 0);

        for (var i = 0; i < n; i++)
        {
            var ai = memory.ReadUInt32(aStart + (int)i) % prime;
            for (var j = 0; j < n; j++)
            {
                var bj = memory.ReadUInt32(bStart + (int)j) % prime;
                var resultIndex = resultStart + (int)(i + j);
                var current = memory.ReadUInt32(resultIndex);
                var next = AddMulMod(current, ai, bj, prime);
                memory.WriteUInt32(resultIndex, next);
            }
        }
    }

    public static void ExecuteCore(SimpleMemory memory)
    {
        var n = memory.ReadUInt32(LengthCell);
        var prime = memory.ReadUInt32(PrimeCell);
        if (n == 0 || prime == 0)
            return;

        var aStart = PayloadStartCell;
        var bStart = aStart + (int)n;
        var resultStart = bStart + (int)n;
        var resultLength = checked((int)((2 * n) - 1));

        for (var i = 0; i < resultLength; i++)
            memory.WriteUInt32(resultStart + i, 0);

        for (var i = 0; i < n; i++)
        {
            var ai = memory.ReadUInt32(aStart + (int)i) % prime;
            for (var j = 0; j < n; j++)
            {
                var bj = memory.ReadUInt32(bStart + (int)j) % prime;
                var resultIndex = resultStart + (int)(i + j);
                var current = memory.ReadUInt32(resultIndex);
                var next = AddMulMod(current, ai, bj, prime);
                memory.WriteUInt32(resultIndex, next);
            }
        }
    }

    public uint[] ExecuteSoftware(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b, uint prime)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Input polynomials must have the same length.", nameof(b));
        if (a.IsEmpty)
            return [];
        if (prime == 0)
            throw new ArgumentOutOfRangeException(nameof(prime));

        var n = a.Length;
        var aStart = PayloadStartCell;
        var bStart = aStart + n;
        var resultStart = bStart + n;
        var resultLength = checked((2 * n) - 1);
        var memory = SimpleMemory32.CreateSoftwareMemory(RequiredCellCount(n));
        memory.WriteUInt32(LengthCell, checked((uint)n));
        memory.WriteUInt32(PrimeCell, prime);

        for (var i = 0; i < n; i++)
        {
            memory.WriteUInt32(aStart + i, a[i]);
            memory.WriteUInt32(bStart + i, b[i]);
        }

        Execute(memory);

        var result = new uint[resultLength];
        for (var i = 0; i < result.Length; i++)
            result[i] = memory.ReadUInt32(resultStart + i);
        return result;
    }

    private static uint AddMulMod(uint current, uint left, uint right, uint prime)
    {
        var value = ((ulong)current + ((ulong)left * right)) % prime;
        return (uint)value;
    }
}
