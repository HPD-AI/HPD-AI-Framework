using HPD.ML.Backends.Mlx;
using HPD.ML.Backends.Mlx.Training;
using HPD.ML.Backends.Abstractions.Training;
using Helium.Primitives;

namespace HPD.ML.Backends.Tests;

public sealed class MlxBackendTests
{
    [Fact]
    public void Resolver_ReturnsUnavailableForMissingRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var resolution = MlxRuntimeResolver.Resolve(new MlxRuntimeOptions { SearchRoot = root });

        Assert.False(resolution.IsAvailable);
        Assert.Null(resolution.LibraryPath);
        Assert.Contains("prepare-mlx-runtime.cs", resolution.ReasonUnavailable);
        Assert.NotEmpty(resolution.SearchedPaths);
    }

    [Fact]
    public void Resolver_RejectsMissingExplicitPath()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "libmlxc.dylib");
        var resolution = MlxRuntimeResolver.Resolve(new MlxRuntimeOptions { NativeLibraryPath = missingPath });

        Assert.False(resolution.IsAvailable);
        Assert.Equal("explicit native library path", resolution.Source);
        Assert.Contains(Path.GetFullPath(missingPath), resolution.ReasonUnavailable);
    }

    [Theory]
    [InlineData("libmlxc.dylib")]
    [InlineData("MLX.NativeAOT.dylib")]
    public void Resolver_FindsPreparedDarwinArm64Layout(string fileName)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var nativeDirectory = Path.Combine(root, "artifacts", "mlx", CurrentRuntimeIdentifier(), "native");
            Directory.CreateDirectory(nativeDirectory);
            var libraryPath = Path.Combine(nativeDirectory, fileName);
            File.WriteAllBytes(libraryPath, [0]);

            var resolution = MlxRuntimeResolver.Resolve(new MlxRuntimeOptions { SearchRoot = root });

            Assert.True(resolution.IsAvailable, resolution.ReasonUnavailable);
            Assert.Equal(Path.GetFullPath(libraryPath), resolution.LibraryPath);
            Assert.Equal("prepared runtime", resolution.Source);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryCreate_ReturnsFalseForMissingRuntime()
    {
        var created = MlxFloatBackend.TryCreate(
            out var backend,
            new MlxRuntimeOptions { SearchRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) });

        Assert.False(created);
        Assert.Null(backend);
    }

    [Fact]
    public void Create_ThrowsActionableErrorForMissingRuntime()
    {
        var error = Assert.Throws<MlxException>(() =>
            MlxFloatBackend.Create(new MlxRuntimeOptions { SearchRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) }));

        Assert.Contains("MLX C runtime", error.Message);
        Assert.Contains("prepare-mlx-runtime.cs", error.Message);
    }

    [Fact]
    public void PublicApi_DoesNotExportRawMlxInterop()
    {
        var exportedTypeNames = typeof(MlxFloatBackend).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace?.Contains(".Mlx", StringComparison.Ordinal) == true)
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(nameof(MlxFloatBackend), exportedTypeNames);
        Assert.Contains(nameof(MlxFloatTensor), exportedTypeNames);
        Assert.DoesNotContain("MlxNative", exportedTypeNames);
        Assert.DoesNotContain("MlxArrayHandle", exportedTypeNames);
        Assert.DoesNotContain("MlxDeviceHandle", exportedTypeNames);
        Assert.DoesNotContain("MlxStreamHandle", exportedTypeNames);
    }

    [Fact]
    public void MlxTensor_DoesNotImplementExactAlgebraInterfaces()
    {
        var interfaces = typeof(MlxFloatTensor).GetInterfaces();

        Assert.DoesNotContain(interfaces, IsOpenGeneric(typeof(IRing<>)));
        Assert.DoesNotContain(interfaces, IsOpenGeneric(typeof(ICommRing<>)));
        Assert.DoesNotContain(interfaces, IsOpenGeneric(typeof(IField<>)));
    }

    [Fact]
    public void NativeCpuSmoke_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
        if (backend is null)
            return;

        using var a = backend.CreateMatrix(2, 2, [1, 2, 3, 4]);
        using var b = backend.CreateMatrix(2, 2, [5, 6, 7, 8]);
        using var product = backend.MatMul(a, b);
        using var sum = backend.Add(a, b);
        using var elementwise = backend.Multiply(a, b);
        using var transpose = backend.Transpose(a);

        Assert.Equal([19, 22, 43, 50], product.ToArray());
        Assert.Equal([6, 8, 10, 12], sum.ToArray());
        Assert.Equal([5, 12, 21, 32], elementwise.ToArray());
        Assert.Equal([1, 3, 2, 4], transpose.ToArray());
        Assert.Equal(10, backend.Sum(a));
        Assert.Equal(2.5f, backend.Mean(a));
    }

    [Fact]
    public void NativeCpuLinearAlgebra_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
        if (backend is null)
            return;

        using var matrix = backend.CreateMatrix(2, 2, [4, 7, 2, 6]);
        using var rhs = backend.CreateMatrix(2, 1, [1, 0]);
        using var solution = backend.LinearSolve(matrix, rhs);
        using var inverse = backend.MatrixInverse(matrix);

        AssertClose([0.6f, -0.2f], solution.ToArray());
        AssertClose([0.6f, -0.7f, -0.2f, 0.4f], inverse.ToArray());
    }

    [Fact]
    public void NativeCpuShapeOps_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
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
    public void NativeCpuShapeOps_RejectInvalidShapesBeforeMlx_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
        if (backend is null)
            return;

        using var value = backend.CreateMatrix(2, 3, [1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f]);

        Assert.Throws<ArgumentException>(() => backend.Reshape(value, 4, 2));
        Assert.Throws<ArgumentException>(() => backend.Broadcast(value, 2, 3));
        Assert.Throws<ArgumentException>(() => backend.Slice(value, 1, 2, 2, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => backend.Concatenate(value, value, axis: 2));
    }

    [Fact]
    public void NativeCpuUnaryOps_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
        if (backend is null)
            return;

        using var value = backend.CreateMatrix(2, 2, [1.0f, 4.0f, 9.0f, 16.0f]);
        using var denominator = backend.CreateMatrix(2, 2, [1.0f, 2.0f, 3.0f, 4.0f]);
        using var signed = backend.CreateMatrix(1, 3, [-1.0f, 0.0f, 1.0f]);
        using var divided = backend.Divide(value, denominator);
        using var squared = backend.Square(denominator);
        using var exp = backend.Exp(signed);
        using var log = backend.Log(value);
        using var sqrt = backend.Sqrt(value);
        using var tanh = backend.Tanh(signed);
        using var sigmoid = backend.Sigmoid(signed);

        AssertClose([1.0f, 2.0f, 3.0f, 4.0f], divided.ToArray());
        AssertClose([1.0f, 4.0f, 9.0f, 16.0f], squared.ToArray());
        AssertClose([MathF.Exp(-1.0f), 1.0f, MathF.Exp(1.0f)], exp.ToArray());
        AssertClose([0.0f, MathF.Log(4.0f), MathF.Log(9.0f), MathF.Log(16.0f)], log.ToArray());
        AssertClose([1.0f, 2.0f, 3.0f, 4.0f], sqrt.ToArray());
        AssertClose([MathF.Tanh(-1.0f), 0.0f, MathF.Tanh(1.0f)], tanh.ToArray());
        AssertClose([1.0f / (1.0f + MathF.Exp(1.0f)), 0.5f, 1.0f / (1.0f + MathF.Exp(-1.0f))], sigmoid.ToArray());
    }

    [Fact]
    public void NativeCpuSoftmax_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
        if (backend is null)
            return;

        using var value = backend.CreateMatrix(2, 3, [1.0f, 2.0f, 3.0f, 1.0f, 1.0f, 1.0f]);
        using var rowSoftmax = backend.Softmax(value, axis: 1);
        using var columnSoftmax = backend.Softmax(value, axis: 0);

        var e1 = MathF.Exp(1.0f);
        var e2 = MathF.Exp(2.0f);
        var e3 = MathF.Exp(3.0f);
        var row0Denominator = e1 + e2 + e3;
        AssertClose(
            [e1 / row0Denominator, e2 / row0Denominator, e3 / row0Denominator, 1.0f / 3.0f, 1.0f / 3.0f, 1.0f / 3.0f],
            rowSoftmax.ToArray());

        var col0Denominator = e1 + e1;
        var col1Denominator = e2 + e1;
        var col2Denominator = e3 + e1;
        AssertClose(
            [e1 / col0Denominator, e2 / col1Denominator, e3 / col2Denominator, e1 / col0Denominator, e1 / col1Denominator, e1 / col2Denominator],
            columnSoftmax.ToArray());
    }

    [Fact]
    public void NativeCpuAxisReductionAndBroadcastTo_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
        if (backend is null)
            return;

        using var value = backend.CreateMatrix(2, 3, [1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f]);
        using var sumRows = backend.SumAxis(value, axis: 1);
        using var sumCols = backend.SumAxis(value, axis: 0);
        using var broadcastRows = backend.BroadcastTo(sumRows, 2, 3);
        using var broadcastCols = backend.BroadcastTo(sumCols, 2, 3);

        Assert.Equal(2, sumRows.Rows);
        Assert.Equal(1, sumRows.Cols);
        Assert.Equal(1, sumCols.Rows);
        Assert.Equal(3, sumCols.Cols);
        Assert.Equal([6.0f, 15.0f], sumRows.ToArray());
        Assert.Equal([5.0f, 7.0f, 9.0f], sumCols.ToArray());
        Assert.Equal([6.0f, 6.0f, 6.0f, 15.0f, 15.0f, 15.0f], broadcastRows.ToArray());
        Assert.Equal([5.0f, 7.0f, 9.0f, 5.0f, 7.0f, 9.0f], broadcastCols.ToArray());
    }

    [Fact]
    public void TensorTape_MatMulMean_ReturnsExpectedInputGradients_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
        if (backend is null)
            return;

        using var aValue = backend.CreateMatrix(2, 2, [1.0f, 2.0f, 3.0f, 4.0f]);
        using var bValue = backend.CreateMatrix(2, 2, [5.0f, 6.0f, 7.0f, 8.0f]);
        using var tape = new MlxTensorTape(backend);
        var a = tape.Watch(aValue);
        var b = tape.Watch(bValue);

        var loss = tape.Mean(tape.MatMul(a, b));

        using var gradA = tape.Gradient(loss, a);
        using var gradB = tape.Gradient(loss, b);

        AssertClose([2.75f, 3.75f, 2.75f, 3.75f], gradA.ToArray());
        AssertClose([1.0f, 1.0f, 1.5f, 1.5f], gradB.ToArray());
    }

    [Fact]
    public void TensorTape_ShapeAdjoints_ReturnExpectedInputGradient_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
        if (backend is null)
            return;

        using var xValue = backend.CreateMatrix(2, 3, [1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f]);
        using var tape = new MlxTensorTape(backend);
        var x = tape.Watch(xValue);

        var sliced = tape.Slice(x, 0, 1, 2, 2);
        var reshaped = tape.Reshape(sliced, 1, 4);
        var loss = tape.Sum(reshaped);

        using var gradX = tape.Gradient(loss, x);

        AssertClose([0.0f, 1.0f, 1.0f, 0.0f, 1.0f, 1.0f], gradX.ToArray());
    }

    [Fact]
    public void TensorTape_NonlinearActivations_ReturnExpectedInputGradient_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
        if (backend is null)
            return;

        using var xValue = backend.CreateMatrix(1, 3, [-1.0f, 0.0f, 1.0f]);
        using var tape = new MlxTensorTape(backend);
        var x = tape.Watch(xValue);

        var loss = tape.Sum(tape.Add(tape.Exp(x), tape.Add(tape.Tanh(x), tape.Sigmoid(x))));

        using var gradX = tape.Gradient(loss, x);

        var expected = new float[3];
        var source = xValue.ToArray();
        for (var i = 0; i < source.Length; i++)
        {
            var sigmoid = 1.0f / (1.0f + MathF.Exp(-source[i]));
            var tanh = MathF.Tanh(source[i]);
            expected[i] = MathF.Exp(source[i]) + (1.0f - tanh * tanh) + sigmoid * (1.0f - sigmoid);
        }

        AssertClose(expected, gradX.ToArray());
    }

    [Fact]
    public void TensorTape_DivideLogSqrt_ReturnExpectedInputGradient_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
        if (backend is null)
            return;

        using var xValue = backend.CreateMatrix(1, 3, [1.0f, 4.0f, 9.0f]);
        using var yValue = backend.CreateMatrix(1, 3, [2.0f, 5.0f, 10.0f]);
        using var tape = new MlxTensorTape(backend);
        var x = tape.Watch(xValue);
        var y = tape.Watch(yValue);

        var loss = tape.Sum(tape.Add(tape.Divide(x, y), tape.Add(tape.Log(x), tape.Sqrt(x))));

        using var gradX = tape.Gradient(loss, x);
        using var gradY = tape.Gradient(loss, y);

        var xData = xValue.ToArray();
        var yData = yValue.ToArray();
        var expectedX = new float[3];
        var expectedY = new float[3];
        for (var i = 0; i < xData.Length; i++)
        {
            expectedX[i] = 1.0f / yData[i] + 1.0f / xData[i] + 1.0f / (2.0f * MathF.Sqrt(xData[i]));
            expectedY[i] = -xData[i] / (yData[i] * yData[i]);
        }

        AssertClose(expectedX, gradX.ToArray());
        AssertClose(expectedY, gradY.ToArray());
    }

    [Fact]
    public void TensorTape_TinyMlpSmoke_ReturnsExpectedWeightGradients_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
        if (backend is null)
            return;

        using var inputValue = backend.CreateMatrix(1, 2, [1.0f, 2.0f]);
        using var w1Value = backend.CreateMatrix(2, 2, [0.5f, -1.0f, 1.0f, 0.25f]);
        using var w2Value = backend.CreateMatrix(2, 1, [1.5f, -2.0f]);
        using var tape = new MlxTensorTape(backend);
        var input = tape.Watch(inputValue);
        var w1 = tape.Watch(w1Value);
        var w2 = tape.Watch(w2Value);

        var hidden = tape.Tanh(tape.MatMul(input, w1));
        var prediction = tape.MatMul(hidden, w2);
        var loss = tape.Mean(prediction);

        using var gradW1 = tape.Gradient(loss, w1);
        using var gradW2 = tape.Gradient(loss, w2);

        var z0 = 1.0f * 0.5f + 2.0f * 1.0f;
        var z1 = 1.0f * -1.0f + 2.0f * 0.25f;
        var h0 = MathF.Tanh(z0);
        var h1 = MathF.Tanh(z1);
        var dz0 = 1.5f * (1.0f - h0 * h0);
        var dz1 = -2.0f * (1.0f - h1 * h1);

        AssertClose([1.0f * dz0, 1.0f * dz1, 2.0f * dz0, 2.0f * dz1], gradW1.ToArray(), tolerance: 1e-4f);
        AssertClose([h0, h1], gradW2.ToArray(), tolerance: 1e-4f);
        Assert.True(float.IsFinite(loss.Value.ToArray()[0]));
    }

    [Fact]
    public void TensorTape_Softmax_ReturnsExpectedInputGradient_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
        if (backend is null)
            return;

        using var logitsValue = backend.CreateMatrix(2, 3, [1.0f, 2.0f, 3.0f, 1.0f, 1.0f, 1.0f]);
        using var targetValue = backend.CreateMatrix(2, 3, [0.1f, -0.2f, 0.3f, 1.0f, 0.0f, -1.0f]);
        using var tape = new MlxTensorTape(backend);
        var logits = tape.Watch(logitsValue);
        var target = tape.Watch(targetValue);

        var probabilities = tape.Softmax(logits, axis: 1);
        var loss = tape.Sum(tape.Multiply(probabilities, target));

        using var gradLogits = tape.Gradient(loss, logits);

        var probabilitiesData = probabilities.Value.ToArray();
        var targetData = targetValue.ToArray();
        var expected = new float[6];
        for (var row = 0; row < 2; row++)
        {
            var dot = 0.0f;
            for (var col = 0; col < 3; col++)
                dot += probabilitiesData[row * 3 + col] * targetData[row * 3 + col];
            for (var col = 0; col < 3; col++)
                expected[row * 3 + col] = probabilitiesData[row * 3 + col] * (targetData[row * 3 + col] - dot);
        }

        AssertClose(expected, gradLogits.ToArray(), tolerance: 1e-5f);
    }

    [Fact]
    public void SoftmaxCrossEntropy_ReturnsExpectedLogitGradient_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
        if (backend is null)
            return;

        using var logitsValue = backend.CreateMatrix(2, 3, [1.0f, 2.0f, 3.0f, 2.0f, 1.0f, 0.0f]);
        using var targetsValue = backend.CreateMatrix(2, 3, [0.0f, 0.0f, 1.0f, 1.0f, 0.0f, 0.0f]);
        using var tape = new MlxTensorTape(backend);
        var logits = tape.Watch(logitsValue);
        var targets = tape.Watch(targetsValue);

        var loss = MlxLosses.SoftmaxCrossEntropy(tape, logits, targets);

        using var gradLogits = tape.Gradient(loss, logits);

        using var probabilities = backend.Softmax(logitsValue, axis: 1);
        var expected = probabilities.ToArray();
        var targetData = targetsValue.ToArray();
        for (var i = 0; i < expected.Length; i++)
            expected[i] = (expected[i] - targetData[i]) / 2.0f;

        Assert.True(loss.Value.ToArray()[0] > 0.0f);
        AssertClose(expected, gradLogits.ToArray(), tolerance: 1e-5f);
    }

    [Fact]
    public void TensorTape_Dispose_DisposesIntermediateTensors_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
        if (backend is null)
            return;

        using var xValue = backend.CreateMatrix(1, 1, [2.0f]);
        var tape = new MlxTensorTape(backend);
        var x = tape.Watch(xValue);
        var y = tape.Multiply(x, x);

        tape.Dispose();

        Assert.Throws<ObjectDisposedException>(() => y.Value.ToArray());
        Assert.Throws<ObjectDisposedException>(() => tape.Watch(xValue));
    }

    [Fact]
    public void SgdOptimizer_LinearRegression_DecreasesMse_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
        if (backend is null)
            return;

        using var inputs = backend.CreateMatrix(4, 1, [1.0f, 2.0f, 3.0f, 4.0f]);
        using var targets = backend.CreateMatrix(4, 1, [2.0f, 4.0f, 6.0f, 8.0f]);
        using var weight = backend.CreateMatrix(1, 1, [0.0f]);
        var optimizer = new MlxSgdOptimizer(backend, learningRate: 0.03f);

        var initialLoss = ComputeLinearRegressionStep(backend, optimizer, inputs, targets, weight, update: false);
        for (var step = 0; step < 40; step++)
            _ = ComputeLinearRegressionStep(backend, optimizer, inputs, targets, weight, update: true);
        var finalLoss = ComputeLinearRegressionStep(backend, optimizer, inputs, targets, weight, update: false);

        Assert.True(finalLoss < initialLoss * 0.01f, $"Expected loss to fall sharply. Initial={initialLoss}, Final={finalLoss}");
        Assert.InRange(weight.ToArray()[0], 1.95f, 2.05f);
    }

    [Fact]
    public void AdamOptimizer_LinearRegression_DecreasesMse_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
        if (backend is null)
            return;

        using var inputs = backend.CreateMatrix(4, 1, [1.0f, 2.0f, 3.0f, 4.0f]);
        using var targets = backend.CreateMatrix(4, 1, [2.0f, 4.0f, 6.0f, 8.0f]);
        using var weight = backend.CreateMatrix(1, 1, [0.0f]);
        using var optimizer = new MlxAdamOptimizer(backend, learningRate: 0.1f);

        var initialLoss = ComputeLinearRegressionStep(backend, optimizer, inputs, targets, weight, update: false);
        for (var step = 0; step < 80; step++)
            _ = ComputeLinearRegressionStep(backend, optimizer, inputs, targets, weight, update: true);
        var finalLoss = ComputeLinearRegressionStep(backend, optimizer, inputs, targets, weight, update: false);

        Assert.True(finalLoss < initialLoss * 0.02f, $"Expected loss to fall sharply. Initial={initialLoss}, Final={finalLoss}");
        Assert.InRange(weight.ToArray()[0], 1.85f, 2.15f);
    }

    [Fact]
    public void Losses_BinaryCrossEntropyAndL2_ReturnExpectedGradients_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
        if (backend is null)
            return;

        using var probabilitiesValue = backend.CreateMatrix(1, 2, [0.25f, 0.75f]);
        using var targetsValue = backend.CreateMatrix(1, 2, [0.0f, 1.0f]);
        using var tape = new MlxTensorTape(backend);
        var probabilities = tape.Watch(probabilitiesValue);
        var targets = tape.Watch(targetsValue);
        var loss = tape.Add(
            MlxLosses.BinaryCrossEntropy(tape, probabilities, targets),
            MlxLosses.L2Penalty(tape, probabilities, coefficient: 0.1f));

        using var gradient = tape.Gradient(loss, probabilities);

        var p = probabilitiesValue.ToArray();
        var y = targetsValue.ToArray();
        var expected = new float[p.Length];
        for (var i = 0; i < p.Length; i++)
            expected[i] = ((1.0f - y[i]) / (1.0f - p[i]) - y[i] / p[i]) / p.Length + 0.2f * p[i];

        Assert.True(loss.Value.ToArray()[0] > 0.0f);
        AssertClose(expected, gradient.ToArray(), tolerance: 1e-5f);
    }

    [Fact]
    public void Activations_SiluAndGeluApprox_ReturnFiniteGradients_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
        if (backend is null)
            return;

        using var value = backend.CreateMatrix(1, 3, [-1.0f, 0.0f, 1.0f]);
        using var tape = new MlxTensorTape(backend);
        var x = tape.Watch(value);
        var loss = tape.Sum(tape.Add(MlxActivations.SiLU(tape, x), MlxActivations.GeluApprox(tape, x)));

        using var gradient = tape.Gradient(loss, x);

        Assert.All(gradient.ToArray(), item => Assert.True(float.IsFinite(item)));
    }

    [Fact]
    public void Activations_ReluAndLeakyRelu_ReturnExpectedGradients_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
        if (backend is null)
            return;

        using var value = backend.CreateMatrix(1, 3, [-2.0f, 0.0f, 3.0f]);
        using var tape = new MlxTensorTape(backend);
        var x = tape.Watch(value);
        var loss = tape.Sum(tape.Add(MlxActivations.ReLU(tape, x), MlxActivations.LeakyReLU(tape, x, negativeSlope: 0.1f)));

        using var gradient = tape.Gradient(loss, x);

        AssertClose([0.1f, 2.0f, 2.0f], gradient.ToArray());
    }

    [Fact]
    public void LinearLayerAndTrainStep_DecreaseMse_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
        if (backend is null)
            return;

        using var inputs = backend.CreateMatrix(4, 1, [1.0f, 2.0f, 3.0f, 4.0f]);
        using var targets = backend.CreateMatrix(4, 1, [3.0f, 6.0f, 9.0f, 12.0f]);
        using var layer = new MlxLinearLayer(backend, 1, 1, [0.0f], [0.0f], "regressor");
        var parameters = layer.Parameters.ToArray();
        var optimizer = new MlxSgdOptimizer(backend, learningRate: 0.02f);

        var initialLoss = ComputeLayerLoss(backend, layer, inputs, targets);
        for (var i = 0; i < 80; i++)
        {
            _ = MlxTrainStep.Run(
                backend,
                parameters,
                optimizer,
                (tape, watched) =>
                {
                    var x = tape.Watch(inputs);
                    var y = tape.Watch(targets);
                    return MlxLosses.MeanSquaredError(tape, layer.Forward(tape, x, watched), y);
                });
        }
        var finalLoss = ComputeLayerLoss(backend, layer, inputs, targets);

        Assert.True(finalLoss < initialLoss * 0.05f, $"Expected layer training to reduce loss. Initial={initialLoss}, Final={finalLoss}");
        Assert.InRange(layer.Weight.Value.ToArray()[0], 2.8f, 3.2f);
    }

    [Fact]
    public void Sequential_Forward_ComposesLayers_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
        if (backend is null)
            return;

        using var input = backend.CreateMatrix(1, 1, [2.0f]);
        using var model = new MlxSequential(
            new MlxLinearLayer(backend, 1, 1, [3.0f], [1.0f], "a"),
            new MlxLinearLayer(backend, 1, 1, [2.0f], [-4.0f], "b"));
        using var tape = new MlxTensorTape(backend);
        var output = model.Forward(tape, tape.Watch(input));

        Assert.Equal([10.0f], output.Value.ToArray());
    }

    [Fact]
    public void RepeatedTrainSteps_DoNotGrowLiveTensorCount_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
        if (backend is null)
            return;

        using var inputs = backend.CreateMatrix(4, 1, [1.0f, 2.0f, 3.0f, 4.0f]);
        using var targets = backend.CreateMatrix(4, 1, [2.0f, 4.0f, 6.0f, 8.0f]);
        using var weight = backend.CreateMatrix(1, 1, [0.0f]);
        var optimizer = new MlxSgdOptimizer(backend, learningRate: 0.03f);
        var baseline = backend.LiveTensorCount;

        for (var step = 0; step < 100; step++)
            _ = ComputeLinearRegressionStep(backend, optimizer, inputs, targets, weight, update: true);

        Assert.Equal(baseline, backend.LiveTensorCount);
    }

    [Fact]
    public void BackendNeutralTrainStep_DecreasesMseOnMlx_WhenRuntimeAvailable()
    {
        using var backend = CreateNativeCpuBackendOrNull();
        if (backend is null)
            return;

        var trainableBackend = new MlxTrainableBackend(backend);
        using var inputs = backend.CreateMatrix(4, 1, [1.0f, 2.0f, 3.0f, 4.0f]);
        using var targets = backend.CreateMatrix(4, 1, [2.0f, 4.0f, 6.0f, 8.0f]);
        using var weight = new TrainableParameter<MlxFloatTensor>("weight", backend.CreateMatrix(1, 1, [0.0f]));
        var parameters = new[] { weight };
        var optimizer = new TrainableSgdOptimizer<MlxFloatTensor, MlxFloatTensorVar, MlxTensorTape>(trainableBackend, learningRate: 0.03f);

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

    private static Predicate<Type> IsOpenGeneric(Type openGeneric)
        => type => type.IsGenericType && type.GetGenericTypeDefinition() == openGeneric;

    private static float ComputeLayerLoss(MlxFloatBackend backend, MlxLinearLayer layer, MlxFloatTensor inputs, MlxFloatTensor targets)
    {
        using var tape = new MlxTensorTape(backend);
        var x = tape.Watch(inputs);
        var y = tape.Watch(targets);
        var prediction = layer.Forward(tape, x);
        return MlxLosses.MeanSquaredError(tape, prediction, y).Value.ToArray()[0];
    }

    private static float ComputeLinearRegressionStep(
        MlxFloatBackend backend,
        MlxSgdOptimizer optimizer,
        MlxFloatTensor inputs,
        MlxFloatTensor targets,
        MlxFloatTensor weight,
        bool update)
    {
        using var tape = new MlxTensorTape(backend);
        var x = tape.Watch(inputs);
        var y = tape.Watch(targets);
        var w = tape.Watch(weight);
        var prediction = tape.MatMul(x, w);
        var loss = MlxLosses.MeanSquaredError(tape, prediction, y);
        var lossValue = loss.Value.ToArray()[0];

        if (update)
        {
            using var gradient = tape.Gradient(loss, w);
            optimizer.Step(weight, gradient);
        }

        return lossValue;
    }

    private static float ComputeLinearRegressionStep(
        MlxFloatBackend backend,
        MlxAdamOptimizer optimizer,
        MlxFloatTensor inputs,
        MlxFloatTensor targets,
        MlxFloatTensor weight,
        bool update)
    {
        using var tape = new MlxTensorTape(backend);
        var x = tape.Watch(inputs);
        var y = tape.Watch(targets);
        var w = tape.Watch(weight);
        var prediction = tape.MatMul(x, w);
        var loss = MlxLosses.MeanSquaredError(tape, prediction, y);
        var lossValue = loss.Value.ToArray()[0];

        if (update)
        {
            using var gradient = tape.Gradient(loss, w);
            optimizer.Step(weight, gradient);
        }

        return lossValue;
    }

    private static float ComputeBackendNeutralLinearLoss(
        MlxTrainableBackend backend,
        MlxFloatTensor inputs,
        MlxFloatTensor targets,
        MlxFloatTensor weight)
    {
        using var tape = backend.CreateTape();
        var prediction = backend.MatMul(tape, backend.Watch(tape, inputs), backend.Watch(tape, weight));
        var loss = TrainableLosses.MeanSquaredError(backend, tape, prediction, backend.Watch(tape, targets));
        return backend.ReadScalar(backend.Value(loss));
    }

    private static MlxFloatBackend? CreateNativeCpuBackendOrNull()
    {
        var options = new MlxRuntimeOptions
        {
            SearchRoot = FindRepoRoot(),
            Device = MlxDeviceKind.Cpu,
            AllowCpuFallback = false
        };

        return MlxFloatBackend.TryCreate(out var backend, options) ? backend : null;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "artifacts", "mlx")) ||
                File.Exists(Path.Combine(directory.FullName, "HPD-AI-Framework", "dotnet", "shared", "src", "Helium", "Helium.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }

    private static void AssertClose(float[] expected, float[] actual, float tolerance = 1e-5f)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
            Assert.InRange(MathF.Abs(actual[i] - expected[i]), 0, tolerance);
    }

    private static string CurrentRuntimeIdentifier()
    {
        var os = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsMacOS()
                ? "osx"
                : "linux";

        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            _ => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
        };

        return $"{os}-{arch}";
    }
}
