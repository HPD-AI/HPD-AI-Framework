namespace HPD.ML.Backends.Pjrt;

public sealed class PjrtFloatBackend : Helium.Hardware.IExecutionBackend<float>, IDisposable
{
    private readonly PjrtPlugin _plugin;
    private readonly PjrtClient _client;
    private readonly PjrtExecutableCache<OperationKey> _executableCache;
    private readonly HashSet<PjrtFloatTensor> _liveTensors = [];
    private bool _disposed;

    private PjrtFloatBackend(PjrtPlugin plugin, PjrtClient client)
    {
        _plugin = plugin;
        _client = client;
        _executableCache = new PjrtExecutableCache<OperationKey>(_client, StableHlo);
    }

    public int CachedExecutableCount
    {
        get
        {
            ThrowIfDisposed();
            return _executableCache.Count;
        }
    }

    public PjrtPluginInfo PluginInfo
    {
        get
        {
            ThrowIfDisposed();
            return _plugin.GetInfo();
        }
    }

    public PjrtClientInfo ClientInfo
    {
        get
        {
            ThrowIfDisposed();
            return _client.GetInfo();
        }
    }

    internal int LiveTensorCount => _liveTensors.Count;

    public static bool IsAvailable(PjrtPluginResolverOptions? options = null)
        => PjrtPluginResolver.Resolve(options ?? new PjrtPluginResolverOptions { Backend = "cpu" }).IsAvailable;

    public static bool TryCreate(out PjrtFloatBackend? backend, PjrtPluginResolverOptions? options = null)
    {
        var resolution = PjrtPluginResolver.Resolve(options ?? new PjrtPluginResolverOptions { Backend = "cpu" });
        if (!resolution.IsAvailable || resolution.LibraryPath is null)
        {
            backend = null;
            return false;
        }

        backend = Create((options ?? new PjrtPluginResolverOptions { Backend = "cpu" }) with { ExplicitPath = resolution.LibraryPath });
        return true;
    }

    public static PjrtFloatBackend Create(PjrtPluginResolverOptions? options = null)
    {
        var resolution = PjrtPluginResolver.Resolve(options ?? new PjrtPluginResolverOptions { Backend = "cpu" });
        if (!resolution.IsAvailable || resolution.LibraryPath is null)
            throw new PjrtException(resolution.ReasonUnavailable ?? "PJRT plugin is unavailable.");

        PjrtGpuRuntimeLoader.PreloadForBackend((options ?? new PjrtPluginResolverOptions { Backend = "cpu" }).Backend);
        var plugin = PjrtPlugin.Load(resolution.LibraryPath);
        try
        {
            var client = PjrtClient.Create(plugin, PjrtClientCreateOptionDefaults.WithBackendDefaults(options ?? new PjrtPluginResolverOptions { Backend = "cpu" }).ClientOptions);
            return new PjrtFloatBackend(plugin, client);
        }
        catch
        {
            plugin.Dispose();
            throw;
        }
    }

    public PjrtFloatTensor CreateMatrix(int rows, int cols, ReadOnlySpan<float> data = default)
    {
        ThrowIfDisposed();
        PjrtBackendCore.ValidateMatrixShape(rows, cols);

        var expectedLength = checked(rows * cols);
        if (!data.IsEmpty && data.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Data length must be {expectedLength} for a {rows}x{cols} matrix.",
                nameof(data));
        }

