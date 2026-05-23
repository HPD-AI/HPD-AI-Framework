using Hast.Transformer.SimpleMemory;

namespace Helium.Hastlayer;

/// <summary>
/// Goldilocks-prime polynomial multiply memory kernel.
/// This is a software-testable baseline for the later FPGA NTT kernel.
/// Layout:
/// cell 0: n, number of input coefficients for A and B
/// cell 1: reserved
/// cell 2: reserved
/// cells 3..3+(2n)-1: A coefficients, two cells per coefficient
/// cells 3+(2n)..3+(4n)-1: B coefficients, two cells per coefficient
/// cells 3+(4n)..3+(8n)-3: result coefficients, length 2n-1, two cells each
/// </summary>
public class GoldilocksPolyMulKernel
{
    public const ulong Prime = 0xFFFFFFFF00000001UL;
    public const int LengthCell = 0;
    public const int Reserved0Cell = 1;
    public const int Reserved1Cell = 2;
    public const int PayloadStartCell = 3;

    public static int RequiredCellCount(int coefficientCount)
    {
        if (coefficientCount < 0) throw new ArgumentOutOfRangeException(nameof(coefficientCount));
        if (coefficientCount == 0)
            return PayloadStartCell;

        var inputCells = checked(4 * coefficientCount);
        var resultCells = checked(((2 * coefficientCount) - 1) * 2);
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
        if (n == 0)
            return;

        var aStart = PayloadStartCell;
        var bStart = aStart + (int)(2 * n);
        var resultStart = bStart + (int)(2 * n);
        var resultLength = checked((int)((2 * n) - 1));

        for (var i = 0; i < resultLength; i++)
            SimpleMemoryLayout.WriteUInt64(memory, resultStart + (i * 2), 0);

        for (var i = 0; i < n; i++)
        {
            var ai = SimpleMemoryLayout.ReadUInt64(memory, aStart + (int)(i * 2)) % Prime;
            for (var j = 0; j < n; j++)
            {
                var bj = SimpleMemoryLayout.ReadUInt64(memory, bStart + (int)(j * 2)) % Prime;
                var resultCell = resultStart + (int)((i + j) * 2);
                var current = SimpleMemoryLayout.ReadUInt64(memory, resultCell);
                SimpleMemoryLayout.WriteUInt64(memory, resultCell, AddMulMod(current, ai, bj));
            }
        }
    }

    public static void ExecuteCore(SimpleMemory memory)
    {
        var n = memory.ReadUInt32(LengthCell);
        if (n == 0)
            return;

        var aStart = PayloadStartCell;
        var bStart = aStart + (int)(2 * n);
        var resultStart = bStart + (int)(2 * n);
        var resultLength = checked((int)((2 * n) - 1));

        for (var i = 0; i < resultLength; i++)
            WriteUInt64(memory, resultStart + (i * 2), 0);

        for (var i = 0; i < n; i++)
        {
            var ai = ReadUInt64(memory, aStart + (int)(i * 2)) % Prime;
            for (var j = 0; j < n; j++)
            {
                var bj = ReadUInt64(memory, bStart + (int)(j * 2)) % Prime;
                var resultCell = resultStart + (int)((i + j) * 2);
                var current = ReadUInt64(memory, resultCell);
                WriteUInt64(memory, resultCell, AddMulMod(current, ai, bj));
            }
        }
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

    public ulong[] ExecuteSoftware(ReadOnlySpan<ulong> a, ReadOnlySpan<ulong> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Input polynomials must have the same length.", nameof(b));
        if (a.IsEmpty)
            return [];

        var n = a.Length;
        var aStart = PayloadStartCell;
        var bStart = aStart + checked(2 * n);
        var resultStart = bStart + checked(2 * n);
        var resultLength = checked((2 * n) - 1);
        var memory = SimpleMemory32.CreateSoftwareMemory(RequiredCellCount(n));
        memory.WriteUInt32(LengthCell, checked((uint)n));

        for (var i = 0; i < n; i++)
        {
            SimpleMemoryLayout.WriteUInt64(memory, aStart + (i * 2), a[i]);
            SimpleMemoryLayout.WriteUInt64(memory, bStart + (i * 2), b[i]);
        }

        Execute(memory);

        var result = new ulong[resultLength];
        for (var i = 0; i < result.Length; i++)
            result[i] = SimpleMemoryLayout.ReadUInt64(memory, resultStart + (i * 2));
        return result;
    }

    private static ulong AddMulMod(ulong current, ulong left, ulong right)
    {
        var product = MulMod(left, right);
        return AddMod(current, product);
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
}
