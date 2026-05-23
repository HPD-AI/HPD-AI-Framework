using Hast.Transformer.SimpleMemory;
using Helium.Hardware;

namespace Helium.Hastlayer;

/// <summary>
/// Hastlayer-compatible fixed-point matrix-vector multiply kernel.
/// All Fix64 values occupy two SimpleMemory cells in little-endian low/high order.
/// </summary>
public class FixedPointMatVecKernel
{
    public const int RowsCell = 0;
    public const int ColsCell = 1;
    public const int PayloadStartCell = 2;

    public static int RequiredCellCount(int rows, int cols)
    {
        if (rows < 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (cols < 0) throw new ArgumentOutOfRangeException(nameof(cols));
        var matrixCells = checked(rows * cols * 2);
        var vectorCells = checked(cols * 2);
        var resultCells = checked(rows * 2);
        return checked(PayloadStartCell + matrixCells + vectorCells + resultCells);
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
        var rows = memory.ReadUInt32(RowsCell);
        var cols = memory.ReadUInt32(ColsCell);
        var matrixStart = PayloadStartCell;
        var vectorStart = matrixStart + (int)(rows * cols * 2);
        var resultStart = vectorStart + (int)(cols * 2);

        for (var row = 0; row < rows; row++)
        {
            long sum = 0;
            for (var col = 0; col < cols; col++)
            {
                var matrixCell = matrixStart + (int)(((row * cols) + col) * 2);
                var vectorCell = vectorStart + (int)(col * 2);
                var matrixValue = SimpleMemoryLayout.ReadInt64(memory, matrixCell);
                var vectorValue = SimpleMemoryLayout.ReadInt64(memory, vectorCell);
                sum = unchecked(sum + MultiplyRawQ31_32(matrixValue, vectorValue));
            }

            SimpleMemoryLayout.WriteInt64(memory, resultStart + (int)(row * 2), sum);
        }
    }

    public static void ExecuteCore(SimpleMemory memory)
    {
        var rows = memory.ReadUInt32(RowsCell);
        var cols = memory.ReadUInt32(ColsCell);
        var matrixStart = PayloadStartCell;
        var vectorStart = matrixStart + (int)(rows * cols * 2);
        var resultStart = vectorStart + (int)(cols * 2);

        for (var row = 0; row < rows; row++)
        {
            long sum = 0;
            for (var col = 0; col < cols; col++)
            {
                var matrixCell = matrixStart + (int)(((row * cols) + col) * 2);
                var vectorCell = vectorStart + (int)(col * 2);
                var matrixValue = ReadInt64(memory, matrixCell);
                var vectorValue = ReadInt64(memory, vectorCell);
                sum = unchecked(sum + MultiplyRawQ31_32(matrixValue, vectorValue));
            }

            WriteInt64(memory, resultStart + (int)(row * 2), sum);
        }
    }

    private static long ReadInt64(SimpleMemory memory, int cellIndex)
    {
        var low = memory.ReadUInt32(cellIndex);
        var high = memory.ReadUInt32(cellIndex + 1);
        return unchecked((long)(((ulong)high << 32) | low));
    }

    private static void WriteInt64(SimpleMemory memory, int cellIndex, long value)
    {
        memory.WriteUInt32(cellIndex, unchecked((uint)value));
        memory.WriteUInt32(cellIndex + 1, unchecked((uint)((ulong)value >> 32)));
    }

    private static long MultiplyRawQ31_32(long left, long right)
    {
        var negative = (left < 0) != (right < 0);
        var leftMagnitude = AbsAsUInt64(left);
        var rightMagnitude = AbsAsUInt64(right);
        var shifted = UnsignedMulShiftRight32Low64(leftMagnitude, rightMagnitude);

        if (!negative)
            return unchecked((long)shifted);

        if (HasDiscardedLow32Bits(leftMagnitude, rightMagnitude))
            shifted = unchecked(shifted + 1);

        return unchecked(-(long)shifted);
    }

    private static ulong AbsAsUInt64(long value)
    {
        if (value >= 0)
            return (ulong)value;

        return unchecked((ulong)(~value) + 1UL);
    }

    private static bool HasDiscardedLow32Bits(ulong left, ulong right)
    {
        var leftLow = unchecked((uint)left);
        var rightLow = unchecked((uint)right);
        return unchecked(leftLow * rightLow) != 0;
    }

    private static ulong UnsignedMulShiftRight32Low64(ulong left, ulong right)
    {
        var leftLow = unchecked((uint)left);
        var leftHigh = left >> 32;
        var rightLow = unchecked((uint)right);
        var rightHigh = right >> 32;

        var lowLow = (ulong)leftLow * rightLow;
        var lowHigh = (ulong)leftLow * rightHigh;
        var highLow = leftHigh * rightLow;
        var highHighLow = unchecked((uint)(leftHigh * rightHigh));

        return unchecked((lowLow >> 32) + lowHigh + highLow + ((ulong)highHighLow << 32));
    }

    public Fix64[] ExecuteSoftware(int rows, int cols, ReadOnlySpan<Fix64> matrix, ReadOnlySpan<Fix64> vector)
    {
        if (rows < 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (cols < 0) throw new ArgumentOutOfRangeException(nameof(cols));
        if (matrix.Length != checked(rows * cols))
            throw new ArgumentException("Matrix length must equal rows * cols.", nameof(matrix));
        if (vector.Length != cols)
            throw new ArgumentException("Vector length must equal cols.", nameof(vector));

        var matrixStart = PayloadStartCell;
        var vectorStart = matrixStart + checked(rows * cols * 2);
        var resultStart = vectorStart + checked(cols * 2);
        var memory = SimpleMemory32.CreateSoftwareMemory(RequiredCellCount(rows, cols));

        memory.WriteUInt32(RowsCell, checked((uint)rows));
        memory.WriteUInt32(ColsCell, checked((uint)cols));

        for (var i = 0; i < matrix.Length; i++)
            SimpleMemoryLayout.WriteFix64(memory, matrixStart + (i * 2), matrix[i]);

        for (var i = 0; i < vector.Length; i++)
            SimpleMemoryLayout.WriteFix64(memory, vectorStart + (i * 2), vector[i]);

        Execute(memory);

        var result = new Fix64[rows];
        for (var i = 0; i < result.Length; i++)
            result[i] = SimpleMemoryLayout.ReadFix64(memory, resultStart + (i * 2));
        return result;
    }
}