        var buffer = data.IsEmpty
            ? _client.BufferFromHost(new float[expectedLength], [rows, cols])
            : _client.BufferFromHost(data, [rows, cols]);
        return new PjrtFloatTensor(this, buffer, rows, cols, PjrtElementType.Float32);
    }

    Helium.Hardware.IHardwareTensor<float> Helium.Hardware.IExecutionBackend<float>.CreateMatrix(
        int rows,
        int cols,
        ReadOnlySpan<float> initialData)
        => CreateMatrix(rows, cols, initialData);

    public PjrtFloatTensor MatMul(PjrtFloatTensor a, PjrtFloatTensor b)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (!ReferenceEquals(a.Owner, this) || !ReferenceEquals(b.Owner, this))
            throw new ArgumentException("Both tensors must be owned by this backend.");
        if (a.ElementType != PjrtElementType.Float32 || b.ElementType != PjrtElementType.Float32)
            throw new NotSupportedException("Only Float32 matmul is supported.");
        if (a.Cols != b.Rows)
            throw new ArgumentException($"Cannot multiply {a.Rows}x{a.Cols} by {b.Rows}x{b.Cols}.");

        var key = OperationKey.MatMul(a.Rows, a.Cols, b.Cols);
        var executable = GetOrCompile(key);
        var result = executable.Execute(a.Buffer, b.Buffer);
        return new PjrtFloatTensor(this, result, a.Rows, b.Cols, PjrtElementType.Float32);
    }

    Helium.Hardware.IHardwareTensor<float> Helium.Hardware.IExecutionBackend<float>.MatMul(
        Helium.Hardware.IHardwareTensor<float> left,
        Helium.Hardware.IHardwareTensor<float> right)
        => MatMul(RequirePjrtFloatTensor(left, nameof(left)), RequirePjrtFloatTensor(right, nameof(right)));

    public PjrtFloatTensor Add(PjrtFloatTensor a, PjrtFloatTensor b)
    {
        ThrowIfDisposed();
        ValidateSameShapeBinary(a, b, nameof(Add));

        var key = OperationKey.Binary(OperationKind.Add, a.Rows, a.Cols);
        var executable = GetOrCompile(key);
        var result = executable.Execute(a.Buffer, b.Buffer);
        return new PjrtFloatTensor(this, result, a.Rows, a.Cols, PjrtElementType.Float32);
    }

    Helium.Hardware.IHardwareTensor<float> Helium.Hardware.IExecutionBackend<float>.Add(
        Helium.Hardware.IHardwareTensor<float> left,
        Helium.Hardware.IHardwareTensor<float> right)
        => Add(RequirePjrtFloatTensor(left, nameof(left)), RequirePjrtFloatTensor(right, nameof(right)));

    public PjrtFloatTensor Multiply(PjrtFloatTensor a, PjrtFloatTensor b)
    {
        ThrowIfDisposed();
        ValidateSameShapeBinary(a, b, nameof(Multiply));

        var key = OperationKey.Binary(OperationKind.Multiply, a.Rows, a.Cols);
        var executable = GetOrCompile(key);
        var result = executable.Execute(a.Buffer, b.Buffer);
        return new PjrtFloatTensor(this, result, a.Rows, a.Cols, PjrtElementType.Float32);
    }

    Helium.Hardware.IHardwareTensor<float> Helium.Hardware.IExecutionBackend<float>.Multiply(
        Helium.Hardware.IHardwareTensor<float> left,
        Helium.Hardware.IHardwareTensor<float> right)
        => Multiply(RequirePjrtFloatTensor(left, nameof(left)), RequirePjrtFloatTensor(right, nameof(right)));

    public PjrtFloatTensor Subtract(PjrtFloatTensor a, PjrtFloatTensor b)
    {
        ThrowIfDisposed();
        ValidateSameShapeBinary(a, b, nameof(Subtract));

        var key = OperationKey.Binary(OperationKind.Subtract, a.Rows, a.Cols);
        var executable = GetOrCompile(key);
        var result = executable.Execute(a.Buffer, b.Buffer);
        return new PjrtFloatTensor(this, result, a.Rows, a.Cols, PjrtElementType.Float32);
    }

    Helium.Hardware.IHardwareTensor<float> Helium.Hardware.IExecutionBackend<float>.Subtract(
        Helium.Hardware.IHardwareTensor<float> left,
        Helium.Hardware.IHardwareTensor<float> right)
        => Subtract(RequirePjrtFloatTensor(left, nameof(left)), RequirePjrtFloatTensor(right, nameof(right)));

    public PjrtFloatTensor Negate(PjrtFloatTensor value)
    {
        ThrowIfDisposed();
        ValidateUnary(value, nameof(Negate));

        var key = OperationKey.Unary(OperationKind.Negate, value.Rows, value.Cols);
        var executable = GetOrCompile(key);
        var result = executable.Execute(value.Buffer);
        return new PjrtFloatTensor(this, result, value.Rows, value.Cols, PjrtElementType.Float32);
    }

    Helium.Hardware.IHardwareTensor<float> Helium.Hardware.IExecutionBackend<float>.Negate(
        Helium.Hardware.IHardwareTensor<float> value)
        => Negate(RequirePjrtFloatTensor(value, nameof(value)));

    public PjrtFloatTensor Sum(PjrtFloatTensor value)
    {
        ThrowIfDisposed();
        ValidateUnary(value, nameof(Sum));

        var key = OperationKey.Unary(OperationKind.Sum, value.Rows, value.Cols);
        var executable = GetOrCompile(key);
        var result = executable.Execute(value.Buffer);
        return new PjrtFloatTensor(this, result, 1, 1, PjrtElementType.Float32);
    }

    float Helium.Hardware.IExecutionBackend<float>.Sum(Helium.Hardware.IHardwareTensor<float> value)
    {
        using var result = Sum(RequirePjrtFloatTensor(value, nameof(value)));
        return result.ToArray()[0];
    }

    public PjrtFloatTensor Mean(PjrtFloatTensor value)
    {
        ThrowIfDisposed();
        ValidateUnary(value, nameof(Mean));

        var key = OperationKey.Unary(OperationKind.Mean, value.Rows, value.Cols);
        var executable = GetOrCompile(key);
        var result = executable.Execute(value.Buffer);
        return new PjrtFloatTensor(this, result, 1, 1, PjrtElementType.Float32);
    }

    public PjrtFloatTensor Transpose(PjrtFloatTensor value)
    {
        ThrowIfDisposed();
        ValidateUnary(value, nameof(Transpose));

        var key = OperationKey.Unary(OperationKind.Transpose, value.Rows, value.Cols);
        var executable = GetOrCompile(key);
        var result = executable.Execute(value.Buffer);
        return new PjrtFloatTensor(this, result, value.Cols, value.Rows, PjrtElementType.Float32);
    }

    public PjrtFloatTensor Norm(PjrtFloatTensor value)
    {
        ThrowIfDisposed();
        ValidateUnary(value, nameof(Norm));

        var key = OperationKey.Unary(OperationKind.Norm, value.Rows, value.Cols);
        var executable = GetOrCompile(key);
        var result = executable.Execute(value.Buffer);
        return new PjrtFloatTensor(this, result, 1, 1, PjrtElementType.Float32);
    }

    public PjrtFloatTensor Scale(PjrtFloatTensor value, float scalar)
    {
        ThrowIfDisposed();
        ValidateUnary(value, nameof(Scale));

        var key = OperationKey.Scale(value.Rows, value.Cols, scalar);
        var executable = GetOrCompile(key);
        var result = executable.Execute(value.Buffer);
        return new PjrtFloatTensor(this, result, value.Rows, value.Cols, PjrtElementType.Float32);
    }

    public PjrtFloatTensor ReLU(PjrtFloatTensor value)
    {
        ThrowIfDisposed();
        ValidateUnary(value, nameof(ReLU));

        var key = OperationKey.Unary(OperationKind.ReLU, value.Rows, value.Cols);
        var executable = GetOrCompile(key);
        var result = executable.Execute(value.Buffer);
        return new PjrtFloatTensor(this, result, value.Rows, value.Cols, PjrtElementType.Float32);
    }

    public PjrtFloatTensor Reshape(PjrtFloatTensor value, int rows, int cols)
    {
        ThrowIfDisposed();
        ValidateUnary(value, nameof(Reshape));
        PjrtBackendCore.ValidateMatrixShape(rows, cols);
        if (checked(rows * cols) != value.Rows * value.Cols)
            throw new ArgumentException("Target shape must preserve element count.");

        using var executable = _client.CompileMlir(StableHloProgram.Reshape(value.Rows, value.Cols, rows, cols));
        var result = executable.Execute(value.Buffer);
        return new PjrtFloatTensor(this, result, rows, cols, PjrtElementType.Float32);
    }

    public PjrtFloatTensor Broadcast(PjrtFloatTensor scalar, int rows, int cols)
    {
        ThrowIfDisposed();
        ValidateUnary(scalar, nameof(Broadcast));
        PjrtBackendCore.ValidateMatrixShape(rows, cols);
        if (scalar.Rows != 1 || scalar.Cols != 1)
            throw new ArgumentException("Only 1x1 scalar broadcast is supported.", nameof(scalar));

        using var executable = _client.CompileMlir(StableHloProgram.BroadcastScalar(rows, cols));
        var result = executable.Execute(scalar.Buffer);
        return new PjrtFloatTensor(this, result, rows, cols, PjrtElementType.Float32);
    }

    public PjrtFloatTensor BroadcastTo(PjrtFloatTensor value, int rows, int cols)
    {
        ThrowIfDisposed();
        ValidateUnary(value, nameof(BroadcastTo));
        PjrtBackendCore.ValidateMatrixShape(rows, cols);

        if (value.Rows == rows && value.Cols == cols)
            return CreateMatrix(rows, cols, value.ToArray());

        if (value.Rows == 1 && value.Cols == 1)
            return Broadcast(value, rows, cols);

        var source = value.ToArray();
        var target = new float[checked(rows * cols)];
        if (value.Rows == 1 && value.Cols == cols)
        {
            for (var row = 0; row < rows; row++)
                source.AsSpan().CopyTo(target.AsSpan(row * cols, cols));
            return CreateMatrix(rows, cols, target);
        }

        if (value.Cols == 1 && value.Rows == rows)
        {
            for (var row = 0; row < rows; row++)
            {
                var item = source[row];
                for (var col = 0; col < cols; col++)
                    target[row * cols + col] = item;
            }

            return CreateMatrix(rows, cols, target);
        }

        throw new ArgumentException($"Cannot broadcast {value.Rows}x{value.Cols} tensor to {rows}x{cols}.", nameof(value));
    }

    public PjrtFloatTensor Slice(PjrtFloatTensor value, int startRow, int startCol, int rowCount, int colCount)
    {
        ThrowIfDisposed();
        ValidateUnary(value, nameof(Slice));
        PjrtBackendCore.ValidateSlice(value.Rows, value.Cols, startRow, startCol, rowCount, colCount);

        using var executable = _client.CompileMlir(StableHloProgram.Slice(value.Rows, value.Cols, startRow, startCol, rowCount, colCount));
        var result = executable.Execute(value.Buffer);
        return new PjrtFloatTensor(this, result, rowCount, colCount, PjrtElementType.Float32);
    }

    public PjrtFloatTensor Concatenate(PjrtFloatTensor a, PjrtFloatTensor b, int axis)
    {
        ThrowIfDisposed();
        ValidateSameShapeBinary(a, b, nameof(Concatenate));
        if (axis is not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(axis), "Axis must be 0 or 1.");

        using var executable = _client.CompileMlir(StableHloProgram.Concatenate(a.Rows, a.Cols, axis));
        var result = executable.Execute(a.Buffer, b.Buffer);
        return axis == 0
            ? new PjrtFloatTensor(this, result, a.Rows + b.Rows, a.Cols, PjrtElementType.Float32)
            : new PjrtFloatTensor(this, result, a.Rows, a.Cols + b.Cols, PjrtElementType.Float32);
    }

    float Helium.Hardware.IExecutionBackend<float>.Mean(Helium.Hardware.IHardwareTensor<float> value)
    {
        using var result = Mean(RequirePjrtFloatTensor(value, nameof(value)));
        return result.ToArray()[0];
    }

    public PjrtFloatTensor LinearSolve(PjrtFloatTensor matrix, PjrtFloatTensor rightHandSide)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(rightHandSide);
        if (!ReferenceEquals(matrix.Owner, this) || !ReferenceEquals(rightHandSide.Owner, this))
            throw new ArgumentException("Both tensors must be owned by this backend.");

        ValidateLinearSolve(matrix, rightHandSide, allowLarge: true);
        var matrixData = matrix.ToArray();
        if (matrix.Rows > PjrtBackendCore.MaxGeneratedSolveSize &&
            PjrtBackendCore.TryDetectTriangular(matrixData, matrix.Rows, 1e-6f, out var lower))
        {
            return TriangularSolve(matrix, rightHandSide, lower);
        }

        if (matrix.Rows > PjrtBackendCore.MaxGeneratedSolveSize &&
            PjrtBackendCore.IsSymmetricPositiveDefinite(matrixData, matrix.Rows, 1e-5f))
        {
            var choleskyKey = OperationKey.CholeskySolve(matrix.Rows, rightHandSide.Cols);
            var choleskyExecutable = GetOrCompile(choleskyKey);
            return new PjrtFloatTensor(this, choleskyExecutable.Execute(matrix.Buffer, rightHandSide.Buffer), rightHandSide.Rows, rightHandSide.Cols, PjrtElementType.Float32);
        }

        PjrtBackendCore.ValidateGeneratedSolveSize(matrix.Rows, "linear solve");
        var pivotPlan = PjrtBackendCore.PlanPivoting(matrixData, matrix.Rows, 1e-6f);

        var key = OperationKey.LinearSolve(matrix.Rows, rightHandSide.Cols);
        var executable = GetOrCompile(key);
        PjrtBuffer result;
        if (pivotPlan.RequiresPermutation)
        {
            var rhsData = rightHandSide.ToArray();
            var permutedMatrixData = PjrtBackendCore.ApplyRowPermutation(matrixData, matrix.Rows, matrix.Cols, pivotPlan.RowPermutation);
            var permutedRhsData = PjrtBackendCore.ApplyRowPermutation(rhsData, rightHandSide.Rows, rightHandSide.Cols, pivotPlan.RowPermutation);
            using var permutedMatrix = CreateMatrix(matrix.Rows, matrix.Cols, permutedMatrixData);
            using var permutedRightHandSide = CreateMatrix(rightHandSide.Rows, rightHandSide.Cols, permutedRhsData);
            result = executable.Execute(permutedMatrix.Buffer, permutedRightHandSide.Buffer);
        }
        else
        {
            result = executable.Execute(matrix.Buffer, rightHandSide.Buffer);
        }

        return new PjrtFloatTensor(this, result, rightHandSide.Rows, rightHandSide.Cols, PjrtElementType.Float32);
    }

    public PjrtFloatTensor TriangularSolve(PjrtFloatTensor matrix, PjrtFloatTensor rightHandSide, bool lower, bool unitDiagonal = false)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(rightHandSide);
        if (!ReferenceEquals(matrix.Owner, this) || !ReferenceEquals(rightHandSide.Owner, this))
            throw new ArgumentException("Both tensors must be owned by this backend.");
        ValidateLinearSolve(matrix, rightHandSide, allowLarge: true);

        var key = OperationKey.TriangularSolve(matrix.Rows, rightHandSide.Cols, lower, unitDiagonal);
        var executable = GetOrCompile(key);
        return new PjrtFloatTensor(this, executable.Execute(matrix.Buffer, rightHandSide.Buffer), rightHandSide.Rows, rightHandSide.Cols, PjrtElementType.Float32);
    }

    Helium.Hardware.IHardwareTensor<float> Helium.Hardware.IExecutionBackend<float>.LinearSolve(
        Helium.Hardware.IHardwareTensor<float> matrix,
        Helium.Hardware.IHardwareTensor<float> rightHandSide)
        => LinearSolve(RequirePjrtFloatTensor(matrix, nameof(matrix)), RequirePjrtFloatTensor(rightHandSide, nameof(rightHandSide)));

    public PjrtFloatTensor MatrixInverse(PjrtFloatTensor value)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(value);
        if (!ReferenceEquals(value.Owner, this))
            throw new ArgumentException("Tensor must be owned by this backend.", nameof(value));

        ValidateMatrixInverse(value);

        using var identity = CreateMatrix(value.Rows, value.Cols, PjrtBackendCore.IdentityFloat(value.Rows));
        return LinearSolve(value, identity);
    }

    Helium.Hardware.IHardwareTensor<float> Helium.Hardware.IExecutionBackend<float>.MatrixInverse(
        Helium.Hardware.IHardwareTensor<float> value)
        => MatrixInverse(RequirePjrtFloatTensor(value, nameof(value)));

    Helium.Hardware.IHardwareTensor<float> Helium.Hardware.IExecutionBackend<float>.Transpose(
        Helium.Hardware.IHardwareTensor<float> value)
        => Transpose(RequirePjrtFloatTensor(value, nameof(value)));


    Helium.Hardware.IHardwareTensor<float> Helium.Hardware.IExecutionBackend<float>.Scale(
        Helium.Hardware.IHardwareTensor<float> value,
        float scalar)
        => Scale(RequirePjrtFloatTensor(value, nameof(value)), scalar);

    float Helium.Hardware.IExecutionBackend<float>.Dot(
        Helium.Hardware.IHardwareTensor<float> left,
        Helium.Hardware.IHardwareTensor<float> right)
    {
        var a = RequirePjrtFloatTensor(left, nameof(left));
        var b = RequirePjrtFloatTensor(right, nameof(right));
        if (a.Rows * a.Cols != b.Rows * b.Cols)
            throw new ArgumentException("Tensor lengths must match.");

        using var product = Multiply(a, b);
        using var result = Sum(product);
        return result.ToArray()[0];
    }

    float Helium.Hardware.IExecutionBackend<float>.Norm(Helium.Hardware.IHardwareTensor<float> value)
    {
        using var result = Norm(RequirePjrtFloatTensor(value, nameof(value)));
        return result.ToArray()[0];
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (var tensor in _liveTensors.ToArray())
            tensor.DisposeFromOwner();

        _liveTensors.Clear();

        _executableCache.Dispose();
        _client.Dispose();
        _plugin.Dispose();
    }

    private PjrtLoadedExecutable GetOrCompile(OperationKey key) => _executableCache.GetOrCompile(key);

    internal PjrtBuffer BufferFromHost(ReadOnlySpan<float> data, int rows, int cols)
    {
        ThrowIfDisposed();
        return _client.BufferFromHost(data, [rows, cols]);
    }

    internal void RegisterTensor(PjrtFloatTensor tensor)
    {
        ThrowIfDisposed();
        _liveTensors.Add(tensor);
    }

    internal void UnregisterTensor(PjrtFloatTensor tensor)
    {
        if (_disposed)
            return;

        _liveTensors.Remove(tensor);
    }

    private static string StableHlo(OperationKey key)
    {
        return key.Kind switch
        {
            OperationKind.MatMul => StableHloProgram.MatMul(key.LeftRows, key.SharedDim, key.RightCols),
            OperationKind.Add => StableHloProgram.Binary(key.LeftRows, key.SharedDim, "stablehlo.add"),
            OperationKind.Multiply => StableHloProgram.Binary(key.LeftRows, key.SharedDim, "stablehlo.multiply"),
            OperationKind.Subtract => StableHloProgram.Binary(key.LeftRows, key.SharedDim, "stablehlo.subtract"),
            OperationKind.Negate => StableHloProgram.Unary(key.LeftRows, key.SharedDim, "stablehlo.negate"),
            OperationKind.Sum => StableHloProgram.Sum(key.LeftRows, key.SharedDim),
            OperationKind.Mean => StableHloProgram.Mean(key.LeftRows, key.SharedDim),
            OperationKind.Transpose => StableHloProgram.Transpose(key.LeftRows, key.SharedDim),
            OperationKind.Scale => StableHloProgram.Scale(key.LeftRows, key.SharedDim, key.Scalar),
            OperationKind.ReLU => StableHloProgram.ReLU(key.LeftRows, key.SharedDim),
            OperationKind.Norm => StableHloProgram.Norm(key.LeftRows, key.SharedDim),
            OperationKind.LinearSolve => StableHloProgram.LinearSolve(key.LeftRows, key.RightCols),
            OperationKind.TriangularSolve => StableHloProgram.TriangularSolve(key.LeftRows, key.RightCols, key.Lower, key.UnitDiagonal),
            OperationKind.CholeskySolve => StableHloProgram.CholeskySolve(key.LeftRows, key.RightCols),
            _ => throw new NotSupportedException($"Unsupported XLA operation: {key.Kind}")
        };
    }

    private void ValidateUnary(PjrtFloatTensor value, string operationName)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!ReferenceEquals(value.Owner, this))
            throw new ArgumentException("Tensor must be owned by this backend.", nameof(value));
        if (value.ElementType != PjrtElementType.Float32)
            throw new NotSupportedException($"Only Float32 {operationName} is supported.");
    }

    private void ValidateSameShapeBinary(PjrtFloatTensor a, PjrtFloatTensor b, string operationName)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (!ReferenceEquals(a.Owner, this) || !ReferenceEquals(b.Owner, this))
            throw new ArgumentException("Both tensors must be owned by this backend.");
        if (a.ElementType != PjrtElementType.Float32 || b.ElementType != PjrtElementType.Float32)
            throw new NotSupportedException($"Only Float32 {operationName} is supported.");
        if (a.Rows != b.Rows || a.Cols != b.Cols)
        {
            throw new ArgumentException(
                $"Cannot {operationName} tensors with shapes {a.Rows}x{a.Cols} and {b.Rows}x{b.Cols}.");
        }
    }

    private static void ValidateLinearSolve(PjrtFloatTensor matrix, PjrtFloatTensor rightHandSide, bool allowLarge = false)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(rightHandSide);

        if (!ReferenceEquals(matrix.Owner, rightHandSide.Owner))
            throw new ArgumentException("Both tensors must be owned by the same backend.");
        if (matrix.ElementType != PjrtElementType.Float32 || rightHandSide.ElementType != PjrtElementType.Float32)
            throw new NotSupportedException("Only Float32 linear solve is supported.");
        if (matrix.Rows != matrix.Cols)
            throw new ArgumentException("Coefficient matrix must be square.", nameof(matrix));
        if (rightHandSide.Rows != matrix.Rows)
            throw new ArgumentException("Right-hand side row count must match matrix dimension.", nameof(rightHandSide));
        if (!allowLarge)
            PjrtBackendCore.ValidateGeneratedSolveSize(matrix.Rows, "linear solve");
    }

    private static void ValidateMatrixInverse(PjrtFloatTensor value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.ElementType != PjrtElementType.Float32)
            throw new NotSupportedException("Only Float32 matrix inverse is supported.");
        if (value.Rows != value.Cols)
            throw new ArgumentException("Only square matrices can be inverted.", nameof(value));
        PjrtBackendCore.ValidateGeneratedSolveSize(value.Rows, "matrix inverse");
    }

    private PjrtFloatTensor RequirePjrtFloatTensor(Helium.Hardware.IHardwareTensor<float> tensor, string parameterName)
    {
        ThrowIfDisposed();
        if (tensor is not PjrtFloatTensor xlaTensor || !ReferenceEquals(xlaTensor.Owner, this))
            throw new ArgumentException("Tensor must be owned by this XLA backend.", parameterName);

        return xlaTensor;
    }

    internal void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PjrtFloatBackend));
    }

    private readonly record struct OperationKey(
        OperationKind Kind,
        int LeftRows,
        int SharedDim,
        int RightCols,
        float Scalar = 0.0f,
        bool Lower = false,
        bool UnitDiagonal = false)
    {
        public static OperationKey MatMul(int leftRows, int sharedDim, int rightCols)
            => new(OperationKind.MatMul, leftRows, sharedDim, rightCols);

        public static OperationKey Binary(OperationKind kind, int rows, int cols)
            => new(kind, rows, cols, cols);

        public static OperationKey Unary(OperationKind kind, int rows, int cols)
            => new(kind, rows, cols, cols);

        public static OperationKey Scale(int rows, int cols, float scalar)
            => new(OperationKind.Scale, rows, cols, cols, scalar);

        public static OperationKey LinearSolve(int n, int rhsCols)
            => new(OperationKind.LinearSolve, n, n, rhsCols);

        public static OperationKey TriangularSolve(int n, int rhsCols, bool lower, bool unitDiagonal)
            => new(OperationKind.TriangularSolve, n, n, rhsCols, Lower: lower, UnitDiagonal: unitDiagonal);

        public static OperationKey CholeskySolve(int n, int rhsCols)
            => new(OperationKind.CholeskySolve, n, n, rhsCols);
    }

    private enum OperationKind
    {
        MatMul,
        Add,
        Multiply,
        Subtract,
        Negate,
        Sum,
        Mean,
        Transpose,
        Scale,
        ReLU,
        Norm,
        LinearSolve,
        TriangularSolve,
        CholeskySolve
    }
}
