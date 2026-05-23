using Helium.Hardware;

namespace Helium.Hastlayer;

/// <summary>
/// Host-side kernel invocation helper. It owns allocation, layout population,
/// kernel execution, and output decoding. The memory factory chooses software
/// memory or a real Hastlayer SimpleMemory adapter.
/// </summary>
public sealed class HastlayerKernelHost
{
    private readonly ICellMemory32Factory _memoryFactory;

    public HastlayerKernelHost(ICellMemory32Factory? memoryFactory = null) =>
        _memoryFactory = memoryFactory ?? SimpleMemory32Factory.Instance;

    public uint RunHello(uint input)
    {
        var memory = _memoryFactory.Create(HelloKernel.RequiredCellCount());
        memory.WriteUInt32(HelloKernel.InputCell, input);
        HelloKernel.ExecuteCore(memory);
        return memory.ReadUInt32(HelloKernel.OutputCell);
    }

    public Fix64[] RunFixedPointMatVec(int rows, int cols, ReadOnlySpan<Fix64> matrix, ReadOnlySpan<Fix64> vector)
    {
        if (rows < 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (cols < 0) throw new ArgumentOutOfRangeException(nameof(cols));
        if (matrix.Length != checked(rows * cols))
            throw new ArgumentException("Matrix length must equal rows * cols.", nameof(matrix));
        if (vector.Length != cols)
            throw new ArgumentException("Vector length must equal cols.", nameof(vector));

        var matrixStart = FixedPointMatVecKernel.PayloadStartCell;
        var vectorStart = matrixStart + checked(rows * cols * 2);
        var resultStart = vectorStart + checked(cols * 2);
        var memory = _memoryFactory.Create(FixedPointMatVecKernel.RequiredCellCount(rows, cols));
        memory.WriteUInt32(FixedPointMatVecKernel.RowsCell, checked((uint)rows));
        memory.WriteUInt32(FixedPointMatVecKernel.ColsCell, checked((uint)cols));

        for (var i = 0; i < matrix.Length; i++)
            SimpleMemoryLayout.WriteFix64(memory, matrixStart + (i * 2), matrix[i]);

        for (var i = 0; i < vector.Length; i++)
            SimpleMemoryLayout.WriteFix64(memory, vectorStart + (i * 2), vector[i]);

        FixedPointMatVecKernel.ExecuteCore(memory);

        var result = new Fix64[rows];
        for (var i = 0; i < result.Length; i++)
            result[i] = SimpleMemoryLayout.ReadFix64(memory, resultStart + (i * 2));
        return result;
    }

    public uint[] RunRnsPolyMul(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b, uint prime)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Input polynomials must have the same length.", nameof(b));
        if (a.IsEmpty)
            return [];
        if (prime == 0)
            throw new ArgumentOutOfRangeException(nameof(prime));

        var n = a.Length;
        var aStart = RnsPolyMulKernel.PayloadStartCell;
        var bStart = aStart + n;
        var resultStart = bStart + n;
        var resultLength = checked((2 * n) - 1);
        var memory = _memoryFactory.Create(RnsPolyMulKernel.RequiredCellCount(n));
        memory.WriteUInt32(RnsPolyMulKernel.LengthCell, checked((uint)n));
        memory.WriteUInt32(RnsPolyMulKernel.PrimeCell, prime);

        for (var i = 0; i < n; i++)
        {
            memory.WriteUInt32(aStart + i, a[i]);
            memory.WriteUInt32(bStart + i, b[i]);
        }

        RnsPolyMulKernel.ExecuteCore(memory);

        var result = new uint[resultLength];
        for (var i = 0; i < result.Length; i++)
            result[i] = memory.ReadUInt32(resultStart + i);
        return result;
    }

    public uint[] RunRnsNttPolyMul(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b, uint prime, uint primitiveRoot)
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
        var aStart = RnsNttPolyMulKernel.PayloadStartCell;
        var bStart = aStart + n;
        var resultStart = bStart + n;
        var memory = _memoryFactory.Create(RnsNttPolyMulKernel.RequiredCellCount(n));
        memory.WriteUInt32(RnsNttPolyMulKernel.LengthCell, checked((uint)n));
        memory.WriteUInt32(RnsNttPolyMulKernel.PrimeCell, prime);
        memory.WriteUInt32(RnsNttPolyMulKernel.RootCell, primitiveRoot);

        for (var i = 0; i < n; i++)
        {
            memory.WriteUInt32(aStart + i, a[i]);
            memory.WriteUInt32(bStart + i, b[i]);
        }

        RnsNttPolyMulKernel.ExecuteCore(memory);

        var result = new uint[n];
        for (var i = 0; i < result.Length; i++)
            result[i] = memory.ReadUInt32(resultStart + i);
        return result;
    }

    public ulong[] RunGoldilocksPolyMul(ReadOnlySpan<ulong> a, ReadOnlySpan<ulong> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Input polynomials must have the same length.", nameof(b));
        if (a.IsEmpty)
            return [];

        var n = a.Length;
        var aStart = GoldilocksPolyMulKernel.PayloadStartCell;
        var bStart = aStart + checked(2 * n);
        var resultStart = bStart + checked(2 * n);
        var resultLength = checked((2 * n) - 1);
        var memory = _memoryFactory.Create(GoldilocksPolyMulKernel.RequiredCellCount(n));
        memory.WriteUInt32(GoldilocksPolyMulKernel.LengthCell, checked((uint)n));

        for (var i = 0; i < n; i++)
        {
            SimpleMemoryLayout.WriteUInt64(memory, aStart + (i * 2), a[i]);
            SimpleMemoryLayout.WriteUInt64(memory, bStart + (i * 2), b[i]);
        }

        GoldilocksPolyMulKernel.ExecuteCore(memory);

        var result = new ulong[resultLength];
        for (var i = 0; i < result.Length; i++)
            result[i] = SimpleMemoryLayout.ReadUInt64(memory, resultStart + (i * 2));
        return result;
    }

    public ulong[] RunGoldilocksNttPolyMul(ReadOnlySpan<ulong> a, ReadOnlySpan<ulong> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Input polynomials must have the same transform length.", nameof(b));
        if (a.IsEmpty)
            return [];
        if ((a.Length & (a.Length - 1)) != 0)
            throw new ArgumentException("Transform length must be a power of two.", nameof(a));

        var n = a.Length;
        var aStart = GoldilocksNttPolyMulKernel.PayloadStartCell;
        var bStart = aStart + checked(2 * n);
        var resultStart = bStart + checked(2 * n);
        var memory = _memoryFactory.Create(GoldilocksNttPolyMulKernel.RequiredCellCount(n));
        memory.WriteUInt32(GoldilocksNttPolyMulKernel.LengthCell, checked((uint)n));

        for (var i = 0; i < n; i++)
        {
            SimpleMemoryLayout.WriteUInt64(memory, aStart + (i * 2), a[i]);
            SimpleMemoryLayout.WriteUInt64(memory, bStart + (i * 2), b[i]);
        }

        GoldilocksNttPolyMulKernel.ExecuteCore(memory);

        var result = new ulong[n];
        for (var i = 0; i < result.Length; i++)
            result[i] = SimpleMemoryLayout.ReadUInt64(memory, resultStart + (i * 2));
        return result;
    }
}
