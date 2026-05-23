using Helium.Hardware;

namespace Helium.Hardware.Tests;

public class TensorTapeTests
{
    [Fact]
    public void ElementwiseArithmetic_Backward_ComputesAdjoints()
    {
        var backend = new LocalHardwareBackend<double>();
        using var xValue = backend.CreateMatrix(1, 3, [2.0, 3.0, 4.0]);
        using var wValue = backend.CreateMatrix(1, 3, [5.0, 7.0, 11.0]);
        using var seed = backend.CreateMatrix(1, 3, [1.0, 1.0, 1.0]);
        using var tape = new TensorTape<double>(backend);

        var x = tape.Variable(xValue);
        var w = tape.Variable(wValue);
        var y = x * w - x;

        tape.Backward(y, seed);

        var gradX = new double[3];
        var gradW = new double[3];
        tape.RequireGradient(x).CopyToHost(gradX);
        tape.RequireGradient(w).CopyToHost(gradW);

        Assert.Equal([4.0, 6.0, 10.0], gradX);
        Assert.Equal([2.0, 3.0, 4.0], gradW);
        Assert.Equal(3, tape.EntryCount);
    }

    [Fact]
    public void MatMul_Backward_ComputesMatrixAdjoints()
    {
        var backend = new LocalHardwareBackend<double>();
        using var aValue = backend.CreateMatrix(2, 3, [1.0, 2.0, 3.0, 4.0, 5.0, 6.0]);
        using var bValue = backend.CreateMatrix(3, 2, [7.0, 8.0, 9.0, 10.0, 11.0, 12.0]);
        using var seed = backend.CreateMatrix(2, 2, [1.0, 2.0, 3.0, 4.0]);
        using var tape = new TensorTape<double>(backend);

        var a = tape.Variable(aValue);
        var b = tape.Variable(bValue);
        var y = a.MatMul(b);

        tape.Backward(y, seed);

        var gradA = new double[6];
        var gradB = new double[6];
        tape.RequireGradient(a).CopyToHost(gradA);
        tape.RequireGradient(b).CopyToHost(gradB);

        Assert.Equal([23.0, 29.0, 35.0, 53.0, 67.0, 81.0], gradA);
        Assert.Equal([13.0, 18.0, 17.0, 24.0, 21.0, 30.0], gradB);
        Assert.Equal(1, tape.EntryCount);
    }

    [Fact]
    public void MatMul_RecordsOneEntryRegardlessOfMatrixSize()
    {
        var backend = new LocalHardwareBackend<double>();
        var leftData = Enumerable.Range(1, 64).Select(i => (double)i).ToArray();
        var rightData = Enumerable.Range(1, 64).Select(i => (double)(65 - i)).ToArray();
        using var aValue = backend.CreateMatrix(8, 8, leftData);
        using var bValue = backend.CreateMatrix(8, 8, rightData);
        using var tape = new TensorTape<double>(backend);

        var a = tape.Variable(aValue);
        var b = tape.Variable(bValue);
        _ = a.MatMul(b);

        Assert.Equal(1, tape.EntryCount);
    }

    [Fact]
    public void MatMul_Backward_AccumulatesSharedInputGradient()
    {
        var backend = new LocalHardwareBackend<double>();
        using var xValue = backend.CreateMatrix(2, 2, [1.0, 2.0, 3.0, 4.0]);
        using var seed = backend.CreateMatrix(2, 2, [1.0, 1.0, 1.0, 1.0]);
        using var tape = new TensorTape<double>(backend);

        var x = tape.Variable(xValue);
        var y = x.MatMul(x);

        tape.Backward(y, seed);

        var grad = new double[4];
        tape.RequireGradient(x).CopyToHost(grad);

        Assert.Equal([7.0, 11.0, 9.0, 13.0], grad);
    }

    [Fact]
    public void LocalHardwareBackend_Transpose_ReturnsNewTensor()
    {
        var backend = new LocalHardwareBackend<float>();
        using var value = backend.CreateMatrix(2, 3, [1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f]);

        using var transposed = backend.Transpose(value);

        var output = new float[6];
        transposed.CopyToHost(output);
        Assert.Equal([1.0f, 4.0f, 2.0f, 5.0f, 3.0f, 6.0f], output);
        Assert.Equal(3, transposed.Rows);
        Assert.Equal(2, transposed.Cols);
    }

    [Fact]
    public void LocalHardwareBackend_MatrixInverse_InvertsSquareMatrix()
    {
        var backend = new LocalHardwareBackend<double>();
        using var value = backend.CreateMatrix(2, 2, [2.0, 1.0, 1.0, 1.0]);

        using var inverse = backend.MatrixInverse(value);

        var output = new double[4];
        inverse.CopyToHost(output);
        Assert.Equal([1.0, -1.0, -1.0, 2.0], output);
    }

    [Fact]
    public void LocalHardwareBackend_LinearSolve_SolvesMultipleRightHandSides()
    {
        var backend = new LocalHardwareBackend<double>();
        using var matrix = backend.CreateMatrix(2, 2, [2.0, 1.0, 1.0, 1.0]);
        using var rightHandSide = backend.CreateMatrix(2, 2, [5.0, 1.0, 3.0, 0.0]);

        using var solution = backend.LinearSolve(matrix, rightHandSide);

        var output = new double[4];
        solution.CopyToHost(output);
        Assert.Equal([2.0, 1.0, 1.0, -1.0], output);
    }

    [Fact]
    public void LocalHardwareBackend_LinearSolve_RejectsShapeMismatch()
    {
        var backend = new LocalHardwareBackend<double>();
        using var matrix = backend.CreateMatrix(2, 2, [2.0, 1.0, 1.0, 1.0]);
        using var rightHandSide = backend.CreateMatrix(3, 1, [1.0, 2.0, 3.0]);

        Assert.Throws<ArgumentException>(() => backend.LinearSolve(matrix, rightHandSide));
    }

    [Fact]
    public void LocalHardwareBackend_LinearSolve_RejectsSingularMatrix()
    {
        var backend = new LocalHardwareBackend<float>();
        using var matrix = backend.CreateMatrix(2, 2, [1.0f, 2.0f, 2.0f, 4.0f]);
        using var rightHandSide = backend.CreateMatrix(2, 1, [3.0f, 6.0f]);

        Assert.Throws<ArithmeticException>(() => backend.LinearSolve(matrix, rightHandSide));
    }

    [Fact]
    public void MatrixInverse_Backward_ComputesAdjoint()
    {
        var backend = new LocalHardwareBackend<double>();
        using var value = backend.CreateMatrix(2, 2, [2.0, 1.0, 1.0, 1.0]);
        using var seed = backend.CreateMatrix(2, 2, [1.0, 2.0, 3.0, 4.0]);
        using var tape = new TensorTape<double>(backend);

        var x = tape.Variable(value);
        var y = x.MatrixInverse();

        tape.Backward(y, seed);

        var gradient = new double[4];
        tape.RequireGradient(x).CopyToHost(gradient);
        Assert.Equal([-0.0, 2.0, 1.0, -7.0], gradient);
        Assert.Equal(1, tape.EntryCount);
    }

}
