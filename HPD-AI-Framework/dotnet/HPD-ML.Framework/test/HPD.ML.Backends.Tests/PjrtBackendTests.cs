using HPD.ML.Backends.Pjrt;
using HPD.ML.Backends.Pjrt.Training;
using HPD.ML.Backends.Abstractions.Training;
using Helium.Algebra;
using Helium.Hardware;
using Helium.Primitives;

namespace HPD.ML.Backends.Tests;

public sealed class PjrtFloatBackendTests
{
    [Fact]
    public void MatMul_2x2_ReturnsExpectedResult()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var a = backend.CreateMatrix(2, 2, [1.0f, 2.0f, 3.0f, 4.0f]);
        using var b = backend.CreateMatrix(2, 2, [5.0f, 6.0f, 7.0f, 8.0f]);

        using var c = backend.MatMul(a, b);

        Assert.Equal(2, c.Rows);
        Assert.Equal(2, c.Cols);
        Assert.Equal(PjrtElementType.Float32, c.ElementType);
        Assert.Equal([19.0f, 22.0f, 43.0f, 50.0f], c.ToArray());
    }

    [Fact]
    public void MatMul_Rectangular_ReturnsExpectedResult()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var a = backend.CreateMatrix(2, 3, [1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f]);
        using var b = backend.CreateMatrix(3, 2, [7.0f, 8.0f, 9.0f, 10.0f, 11.0f, 12.0f]);

        using var c = backend.MatMul(a, b);

        Assert.Equal(2, c.Rows);
        Assert.Equal(2, c.Cols);
        Assert.Equal([58.0f, 64.0f, 139.0f, 154.0f], c.ToArray());
    }

    [Fact]
    public void MatMul_InvalidShape_ThrowsBeforePjrt()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var a = backend.CreateMatrix(2, 3, [1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f]);
        using var b = backend.CreateMatrix(2, 2, [1.0f, 2.0f, 3.0f, 4.0f]);

        Assert.Throws<ArgumentException>(() => backend.MatMul(a, b));
    }

    [Fact]
    public void Add_2x2_ReturnsExpectedResult()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var a = backend.CreateMatrix(2, 2, [1.0f, 2.0f, 3.0f, 4.0f]);
        using var b = backend.CreateMatrix(2, 2, [10.0f, 20.0f, 30.0f, 40.0f]);

        using var c = backend.Add(a, b);

        Assert.Equal(2, c.Rows);
        Assert.Equal(2, c.Cols);
        Assert.Equal([11.0f, 22.0f, 33.0f, 44.0f], c.ToArray());
    }

    [Fact]
    public void Multiply_2x2_ReturnsExpectedResult()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var a = backend.CreateMatrix(2, 2, [1.0f, 2.0f, 3.0f, 4.0f]);
        using var b = backend.CreateMatrix(2, 2, [10.0f, 20.0f, 30.0f, 40.0f]);

        using var c = backend.Multiply(a, b);

        Assert.Equal(2, c.Rows);
        Assert.Equal(2, c.Cols);
        Assert.Equal([10.0f, 40.0f, 90.0f, 160.0f], c.ToArray());
    }

    [Fact]
    public void Subtract_2x2_ReturnsExpectedResult()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var a = backend.CreateMatrix(2, 2, [10.0f, 20.0f, 30.0f, 40.0f]);
        using var b = backend.CreateMatrix(2, 2, [1.0f, 2.0f, 3.0f, 4.0f]);

        using var c = backend.Subtract(a, b);

        Assert.Equal(2, c.Rows);
        Assert.Equal(2, c.Cols);
        Assert.Equal([9.0f, 18.0f, 27.0f, 36.0f], c.ToArray());
    }

    [Fact]
    public void Negate_2x2_ReturnsExpectedResult()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var a = backend.CreateMatrix(2, 2, [1.0f, -2.0f, 3.0f, -4.0f]);

        using var c = backend.Negate(a);

        Assert.Equal(2, c.Rows);
        Assert.Equal(2, c.Cols);
        Assert.Equal([-1.0f, 2.0f, -3.0f, 4.0f], c.ToArray());
    }

    [Fact]
    public void Sum_2x3_ReturnsOneByOneExpectedResult()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var a = backend.CreateMatrix(2, 3, [1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f]);

        using var c = backend.Sum(a);

        Assert.Equal(1, c.Rows);
        Assert.Equal(1, c.Cols);
        Assert.Equal([21.0f], c.ToArray());
    }

    [Fact]
    public void Mean_2x3_ReturnsOneByOneExpectedResult()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var a = backend.CreateMatrix(2, 3, [1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f]);

        using var c = backend.Mean(a);

        Assert.Equal(1, c.Rows);
        Assert.Equal(1, c.Cols);
        Assert.Equal([3.5f], c.ToArray());
    }

    [Fact]
    public void Transpose_2x3_ReturnsExpectedResult()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var a = backend.CreateMatrix(2, 3, [1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f]);

        using var c = backend.Transpose(a);

        Assert.Equal(3, c.Rows);
        Assert.Equal(2, c.Cols);
        Assert.Equal([1.0f, 4.0f, 2.0f, 5.0f, 3.0f, 6.0f], c.ToArray());
    }

    [Fact]
    public void Scale_2x2_ReturnsExpectedResult()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var a = backend.CreateMatrix(2, 2, [1.0f, -2.0f, 3.0f, -4.0f]);

        using var c = backend.Scale(a, -2.0f);

        Assert.Equal(2, c.Rows);
        Assert.Equal(2, c.Cols);
        Assert.Equal([-2.0f, 4.0f, -6.0f, 8.0f], c.ToArray());
    }

    [Fact]
    public void ReLU_2x3_ReturnsExpectedResult()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var a = backend.CreateMatrix(2, 3, [-2.0f, -0.0f, 1.0f, 3.0f, -4.0f, 5.0f]);

        using var c = backend.ReLU(a);

        Assert.Equal(2, c.Rows);
        Assert.Equal(3, c.Cols);
        Assert.Equal([0.0f, 0.0f, 1.0f, 3.0f, 0.0f, 5.0f], c.ToArray());
    }

    [Fact]
    public void Norm_1x4_ReturnsOneByOneExpectedResult()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var a = backend.CreateMatrix(1, 4, [1.0f, 2.0f, 2.0f, 4.0f]);

        using var c = backend.Norm(a);

        Assert.Equal(1, c.Rows);
        Assert.Equal(1, c.Cols);
        Assert.Equal([5.0f], c.ToArray());
    }

    [Fact]
    public void MatrixInverse_1x1_ReturnsReciprocal()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var a = backend.CreateMatrix(1, 1, [4.0f]);

        using var inverse = backend.MatrixInverse(a);

        Assert.Equal(1, inverse.Rows);
        Assert.Equal(1, inverse.Cols);
        Assert.Equal([0.25f], inverse.ToArray());
    }

    [Fact]
    public void MatrixInverse_2x2_ReturnsExpectedResult()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var a = backend.CreateMatrix(2, 2, [2.0f, 1.0f, 1.0f, 1.0f]);

        using var inverse = backend.MatrixInverse(a);

        Assert.Equal(2, inverse.Rows);
        Assert.Equal(2, inverse.Cols);
        AssertClose([1.0f, -1.0f, -1.0f, 2.0f], inverse.ToArray());
    }

    [Fact]
    public void LinearSolve_1x1_ReturnsExpectedResult()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var matrix = backend.CreateMatrix(1, 1, [4.0f]);
        using var rightHandSide = backend.CreateMatrix(1, 3, [8.0f, 12.0f, 20.0f]);

        using var solution = backend.LinearSolve(matrix, rightHandSide);

        Assert.Equal(1, solution.Rows);
        Assert.Equal(3, solution.Cols);
        AssertClose([2.0f, 3.0f, 5.0f], solution.ToArray());
    }

    [Fact]
    public void LinearSolve_2x2_SolvesMultipleRightHandSides()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var matrix = backend.CreateMatrix(2, 2, [2.0f, 1.0f, 1.0f, 1.0f]);
        using var rightHandSide = backend.CreateMatrix(2, 2, [5.0f, 1.0f, 3.0f, 0.0f]);

        using var solution = backend.LinearSolve(matrix, rightHandSide);

        Assert.Equal(2, solution.Rows);
        Assert.Equal(2, solution.Cols);
        AssertClose([2.0f, 1.0f, 1.0f, -1.0f], solution.ToArray());
    }

    [Fact]
    public void LinearSolve_3x3_ReturnsExpectedResult()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var matrix = backend.CreateMatrix(3, 3, [2.0f, 0.0f, 0.0f, 0.0f, 3.0f, 0.0f, 0.0f, 0.0f, 4.0f]);
        using var rightHandSide = backend.CreateMatrix(3, 2, [2.0f, 4.0f, 9.0f, 12.0f, 20.0f, 24.0f]);

        using var solution = backend.LinearSolve(matrix, rightHandSide);

        Assert.Equal(3, solution.Rows);
        Assert.Equal(2, solution.Cols);
        AssertClose([1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f], solution.ToArray());
    }

    [Fact]
    public void MatrixInverse_3x3_ReturnsExpectedResult()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var matrix = backend.CreateMatrix(3, 3, [2.0f, 0.0f, 0.0f, 0.0f, 4.0f, 0.0f, 0.0f, 0.0f, 5.0f]);

        using var inverse = backend.MatrixInverse(matrix);

        Assert.Equal(3, inverse.Rows);
        Assert.Equal(3, inverse.Cols);
        AssertClose([0.5f, 0.0f, 0.0f, 0.0f, 0.25f, 0.0f, 0.0f, 0.0f, 0.2f], inverse.ToArray());
    }

    [Fact]
    public void LinearSolve_RejectsUnsupportedTooLargeMatrixBeforePjrt()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var matrix = backend.CreateMatrix(9, 9);
        using var rightHandSide = backend.CreateMatrix(9, 1);

        Assert.Throws<NotSupportedException>(() => backend.LinearSolve(matrix, rightHandSide));
    }

    [Fact]
    public void LinearSolve_PivotsRowsWhenLeadingPivotIsZero()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var matrix = backend.CreateMatrix(2, 2, [0.0f, 1.0f, 1.0f, 1.0f]);
        using var rightHandSide = backend.CreateMatrix(2, 1, [1.0f, 2.0f]);

        using var solution = backend.LinearSolve(matrix, rightHandSide);

        AssertClose([1.0f, 1.0f], solution.ToArray());
    }

    [Fact]
    public void LinearSolve_RejectsSingularMatrixBeforePjrt()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var matrix = backend.CreateMatrix(2, 2, [0.0f, 0.0f, 0.0f, 0.0f]);
        using var rightHandSide = backend.CreateMatrix(2, 1, [1.0f, 2.0f]);

        Assert.Throws<ArithmeticException>(() => backend.LinearSolve(matrix, rightHandSide));
    }

    [Fact]
    public void TriangularSolve_Float32_UsesStableHloPrimitiveForLargerSystems()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var matrix = backend.CreateMatrix(
            10,
            10,
            [
                1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 2, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 0, 3, 0, 0, 0, 0, 0, 0, 0,
                0, 0, 0, 4, 0, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 5, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 6, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 7, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 0, 8, 0, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 9, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 10
            ]);
        using var rightHandSide = backend.CreateMatrix(10, 1, [1, 4, 9, 16, 25, 36, 49, 64, 81, 100]);

        using var solution = backend.TriangularSolve(matrix, rightHandSide, lower: true);

        AssertClose([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], solution.ToArray());
    }

    [Fact]
    public void LinearSolve_LargeTriangularMatrix_RoutesToStableHloTriangularSolve()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var matrix = backend.CreateMatrix(
            10,
            10,
            [
                1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 2, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 0, 3, 0, 0, 0, 0, 0, 0, 0,
                0, 0, 0, 4, 0, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 5, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 6, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 7, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 0, 8, 0, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 9, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 10
            ]);
        using var rightHandSide = backend.CreateMatrix(10, 1, [1, 4, 9, 16, 25, 36, 49, 64, 81, 100]);

        using var solution = backend.LinearSolve(matrix, rightHandSide);

        AssertClose([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], solution.ToArray());
    }

    [Fact]
    public void LinearSolve_LargeSymmetricPositiveDefiniteMatrix_RoutesToStableHloCholeskySolve()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var matrix = backend.CreateMatrix(
            10,
            10,
            [
                4, 1, 0, 0, 0, 0, 0, 0, 0, 0,
                1, 4, 1, 0, 0, 0, 0, 0, 0, 0,
                0, 1, 4, 1, 0, 0, 0, 0, 0, 0,
                0, 0, 1, 4, 1, 0, 0, 0, 0, 0,
                0, 0, 0, 1, 4, 1, 0, 0, 0, 0,
                0, 0, 0, 0, 1, 4, 1, 0, 0, 0,
                0, 0, 0, 0, 0, 1, 4, 1, 0, 0,
                0, 0, 0, 0, 0, 0, 1, 4, 1, 0,
                0, 0, 0, 0, 0, 0, 0, 1, 4, 1,
                0, 0, 0, 0, 0, 0, 0, 0, 1, 4
            ]);
        using var rightHandSide = backend.CreateMatrix(10, 1, [6, 12, 18, 24, 30, 36, 42, 48, 54, 49]);

        using var solution = backend.LinearSolve(matrix, rightHandSide);

        AssertClose([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], solution.ToArray(), tolerance: 1e-4f);
    }

    [Fact]
    public void ShapeOps_Float32_ReturnExpectedResults()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var value = backend.CreateMatrix(2, 3, [1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f]);
        using var reshaped = backend.Reshape(value, 3, 2);
        using var sliced = backend.Slice(value, 0, 1, 2, 2);
        using var scalar = backend.CreateMatrix(1, 1, [7.0f]);
        using var broadcast = backend.Broadcast(scalar, 2, 3);
        using var concatenatedRows = backend.Concatenate(value, value, axis: 0);
        using var concatenatedCols = backend.Concatenate(value, value, axis: 1);

        Assert.Equal([1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f], reshaped.ToArray());
        Assert.Equal([2.0f, 3.0f, 5.0f, 6.0f], sliced.ToArray());
        Assert.Equal([7.0f, 7.0f, 7.0f, 7.0f, 7.0f, 7.0f], broadcast.ToArray());
        Assert.Equal(4, concatenatedRows.Rows);
        Assert.Equal(3, concatenatedRows.Cols);
        Assert.Equal([1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f, 1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f], concatenatedRows.ToArray());
        Assert.Equal(2, concatenatedCols.Rows);
        Assert.Equal(6, concatenatedCols.Cols);
        Assert.Equal([1.0f, 2.0f, 3.0f, 1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f, 4.0f, 5.0f, 6.0f], concatenatedCols.ToArray());
    }

    [Fact]
    public void ShapeOps_RejectInvalidShapesBeforePjrt()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var value = backend.CreateMatrix(2, 3, [1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f]);

        Assert.Throws<ArgumentException>(() => backend.Reshape(value, 4, 2));
        Assert.Throws<ArgumentException>(() => backend.Broadcast(value, 2, 3));
        Assert.Throws<ArgumentException>(() => backend.Slice(value, 1, 2, 2, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => backend.Concatenate(value, value, axis: 2));
    }

    [Fact]
    public void Add_InvalidShape_ThrowsBeforePjrt()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var a = backend.CreateMatrix(2, 2, [1.0f, 2.0f, 3.0f, 4.0f]);
        using var b = backend.CreateMatrix(1, 4, [1.0f, 2.0f, 3.0f, 4.0f]);

        Assert.Throws<ArgumentException>(() => backend.Add(a, b));
    }

    [Fact]
    public void Multiply_InvalidShape_ThrowsBeforePjrt()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var a = backend.CreateMatrix(2, 2, [1.0f, 2.0f, 3.0f, 4.0f]);
        using var b = backend.CreateMatrix(1, 4, [1.0f, 2.0f, 3.0f, 4.0f]);

        Assert.Throws<ArgumentException>(() => backend.Multiply(a, b));
    }

    [Fact]
    public void Subtract_InvalidShape_ThrowsBeforePjrt()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var a = backend.CreateMatrix(2, 2, [1.0f, 2.0f, 3.0f, 4.0f]);
        using var b = backend.CreateMatrix(1, 4, [1.0f, 2.0f, 3.0f, 4.0f]);

        Assert.Throws<ArgumentException>(() => backend.Subtract(a, b));
    }

    [Fact]
    public void RepeatedSameShapeOperations_ReuseCachedExecutablesAndReturnExpectedResults()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        Assert.Equal(0, backend.CachedExecutableCount);

        using var a = backend.CreateMatrix(2, 2, [1.0f, 2.0f, 3.0f, 4.0f]);
        using var b = backend.CreateMatrix(2, 2, [5.0f, 6.0f, 7.0f, 8.0f]);
        using var c = backend.CreateMatrix(2, 2, [2.0f, 4.0f, 6.0f, 8.0f]);
        using var d = backend.CreateMatrix(2, 2, [1.0f, 3.0f, 5.0f, 7.0f]);

        using var matMul1 = backend.MatMul(a, b);
        Assert.Equal(1, backend.CachedExecutableCount);
        using var matMul2 = backend.MatMul(c, d);
        Assert.Equal(1, backend.CachedExecutableCount);
        using var add1 = backend.Add(a, b);
        Assert.Equal(2, backend.CachedExecutableCount);
        using var add2 = backend.Add(c, d);
        Assert.Equal(2, backend.CachedExecutableCount);
        using var multiply1 = backend.Multiply(a, b);
        Assert.Equal(3, backend.CachedExecutableCount);
        using var multiply2 = backend.Multiply(c, d);
        Assert.Equal(3, backend.CachedExecutableCount);
        using var subtract1 = backend.Subtract(b, a);
        Assert.Equal(4, backend.CachedExecutableCount);
        using var subtract2 = backend.Subtract(d, c);
        Assert.Equal(4, backend.CachedExecutableCount);
        using var negate1 = backend.Negate(a);
        Assert.Equal(5, backend.CachedExecutableCount);
        using var negate2 = backend.Negate(c);
        Assert.Equal(5, backend.CachedExecutableCount);
        using var sum1 = backend.Sum(a);
        Assert.Equal(6, backend.CachedExecutableCount);
        using var sum2 = backend.Sum(c);
        Assert.Equal(6, backend.CachedExecutableCount);
        using var mean1 = backend.Mean(a);
        Assert.Equal(7, backend.CachedExecutableCount);
        using var mean2 = backend.Mean(c);
        Assert.Equal(7, backend.CachedExecutableCount);
        using var transpose1 = backend.Transpose(a);
        Assert.Equal(8, backend.CachedExecutableCount);
        using var transpose2 = backend.Transpose(c);
        Assert.Equal(8, backend.CachedExecutableCount);
        using var scale1 = backend.Scale(a, 2.0f);
        Assert.Equal(9, backend.CachedExecutableCount);
        using var scale2 = backend.Scale(c, 2.0f);
        Assert.Equal(9, backend.CachedExecutableCount);
        using var scale3 = backend.Scale(c, 3.0f);
        Assert.Equal(10, backend.CachedExecutableCount);
        using var norm1 = backend.Norm(a);
        Assert.Equal(11, backend.CachedExecutableCount);
        using var norm2 = backend.Norm(c);
        Assert.Equal(11, backend.CachedExecutableCount);

        Assert.Equal([19.0f, 22.0f, 43.0f, 50.0f], matMul1.ToArray());
        Assert.Equal([22.0f, 34.0f, 46.0f, 74.0f], matMul2.ToArray());
        Assert.Equal([6.0f, 8.0f, 10.0f, 12.0f], add1.ToArray());
        Assert.Equal([3.0f, 7.0f, 11.0f, 15.0f], add2.ToArray());
        Assert.Equal([5.0f, 12.0f, 21.0f, 32.0f], multiply1.ToArray());
        Assert.Equal([2.0f, 12.0f, 30.0f, 56.0f], multiply2.ToArray());
        Assert.Equal([4.0f, 4.0f, 4.0f, 4.0f], subtract1.ToArray());
        Assert.Equal([-1.0f, -1.0f, -1.0f, -1.0f], subtract2.ToArray());
        Assert.Equal([-1.0f, -2.0f, -3.0f, -4.0f], negate1.ToArray());
        Assert.Equal([-2.0f, -4.0f, -6.0f, -8.0f], negate2.ToArray());
        Assert.Equal([10.0f], sum1.ToArray());
        Assert.Equal([20.0f], sum2.ToArray());
        Assert.Equal([2.5f], mean1.ToArray());
        Assert.Equal([5.0f], mean2.ToArray());
        Assert.Equal([1.0f, 3.0f, 2.0f, 4.0f], transpose1.ToArray());
        Assert.Equal([2.0f, 6.0f, 4.0f, 8.0f], transpose2.ToArray());
        Assert.Equal([2.0f, 4.0f, 6.0f, 8.0f], scale1.ToArray());
        Assert.Equal([4.0f, 8.0f, 12.0f, 16.0f], scale2.ToArray());
        Assert.Equal([6.0f, 12.0f, 18.0f, 24.0f], scale3.ToArray());
        Assert.Equal([5.477226f], norm1.ToArray());
        Assert.Equal([10.954452f], norm2.ToArray());
    }

    [Fact]
    public void TryCreate_ReturnsFalseForMissingRuntime()
    {
        var created = PjrtFloatBackend.TryCreate(
            out var backend,
            new PjrtPluginResolverOptions
            {
                SearchRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
                Backend = "cpu"
            });

        using (backend)
        {
            Assert.False(created);
            Assert.Null(backend);
        }
    }

    [Fact]
    public void CreateMatrix_InvalidDataLength_Throws()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        Assert.Throws<ArgumentException>(() => backend.CreateMatrix(2, 2, [1.0f, 2.0f, 3.0f]));
    }

    [Fact]
    public void CreateMatrix_DefaultData_CreatesZeroMatrix()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var tensor = backend.CreateMatrix(2, 3);

        Assert.Equal(2, tensor.Rows);
        Assert.Equal(3, tensor.Cols);
        Assert.Equal([0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f], tensor.ToArray());
    }

    [Fact]
    public void HardwareBackendInterface_CreateMatrixDefaultData_CreatesZeroMatrix()
    {
        using var concreteBackend = CreateBackendOrNull();
        if (concreteBackend is null)
            return;

        IExecutionBackend<float> backend = concreteBackend;
        using var tensor = backend.CreateMatrix(2, 3);

        var actual = new float[6];
        tensor.CopyToHost(actual);
        Assert.Equal([0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f], actual);
    }

    [Fact]
    public void CreateMatrix_AfterBackendDispose_Throws()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        backend.Dispose();

        Assert.Throws<ObjectDisposedException>(() => backend.CreateMatrix(1, 1, [1.0f]));
    }

    [Fact]
    public void CachedExecutableCount_AfterBackendDispose_Throws()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        backend.Dispose();

        Assert.Throws<ObjectDisposedException>(() => backend.CachedExecutableCount);
    }

    [Fact]
    public void ToArray_AfterTensorDispose_Throws()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        var tensor = backend.CreateMatrix(1, 1, [1.0f]);
        tensor.Dispose();

        Assert.Throws<ObjectDisposedException>(() => tensor.ToArray());
    }

    [Fact]
    public void BackendDispose_DisposesLiveTensorsBeforeNativeRuntime()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        var tensor = backend.CreateMatrix(1, 1, [1.0f]);

        backend.Dispose();

        Assert.Throws<ObjectDisposedException>(() => tensor.ToArray());
        tensor.Dispose();
    }

    [Fact]
    public void HardwareBackendInterface_MatMulAndReductions_ReturnExpectedResults()
    {
        using var concreteBackend = CreateBackendOrNull();
        if (concreteBackend is null)
            return;

        IExecutionBackend<float> backend = concreteBackend;
        using var a = backend.CreateMatrix(2, 2, [1.0f, 2.0f, 3.0f, 4.0f]);
        using var b = backend.CreateMatrix(2, 2, [5.0f, 6.0f, 7.0f, 8.0f]);

        using var c = backend.MatMul(a, b);
        using var transposed = backend.Transpose(c);

        var result = new float[4];
        c.CopyToHost(result);
        Assert.Equal([19.0f, 22.0f, 43.0f, 50.0f], result);
        transposed.CopyToHost(result);
        Assert.Equal([19.0f, 43.0f, 22.0f, 50.0f], result);
        Assert.Equal(134.0f, backend.Sum(c));
        Assert.Equal(33.5f, backend.Mean(c));
    }

    [Fact]
    public void ExactRationalMatrices_ApproximateToPjrtFloatTensor_MatMulMatchesExactOracle()
    {
        using var concreteBackend = CreateBackendOrNull();
        if (concreteBackend is null)
            return;

        IExecutionBackend<float> backend = concreteBackend;
        var exactA = Matrix<Rational>.FromArray(
            2,
            2,
            [(Rational)1, Rational.Create((Integer)1, (Integer)2), (Rational)3, (Rational)(-1)]);
        var exactB = Matrix<Rational>.FromArray(
            2,
            2,
            [(Rational)2, (Rational)(-4), Rational.Create((Integer)3, (Integer)2), (Rational)5]);
        var exactProduct = exactA * exactB;

        using var a = HardwareConvert.ApproximateToFloat32Tensor(backend, exactA);
        using var b = HardwareConvert.ApproximateToFloat32Tensor(backend, exactB);
        using var product = backend.MatMul(a, b);

        var actual = new float[4];
        product.CopyToHost(actual);
        Assert.Equal(
            [
                (float)RationalToDouble(exactProduct[0, 0]),
                (float)RationalToDouble(exactProduct[0, 1]),
                (float)RationalToDouble(exactProduct[1, 0]),
                (float)RationalToDouble(exactProduct[1, 1])
            ],
            actual);
    }

    [Fact]
    public void HardwareBackendInterface_ScaleAndDot_ReturnExpectedResults()
    {
        using var concreteBackend = CreateBackendOrNull();
        if (concreteBackend is null)
            return;

        IExecutionBackend<float> backend = concreteBackend;
        using var a = backend.CreateMatrix(1, 4, [1.0f, 2.0f, 3.0f, 4.0f]);
        using var b = backend.CreateMatrix(1, 4, [5.0f, 6.0f, 7.0f, 8.0f]);

        using var scaled = backend.Scale(a, 2.0f);

        var result = new float[4];
        scaled.CopyToHost(result);
        Assert.Equal([2.0f, 4.0f, 6.0f, 8.0f], result);
        Assert.Equal(70.0f, backend.Dot(a, b));
        Assert.Equal(5.477226f, backend.Norm(a));
    }

    [Fact]
    public void PjrtFloatTensor_UpdateFromSpan_ReplacesDeviceBuffer()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var tensor = backend.CreateMatrix(1, 3, [1.0f, 2.0f, 3.0f]);

        tensor.UpdateFromSpan([4.0f, 5.0f, 6.0f]);

        Assert.Equal([4.0f, 5.0f, 6.0f], tensor.ToArray());
    }

    [Fact]
    public void HardwareBackendInterface_LinearSolveAndMatrixInverse_ReturnExpectedResults()
    {
        using var concreteBackend = CreateBackendOrNull();
        if (concreteBackend is null)
            return;

        IExecutionBackend<float> backend = concreteBackend;
        using var matrix = backend.CreateMatrix(2, 2, [2.0f, 1.0f, 1.0f, 1.0f]);
        using var rightHandSide = backend.CreateMatrix(2, 1, [5.0f, 3.0f]);

        using var solution = backend.LinearSolve(matrix, rightHandSide);
        using var inverse = backend.MatrixInverse(matrix);

        var solutionOutput = new float[2];
        solution.CopyToHost(solutionOutput);
        AssertClose([2.0f, 1.0f], solutionOutput);

        var inverseOutput = new float[4];
        inverse.CopyToHost(inverseOutput);
        AssertClose([1.0f, -1.0f, -1.0f, 2.0f], inverseOutput);
    }

    [Fact]
    public void TensorTape_MatMulMean_ReturnsExpectedInputGradients()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var aValue = backend.CreateMatrix(2, 2, [1.0f, 2.0f, 3.0f, 4.0f]);
        using var bValue = backend.CreateMatrix(2, 2, [5.0f, 6.0f, 7.0f, 8.0f]);
        var tape = new PjrtTensorTape(backend);
        var a = tape.Watch(aValue);
        var b = tape.Watch(bValue);

        var loss = tape.Mean(tape.MatMul(a, b));

        using var gradA = tape.Gradient(loss, a);
        using var gradB = tape.Gradient(loss, b);

        AssertClose([2.75f, 3.75f, 2.75f, 3.75f], gradA.ToArray());
        AssertClose([1.0f, 1.0f, 1.5f, 1.5f], gradB.ToArray());
    }

    [Fact]
    public void TensorTape_ShapeAdjoints_ReturnExpectedInputGradient()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var xValue = backend.CreateMatrix(2, 3, [1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f]);
        var tape = new PjrtTensorTape(backend);
        var x = tape.Watch(xValue);

        var sliced = tape.Slice(x, 0, 1, 2, 2);
        var reshaped = tape.Reshape(sliced, 1, 4);
        var loss = tape.Sum(reshaped);

        using var gradX = tape.Gradient(loss, x);

        AssertClose([0.0f, 1.0f, 1.0f, 0.0f, 1.0f, 1.0f], gradX.ToArray());
    }

    [Fact]
    public void TensorTape_ReLU_ReturnsExpectedInputGradient()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var xValue = backend.CreateMatrix(2, 3, [-2.0f, 0.0f, 1.0f, 3.0f, -4.0f, 5.0f]);
        var tape = new PjrtTensorTape(backend);
        var x = tape.Watch(xValue);

        var loss = tape.Sum(tape.ReLU(x));

        using var gradX = tape.Gradient(loss, x);

        AssertClose([0.0f, 1.0f, 1.0f, 1.0f, 0.0f, 1.0f], gradX.ToArray());
    }

    [Fact]
    public void DoubleBackend_MatMulAndLinearSolve_ReturnExpectedResults()
    {
        using var backend = CreateDoubleBackendOrNull();
        if (backend is null)
            return;

        using var a = backend.CreateMatrix(2, 2, [1.0, 2.0, 3.0, 4.0]);
        using var b = backend.CreateMatrix(2, 2, [5.0, 6.0, 7.0, 8.0]);

        using var product = backend.MatMul(a, b);

        Assert.Equal(PjrtElementType.Float64, product.ElementType);
        AssertClose([19.0, 22.0, 43.0, 50.0], product.ToArray());

        using var matrix = backend.CreateMatrix(3, 3, [2.0, 0.0, 0.0, 0.0, 3.0, 0.0, 0.0, 0.0, 4.0]);
        using var rightHandSide = backend.CreateMatrix(3, 2, [2.0, 4.0, 9.0, 12.0, 20.0, 24.0]);

        using var solution = backend.LinearSolve(matrix, rightHandSide);

        AssertClose([1.0, 2.0, 3.0, 4.0, 5.0, 6.0], solution.ToArray());
    }

    [Fact]
    public void DoubleBackend_HardwareInterface_MatrixInverse_ReturnsExpectedResult()
    {
        using var concreteBackend = CreateDoubleBackendOrNull();
        if (concreteBackend is null)
            return;

        IExecutionBackend<double> backend = concreteBackend;
        using var matrix = backend.CreateMatrix(2, 2, [2.0, 1.0, 1.0, 1.0]);

        using var inverse = backend.MatrixInverse(matrix);

        var output = new double[4];
        inverse.CopyToHost(output);
        AssertClose([1.0, -1.0, -1.0, 2.0], output);
    }

    [Fact]
    public void DoubleBackend_ShapeOpsAndPivoting_ReturnExpectedResults()
    {
        using var backend = CreateDoubleBackendOrNull();
        if (backend is null)
            return;

        using var value = backend.CreateMatrix(2, 2, [1.0, 2.0, 3.0, 4.0]);
        using var reshaped = backend.Reshape(value, 1, 4);
        using var sliced = backend.Slice(value, 0, 1, 2, 1);
        using var scalar = backend.CreateMatrix(1, 1, [9.0]);
        using var broadcast = backend.Broadcast(scalar, 1, 3);

        AssertClose([1.0, 2.0, 3.0, 4.0], reshaped.ToArray());
        AssertClose([2.0, 4.0], sliced.ToArray());
        AssertClose([9.0, 9.0, 9.0], broadcast.ToArray());

        using var pivotMatrix = backend.CreateMatrix(2, 2, [0.0, 1.0, 1.0, 1.0]);
        using var rightHandSide = backend.CreateMatrix(2, 1, [1.0, 2.0]);
        using var solution = backend.LinearSolve(pivotMatrix, rightHandSide);
        AssertClose([1.0, 1.0], solution.ToArray());

        using var singularMatrix = backend.CreateMatrix(2, 2, [0.0, 0.0, 0.0, 0.0]);
        Assert.Throws<ArithmeticException>(() => backend.LinearSolve(singularMatrix, rightHandSide));
    }

    [Fact]
    public void Int32Backend_ArithmeticAndShapeOps_ReturnExpectedResults()
    {
        using var backend = CreateInt32BackendOrNull();
        if (backend is null)
            return;

        using var a = backend.CreateMatrix(2, 2, [1, 2, 3, 4]);
        using var b = backend.CreateMatrix(2, 2, [5, 6, 7, 8]);
        using var product = backend.MatMul(a, b);
        using var add = backend.Add(a, b);
        using var subtract = backend.Subtract(b, a);
        using var multiply = backend.Multiply(a, b);
        using var negate = backend.Negate(a);
        using var sum = backend.Sum(a);
        using var transpose = backend.Transpose(a);
        using var scaled = backend.Scale(a, 3);
        using var reshaped = backend.Reshape(a, 1, 4);
        using var sliced = backend.Slice(a, 0, 1, 2, 1);
        using var scalar = backend.CreateMatrix(1, 1, [9]);
        using var broadcast = backend.Broadcast(scalar, 1, 3);
        using var concatenatedRows = backend.Concatenate(a, a, axis: 0);
        using var concatenatedCols = backend.Concatenate(a, a, axis: 1);

        Assert.Equal(PjrtElementType.Int32, product.ElementType);
        Assert.Equal([19, 22, 43, 50], product.ToArray());
        Assert.Equal([6, 8, 10, 12], add.ToArray());
        Assert.Equal([4, 4, 4, 4], subtract.ToArray());
        Assert.Equal([5, 12, 21, 32], multiply.ToArray());
        Assert.Equal([-1, -2, -3, -4], negate.ToArray());
        Assert.Equal([10], sum.ToArray());
        Assert.Equal([1, 3, 2, 4], transpose.ToArray());
        Assert.Equal([3, 6, 9, 12], scaled.ToArray());
        Assert.Equal([1, 2, 3, 4], reshaped.ToArray());
        Assert.Equal([2, 4], sliced.ToArray());
        Assert.Equal([9, 9, 9], broadcast.ToArray());
        Assert.Equal([1, 2, 3, 4, 1, 2, 3, 4], concatenatedRows.ToArray());
        Assert.Equal([1, 2, 1, 2, 3, 4, 3, 4], concatenatedCols.ToArray());
    }

    [Fact]
    public void Int32Backend_InterfaceRejectsFloatingOnlyOperations()
    {
        using var concreteBackend = CreateInt32BackendOrNull();
        if (concreteBackend is null)
            return;

        IExecutionBackend<int> backend = concreteBackend;
        using var matrix = backend.CreateMatrix(2, 2, [1, 2, 3, 4]);
        using var rightHandSide = backend.CreateMatrix(2, 1, [1, 2]);

        Assert.Throws<NotSupportedException>(() => backend.Mean(matrix));
        Assert.Throws<NotSupportedException>(() => backend.Norm(matrix));
        Assert.Throws<NotSupportedException>(() => backend.LinearSolve(matrix, rightHandSide));
        Assert.Throws<NotSupportedException>(() => backend.MatrixInverse(matrix));
    }

    [Fact]
    public void Int64Backend_ArithmeticAndShapeOps_ReturnExpectedResults()
    {
        using var backend = CreateInt64BackendOrNull();
        if (backend is null)
            return;

        using var a = backend.CreateMatrix(2, 2, [1L, 2L, 3L, 4L]);
        using var b = backend.CreateMatrix(2, 2, [5L, 6L, 7L, 8L]);
        using var product = backend.MatMul(a, b);
        using var add = backend.Add(a, b);
        using var subtract = backend.Subtract(b, a);
        using var multiply = backend.Multiply(a, b);
        using var negate = backend.Negate(a);
        using var sum = backend.Sum(a);
        using var transpose = backend.Transpose(a);
        using var scaled = backend.Scale(a, 4L);
        using var reshaped = backend.Reshape(a, 1, 4);
        using var sliced = backend.Slice(a, 0, 1, 2, 1);
        using var scalar = backend.CreateMatrix(1, 1, [11L]);
        using var broadcast = backend.Broadcast(scalar, 1, 3);
        using var concatenatedRows = backend.Concatenate(a, a, axis: 0);
        using var concatenatedCols = backend.Concatenate(a, a, axis: 1);

        Assert.Equal(PjrtElementType.Int64, product.ElementType);
        Assert.Equal([19L, 22L, 43L, 50L], product.ToArray());
        Assert.Equal([6L, 8L, 10L, 12L], add.ToArray());
        Assert.Equal([4L, 4L, 4L, 4L], subtract.ToArray());
        Assert.Equal([5L, 12L, 21L, 32L], multiply.ToArray());
        Assert.Equal([-1L, -2L, -3L, -4L], negate.ToArray());
        Assert.Equal([10L], sum.ToArray());
        Assert.Equal([1L, 3L, 2L, 4L], transpose.ToArray());
        Assert.Equal([4L, 8L, 12L, 16L], scaled.ToArray());
        Assert.Equal([1L, 2L, 3L, 4L], reshaped.ToArray());
        Assert.Equal([2L, 4L], sliced.ToArray());
        Assert.Equal([11L, 11L, 11L], broadcast.ToArray());
        Assert.Equal([1L, 2L, 3L, 4L, 1L, 2L, 3L, 4L], concatenatedRows.ToArray());
        Assert.Equal([1L, 2L, 1L, 2L, 3L, 4L, 3L, 4L], concatenatedCols.ToArray());
    }

    [Fact]
    public void Int64Backend_InterfaceRejectsFloatingOnlyOperations()
    {
        using var concreteBackend = CreateInt64BackendOrNull();
        if (concreteBackend is null)
            return;

        IExecutionBackend<long> backend = concreteBackend;
        using var matrix = backend.CreateMatrix(2, 2, [1L, 2L, 3L, 4L]);
        using var rightHandSide = backend.CreateMatrix(2, 1, [1L, 2L]);

        Assert.Throws<NotSupportedException>(() => backend.Mean(matrix));
        Assert.Throws<NotSupportedException>(() => backend.Norm(matrix));
        Assert.Throws<NotSupportedException>(() => backend.LinearSolve(matrix, rightHandSide));
        Assert.Throws<NotSupportedException>(() => backend.MatrixInverse(matrix));
    }

    [Fact]
    public void BFloat16_RoundTripsThroughStandardBitLayout()
    {
        var one = BFloat16.FromSingle(1.0f);
        var threeHalves = BFloat16.FromSingle(1.5f);

        Assert.Equal(0x3F80, one.Bits);
        Assert.Equal(1.0f, one.ToSingle());
        Assert.Equal(1.5f, threeHalves.ToSingle());
    }

    [Fact]
    public void BFloat16Backend_ArithmeticAndShapeOps_ReturnExpectedResults()
    {
        using var backend = CreateBFloat16BackendOrNull();
        if (backend is null)
            return;

        using var a = backend.CreateMatrix(2, 2, [1.0f, 2.0f, 3.0f, 4.0f]);
        using var b = backend.CreateMatrix(2, 2, [5.0f, 6.0f, 7.0f, 8.0f]);
        using var product = backend.MatMul(a, b);
        using var add = backend.Add(a, b);
        using var subtract = backend.Subtract(b, a);
        using var multiply = backend.Multiply(a, b);
        using var negate = backend.Negate(a);
        using var sum = backend.Sum(a);
        using var transpose = backend.Transpose(a);
        using var scaled = backend.Scale(a, BFloat16.FromSingle(2.0f));
        using var reshaped = backend.Reshape(a, 1, 4);
        using var sliced = backend.Slice(a, 0, 1, 2, 1);
        using var scalar = backend.CreateMatrix(1, 1, [9.0f]);
        using var broadcast = backend.Broadcast(scalar, 1, 3);
        using var concatenatedRows = backend.Concatenate(a, a, axis: 0);
        using var concatenatedCols = backend.Concatenate(a, a, axis: 1);

        Assert.Equal(PjrtElementType.BFloat16, product.ElementType);
        AssertClose([19.0f, 22.0f, 43.0f, 50.0f], product.ToSingleArray());
        AssertClose([6.0f, 8.0f, 10.0f, 12.0f], add.ToSingleArray());
        AssertClose([4.0f, 4.0f, 4.0f, 4.0f], subtract.ToSingleArray());
        AssertClose([5.0f, 12.0f, 21.0f, 32.0f], multiply.ToSingleArray());
        AssertClose([-1.0f, -2.0f, -3.0f, -4.0f], negate.ToSingleArray());
        AssertClose([10.0f], sum.ToSingleArray());
        AssertClose([1.0f, 3.0f, 2.0f, 4.0f], transpose.ToSingleArray());
        AssertClose([2.0f, 4.0f, 6.0f, 8.0f], scaled.ToSingleArray());
        AssertClose([1.0f, 2.0f, 3.0f, 4.0f], reshaped.ToSingleArray());
        AssertClose([2.0f, 4.0f], sliced.ToSingleArray());
        AssertClose([9.0f, 9.0f, 9.0f], broadcast.ToSingleArray());
        AssertClose([1.0f, 2.0f, 3.0f, 4.0f, 1.0f, 2.0f, 3.0f, 4.0f], concatenatedRows.ToSingleArray());
        AssertClose([1.0f, 2.0f, 1.0f, 2.0f, 3.0f, 4.0f, 3.0f, 4.0f], concatenatedCols.ToSingleArray());
    }

    [Fact]
    public void BFloat16Backend_InterfaceRejectsFloatingOnlyOperations()
    {
        using var concreteBackend = CreateBFloat16BackendOrNull();
        if (concreteBackend is null)
            return;

        IExecutionBackend<BFloat16> backend = concreteBackend;
        using var matrix = backend.CreateMatrix(2, 2, [BFloat16.FromSingle(1.0f), BFloat16.FromSingle(2.0f), BFloat16.FromSingle(3.0f), BFloat16.FromSingle(4.0f)]);
        using var rightHandSide = backend.CreateMatrix(2, 1, [BFloat16.FromSingle(1.0f), BFloat16.FromSingle(2.0f)]);

        Assert.Throws<NotSupportedException>(() => backend.Mean(matrix));
        Assert.Throws<NotSupportedException>(() => backend.Norm(matrix));
        Assert.Throws<NotSupportedException>(() => backend.LinearSolve(matrix, rightHandSide));
        Assert.Throws<NotSupportedException>(() => backend.MatrixInverse(matrix));
    }

    [Fact]
    public void RepeatedCompileExecuteDownloadDispose_DoesNotLeakBackendState()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        for (var i = 0; i < 100; i++)
        {
            using var a = backend.CreateMatrix(2, 2, [1.0f, 2.0f, 3.0f, 4.0f]);
            using var b = backend.CreateMatrix(2, 2, [5.0f, 6.0f, 7.0f, 8.0f]);
            using var product = backend.MatMul(a, b);
            using var reshaped = backend.Reshape(product, 1, 4);
            Assert.Equal([19.0f, 22.0f, 43.0f, 50.0f], reshaped.ToArray());
        }

        Assert.True(backend.CachedExecutableCount <= 1);
    }

    [Fact]
    public void BroadcastTo_RowVector_ReturnsExpectedResult()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var row = backend.CreateMatrix(1, 2, [3.0f, -1.0f]);
        using var broadcast = backend.BroadcastTo(row, 3, 2);

        Assert.Equal([3.0f, -1.0f, 3.0f, -1.0f, 3.0f, -1.0f], broadcast.ToArray());
    }

    [Fact]
    public void PjrtLinearLayerTrainStep_DecreasesMse_WhenRuntimeAvailable()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var inputs = backend.CreateMatrix(4, 1, [1.0f, 2.0f, 3.0f, 4.0f]);
        using var targets = backend.CreateMatrix(4, 1, [2.0f, 4.0f, 6.0f, 8.0f]);
        using var layer = new PjrtLinearLayer(backend, 1, 1, [0.0f], [0.0f], "regressor");
        var parameters = layer.Parameters.ToArray();
        var optimizer = new PjrtSgdOptimizer(backend, learningRate: 0.03f);

        var initialLoss = ComputePjrtLayerLoss(backend, layer, inputs, targets);
        for (var step = 0; step < 80; step++)
        {
            _ = PjrtTrainStep.Run(
                backend,
                parameters,
                optimizer,
                (tape, watched) =>
                {
                    var prediction = layer.Forward(tape, tape.Watch(inputs), watched);
                    return PjrtLosses.MeanSquaredError(tape, prediction, tape.Watch(targets));
                });
        }

        var finalLoss = ComputePjrtLayerLoss(backend, layer, inputs, targets);
        Assert.True(finalLoss < initialLoss, $"Expected loss to decrease from {initialLoss}, got {finalLoss}.");
        Assert.InRange(layer.Weight.Value.ToArray()[0], 1.8f, 2.2f);
    }

    [Fact]
    public void PjrtAdamOptimizer_DecreasesMse_WhenRuntimeAvailable()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var inputs = backend.CreateMatrix(4, 1, [1.0f, 2.0f, 3.0f, 4.0f]);
        using var targets = backend.CreateMatrix(4, 1, [2.0f, 4.0f, 6.0f, 8.0f]);
        using var weight = backend.CreateMatrix(1, 1, [0.0f]);
        using var optimizer = new PjrtAdamOptimizer(learningRate: 0.1f);

        var initialLoss = ComputePjrtLinearRegressionStep(backend, optimizer, inputs, targets, weight, update: false);
        for (var step = 0; step < 100; step++)
            _ = ComputePjrtLinearRegressionStep(backend, optimizer, inputs, targets, weight, update: true);

        var finalLoss = ComputePjrtLinearRegressionStep(backend, optimizer, inputs, targets, weight, update: false);
        Assert.True(finalLoss < initialLoss, $"Expected loss to decrease from {initialLoss}, got {finalLoss}.");
    }

    [Fact]
    public void BackendNeutralTrainStep_DecreasesMseOnPjrt_WhenRuntimeAvailable()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        var trainableBackend = new PjrtTrainableBackend(backend);
        using var inputs = backend.CreateMatrix(4, 1, [1.0f, 2.0f, 3.0f, 4.0f]);
        using var targets = backend.CreateMatrix(4, 1, [2.0f, 4.0f, 6.0f, 8.0f]);
        using var weight = new TrainableParameter<PjrtFloatTensor>("weight", backend.CreateMatrix(1, 1, [0.0f]));
        var parameters = new[] { weight };
        var optimizer = new TrainableSgdOptimizer<PjrtFloatTensor, PjrtFloatTensorVar, PjrtTensorTape>(trainableBackend, learningRate: 0.03f);

        var initialLoss = ComputeBackendNeutralLinearLoss(trainableBackend, inputs, targets, weight.Value);
        for (var step = 0; step < 80; step++)
        {
            _ = TrainStep.Run(
                trainableBackend,
                parameters,
                optimizer,
                (tape, watched) =>
                {
                    var prediction = trainableBackend.MatMul(tape, trainableBackend.Watch(tape, inputs), watched[weight]);
                    return TrainableLosses.MeanSquaredError(trainableBackend, tape, prediction, trainableBackend.Watch(tape, targets));
                });
        }

        var finalLoss = ComputeBackendNeutralLinearLoss(trainableBackend, inputs, targets, weight.Value);
        Assert.True(finalLoss < initialLoss, $"Expected loss to decrease from {initialLoss}, got {finalLoss}.");
        Assert.InRange(weight.Value.ToArray()[0], 1.8f, 2.2f);
    }

    [Fact]
    public void RepeatedPjrtTrainSteps_DoNotGrowLiveTensorCount_WhenRuntimeAvailable()
    {
        using var backend = CreateBackendOrNull();
        if (backend is null)
            return;

        using var inputs = backend.CreateMatrix(4, 1, [1.0f, 2.0f, 3.0f, 4.0f]);
        using var targets = backend.CreateMatrix(4, 1, [2.0f, 4.0f, 6.0f, 8.0f]);
        using var weight = backend.CreateMatrix(1, 1, [0.0f]);
        var optimizer = new PjrtSgdOptimizer(backend, learningRate: 0.03f);
        var baseline = backend.LiveTensorCount;

        for (var step = 0; step < 100; step++)
            _ = ComputePjrtLinearRegressionStep(backend, optimizer, inputs, targets, weight, update: true);

        Assert.Equal(baseline, backend.LiveTensorCount);
    }

    private static float ComputePjrtLayerLoss(PjrtFloatBackend backend, PjrtLinearLayer layer, PjrtFloatTensor inputs, PjrtFloatTensor targets)
    {
        using var tape = new PjrtTensorTape(backend);
        var prediction = layer.Forward(tape, tape.Watch(inputs));
        return PjrtLosses.MeanSquaredError(tape, prediction, tape.Watch(targets)).Value.ToArray()[0];
    }

    private static float ComputePjrtLinearRegressionStep(
        PjrtFloatBackend backend,
        IPjrtOptimizer optimizer,
        PjrtFloatTensor inputs,
        PjrtFloatTensor targets,
        PjrtFloatTensor weight,
        bool update)
    {
        using var tape = new PjrtTensorTape(backend);
        var x = tape.Watch(inputs);
        var y = tape.Watch(targets);
        var w = tape.Watch(weight);
        var prediction = tape.MatMul(x, w);
        var loss = PjrtLosses.MeanSquaredError(tape, prediction, y);
        var lossValue = loss.Value.ToArray()[0];

        if (update)
        {
            using var gradient = tape.Gradient(loss, w);
            optimizer.Step(weight, gradient);
        }

        return lossValue;
    }

    private static float ComputeBackendNeutralLinearLoss(
        PjrtTrainableBackend backend,
        PjrtFloatTensor inputs,
        PjrtFloatTensor targets,
        PjrtFloatTensor weight)
    {
        using var tape = backend.CreateTape();
        var prediction = backend.MatMul(tape, backend.Watch(tape, inputs), backend.Watch(tape, weight));
        var loss = TrainableLosses.MeanSquaredError(backend, tape, prediction, backend.Watch(tape, targets));
        return backend.ReadScalar(backend.Value(loss));
    }

    private static PjrtFloatBackend? CreateBackendOrNull()
    {
        var runtimeRoot = FindPreparedRuntimeRoot();
        if (runtimeRoot is null)
            return null;

        var options = new PjrtPluginResolverOptions
        {
            SearchRoot = runtimeRoot,
            Backend = "cpu"
        };

        return PjrtFloatBackend.TryCreate(out var backend, options) ? backend : null;
    }

    private static PjrtDoubleBackend? CreateDoubleBackendOrNull()
    {
        var runtimeRoot = FindPreparedRuntimeRoot();
        if (runtimeRoot is null)
            return null;

        var options = new PjrtPluginResolverOptions
        {
            SearchRoot = runtimeRoot,
            Backend = "cpu"
        };

        return PjrtDoubleBackend.TryCreate(out var backend, options) ? backend : null;
    }

    private static PjrtInt32Backend? CreateInt32BackendOrNull()
    {
        var runtimeRoot = FindPreparedRuntimeRoot();
        if (runtimeRoot is null)
            return null;

        var options = new PjrtPluginResolverOptions
        {
            SearchRoot = runtimeRoot,
            Backend = "cpu"
        };

        return PjrtInt32Backend.TryCreate(out var backend, options) ? backend : null;
    }

    private static PjrtInt64Backend? CreateInt64BackendOrNull()
    {
        var runtimeRoot = FindPreparedRuntimeRoot();
        if (runtimeRoot is null)
            return null;

        var options = new PjrtPluginResolverOptions
        {
            SearchRoot = runtimeRoot,
            Backend = "cpu"
        };

        return PjrtInt64Backend.TryCreate(out var backend, options) ? backend : null;
    }

    private static PjrtBFloat16Backend? CreateBFloat16BackendOrNull()
    {
        var runtimeRoot = FindPreparedRuntimeRoot();
        if (runtimeRoot is null)
            return null;

        var options = new PjrtPluginResolverOptions
        {
            SearchRoot = runtimeRoot,
            Backend = "cpu"
        };

        return PjrtBFloat16Backend.TryCreate(out var backend, options) ? backend : null;
    }

    private static string? FindPreparedRuntimeRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "artifacts", "pjrt");
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static double RationalToDouble(Rational value) =>
        (double)(System.Numerics.BigInteger)value.Numerator / (double)(System.Numerics.BigInteger)value.Denominator;

    private static void AssertClose(float[] expected, float[] actual, float tolerance = 1e-5f)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
            Assert.InRange(Math.Abs(actual[i] - expected[i]), 0.0f, tolerance);
    }

    private static void AssertClose(double[] expected, double[] actual, double tolerance = 1e-10)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
            Assert.InRange(Math.Abs(actual[i] - expected[i]), 0.0, tolerance);
    }
}
