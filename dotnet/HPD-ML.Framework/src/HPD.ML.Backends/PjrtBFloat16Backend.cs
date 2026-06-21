namespace HPD.ML.Backends.Pjrt;

public sealed class PjrtBFloat16Backend : Helium.Hardware.IExecutionBackend<BFloat16>, IDisposable
{
    private readonly PjrtPlugin _plugin;
    private readonly PjrtClient _client;
    private readonly PjrtExecutableCache<OperationKey> _executableCache;
    private readonly HashSet<PjrtBFloat16Tensor> _liveTensors = [];
    private bool _disposed;

    private PjrtBFloat16Backend(PjrtPlugin plugin, PjrtClient client)
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

    public static bool TryCreate(out PjrtBFloat16Backend? backend, PjrtPluginResolverOptions? options = null)
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

    public static PjrtBFloat16Backend Create(PjrtPluginResolverOptions? options = null)
    {
        var resolution = PjrtPluginResolver.Resolve(options ?? new PjrtPluginResolverOptions { Backend = "cpu" });
        if (!resolution.IsAvailable || resolution.LibraryPath is null)
            throw new PjrtException(resolution.ReasonUnavailable ?? "PJRT plugin is unavailable.");

        PjrtGpuRuntimeLoader.PreloadForBackend((options ?? new PjrtPluginResolverOptions { Backend = "cpu" }).Backend);
        var plugin = PjrtPlugin.Load(resolution.LibraryPath);
        try
        {
            var client = PjrtClient.Create(plugin, PjrtClientCreateOptionDefaults.WithBackendDefaults(options ?? new PjrtPluginResolverOptions { Backend = "cpu" }).ClientOptions);
            return new PjrtBFloat16Backend(plugin, client);
        }
        catch
        {
            plugin.Dispose();
            throw;
        }
    }

    public PjrtBFloat16Tensor CreateMatrix(int rows, int cols, ReadOnlySpan<BFloat16> data = default)
    {
        ThrowIfDisposed();
        PjrtBackendCore.ValidateMatrixShape(rows, cols);
        var expectedLength = checked(rows * cols);
        if (!data.IsEmpty && data.Length != expectedLength)
            throw new ArgumentException($"Data length must be {expectedLength} for a {rows}x{cols} matrix.", nameof(data));

        var buffer = data.IsEmpty
            ? _client.BufferFromHost(new BFloat16[expectedLength], [rows, cols])
            : _client.BufferFromHost(data, [rows, cols]);
        return new PjrtBFloat16Tensor(this, buffer, rows, cols, PjrtElementType.BFloat16);
    }

    public PjrtBFloat16Tensor CreateMatrix(int rows, int cols, ReadOnlySpan<float> data)
    {
        var converted = new BFloat16[data.Length];
        for (var i = 0; i < data.Length; i++)
            converted[i] = BFloat16.FromSingle(data[i]);
        return CreateMatrix(rows, cols, converted);
    }

    Helium.Hardware.IHardwareTensor<BFloat16> Helium.Hardware.IExecutionBackend<BFloat16>.CreateMatrix(int rows, int cols, ReadOnlySpan<BFloat16> initialData)
        => CreateMatrix(rows, cols, initialData);

    public PjrtBFloat16Tensor MatMul(PjrtBFloat16Tensor a, PjrtBFloat16Tensor b)
    {
        ThrowIfDisposed();
        ValidateOwnedBinary(a, b, nameof(MatMul));
        if (a.Cols != b.Rows)
            throw new ArgumentException($"Cannot multiply {a.Rows}x{a.Cols} by {b.Rows}x{b.Cols}.");

        var executable = GetOrCompile(OperationKey.MatMul(a.Rows, a.Cols, b.Cols));
        return new PjrtBFloat16Tensor(this, executable.Execute(a.Buffer, b.Buffer), a.Rows, b.Cols, PjrtElementType.BFloat16);
    }

    public PjrtBFloat16Tensor Add(PjrtBFloat16Tensor a, PjrtBFloat16Tensor b) => Binary(a, b, OperationKind.Add, nameof(Add));
    public PjrtBFloat16Tensor Subtract(PjrtBFloat16Tensor a, PjrtBFloat16Tensor b) => Binary(a, b, OperationKind.Subtract, nameof(Subtract));
    public PjrtBFloat16Tensor Multiply(PjrtBFloat16Tensor a, PjrtBFloat16Tensor b) => Binary(a, b, OperationKind.Multiply, nameof(Multiply));

    public PjrtBFloat16Tensor Negate(PjrtBFloat16Tensor value)
    {
        ValidateUnary(value, nameof(Negate));
        var executable = GetOrCompile(OperationKey.Unary(OperationKind.Negate, value.Rows, value.Cols));
        return new PjrtBFloat16Tensor(this, executable.Execute(value.Buffer), value.Rows, value.Cols, PjrtElementType.BFloat16);
    }

    public PjrtBFloat16Tensor Sum(PjrtBFloat16Tensor value)
    {
        ValidateUnary(value, nameof(Sum));
        var executable = GetOrCompile(OperationKey.Unary(OperationKind.Sum, value.Rows, value.Cols));
        return new PjrtBFloat16Tensor(this, executable.Execute(value.Buffer), 1, 1, PjrtElementType.BFloat16);
    }

    public PjrtBFloat16Tensor Transpose(PjrtBFloat16Tensor value)
    {
        ValidateUnary(value, nameof(Transpose));
        var executable = GetOrCompile(OperationKey.Unary(OperationKind.Transpose, value.Rows, value.Cols));
        return new PjrtBFloat16Tensor(this, executable.Execute(value.Buffer), value.Cols, value.Rows, PjrtElementType.BFloat16);
    }

    public PjrtBFloat16Tensor Scale(PjrtBFloat16Tensor value, BFloat16 scalar)
    {
        ValidateUnary(value, nameof(Scale));
        var executable = GetOrCompile(OperationKey.Scale(value.Rows, value.Cols, scalar));
        return new PjrtBFloat16Tensor(this, executable.Execute(value.Buffer), value.Rows, value.Cols, PjrtElementType.BFloat16);
    }

    public PjrtBFloat16Tensor Reshape(PjrtBFloat16Tensor value, int rows, int cols)
    {
        ValidateUnary(value, nameof(Reshape));
        PjrtBackendCore.ValidateMatrixShape(rows, cols);
        if (checked(rows * cols) != value.Rows * value.Cols)
            throw new ArgumentException("Target shape must preserve element count.");
        using var executable = _client.CompileMlir(StableHloProgram.Reshape(value.Rows, value.Cols, rows, cols, PjrtElementType.BFloat16));
        return new PjrtBFloat16Tensor(this, executable.Execute(value.Buffer), rows, cols, PjrtElementType.BFloat16);
    }

    public PjrtBFloat16Tensor Broadcast(PjrtBFloat16Tensor scalar, int rows, int cols)
    {
        ValidateUnary(scalar, nameof(Broadcast));
        PjrtBackendCore.ValidateMatrixShape(rows, cols);
        if (scalar.Rows != 1 || scalar.Cols != 1)
            throw new ArgumentException("Only 1x1 scalar broadcast is supported.", nameof(scalar));
        using var executable = _client.CompileMlir(StableHloProgram.BroadcastScalar(rows, cols, PjrtElementType.BFloat16));
        return new PjrtBFloat16Tensor(this, executable.Execute(scalar.Buffer), rows, cols, PjrtElementType.BFloat16);
    }

    public PjrtBFloat16Tensor Slice(PjrtBFloat16Tensor value, int startRow, int startCol, int rowCount, int colCount)
    {
        ValidateUnary(value, nameof(Slice));
        PjrtBackendCore.ValidateSlice(value.Rows, value.Cols, startRow, startCol, rowCount, colCount);
        using var executable = _client.CompileMlir(StableHloProgram.Slice(value.Rows, value.Cols, startRow, startCol, rowCount, colCount, PjrtElementType.BFloat16));
        return new PjrtBFloat16Tensor(this, executable.Execute(value.Buffer), rowCount, colCount, PjrtElementType.BFloat16);
    }

    public PjrtBFloat16Tensor Concatenate(PjrtBFloat16Tensor a, PjrtBFloat16Tensor b, int axis)
    {
        ValidateSameShapeBinary(a, b, nameof(Concatenate));
        if (axis is not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(axis), "Axis must be 0 or 1.");
        using var executable = _client.CompileMlir(StableHloProgram.Concatenate(a.Rows, a.Cols, axis, PjrtElementType.BFloat16));
        var result = executable.Execute(a.Buffer, b.Buffer);
        return axis == 0
            ? new PjrtBFloat16Tensor(this, result, a.Rows + b.Rows, a.Cols, PjrtElementType.BFloat16)
            : new PjrtBFloat16Tensor(this, result, a.Rows, a.Cols + b.Cols, PjrtElementType.BFloat16);
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

    Helium.Hardware.IHardwareTensor<BFloat16> Helium.Hardware.IExecutionBackend<BFloat16>.MatMul(Helium.Hardware.IHardwareTensor<BFloat16> left, Helium.Hardware.IHardwareTensor<BFloat16> right) => MatMul(RequirePjrtFloatTensor(left, nameof(left)), RequirePjrtFloatTensor(right, nameof(right)));
    Helium.Hardware.IHardwareTensor<BFloat16> Helium.Hardware.IExecutionBackend<BFloat16>.Add(Helium.Hardware.IHardwareTensor<BFloat16> left, Helium.Hardware.IHardwareTensor<BFloat16> right) => Add(RequirePjrtFloatTensor(left, nameof(left)), RequirePjrtFloatTensor(right, nameof(right)));
    Helium.Hardware.IHardwareTensor<BFloat16> Helium.Hardware.IExecutionBackend<BFloat16>.Subtract(Helium.Hardware.IHardwareTensor<BFloat16> left, Helium.Hardware.IHardwareTensor<BFloat16> right) => Subtract(RequirePjrtFloatTensor(left, nameof(left)), RequirePjrtFloatTensor(right, nameof(right)));
    Helium.Hardware.IHardwareTensor<BFloat16> Helium.Hardware.IExecutionBackend<BFloat16>.Multiply(Helium.Hardware.IHardwareTensor<BFloat16> left, Helium.Hardware.IHardwareTensor<BFloat16> right) => Multiply(RequirePjrtFloatTensor(left, nameof(left)), RequirePjrtFloatTensor(right, nameof(right)));
    Helium.Hardware.IHardwareTensor<BFloat16> Helium.Hardware.IExecutionBackend<BFloat16>.Negate(Helium.Hardware.IHardwareTensor<BFloat16> value) => Negate(RequirePjrtFloatTensor(value, nameof(value)));
    Helium.Hardware.IHardwareTensor<BFloat16> Helium.Hardware.IExecutionBackend<BFloat16>.Scale(Helium.Hardware.IHardwareTensor<BFloat16> value, BFloat16 scalar) => Scale(RequirePjrtFloatTensor(value, nameof(value)), scalar);
    Helium.Hardware.IHardwareTensor<BFloat16> Helium.Hardware.IExecutionBackend<BFloat16>.Transpose(Helium.Hardware.IHardwareTensor<BFloat16> value) => Transpose(RequirePjrtFloatTensor(value, nameof(value)));
    BFloat16 Helium.Hardware.IExecutionBackend<BFloat16>.Sum(Helium.Hardware.IHardwareTensor<BFloat16> value) { using var result = Sum(RequirePjrtFloatTensor(value, nameof(value))); return result.ToArray()[0]; }
    BFloat16 Helium.Hardware.IExecutionBackend<BFloat16>.Dot(Helium.Hardware.IHardwareTensor<BFloat16> left, Helium.Hardware.IHardwareTensor<BFloat16> right) { var a = RequirePjrtFloatTensor(left, nameof(left)); var b = RequirePjrtFloatTensor(right, nameof(right)); using var product = Multiply(a, b); using var sum = Sum(product); return sum.ToArray()[0]; }
    BFloat16 Helium.Hardware.IExecutionBackend<BFloat16>.Mean(Helium.Hardware.IHardwareTensor<BFloat16> value) => throw new NotSupportedException("BFloat16 XLA mean is not supported.");
    BFloat16 Helium.Hardware.IExecutionBackend<BFloat16>.Norm(Helium.Hardware.IHardwareTensor<BFloat16> value) => throw new NotSupportedException("BFloat16 XLA norm is not supported.");
    Helium.Hardware.IHardwareTensor<BFloat16> Helium.Hardware.IExecutionBackend<BFloat16>.LinearSolve(Helium.Hardware.IHardwareTensor<BFloat16> matrix, Helium.Hardware.IHardwareTensor<BFloat16> rightHandSide) => throw new NotSupportedException("BFloat16 XLA linear solve is not supported.");
    Helium.Hardware.IHardwareTensor<BFloat16> Helium.Hardware.IExecutionBackend<BFloat16>.MatrixInverse(Helium.Hardware.IHardwareTensor<BFloat16> value) => throw new NotSupportedException("BFloat16 XLA matrix inverse is not supported.");

    internal PjrtBuffer BufferFromHost(ReadOnlySpan<BFloat16> data, int rows, int cols)
    {
        ThrowIfDisposed();
        return _client.BufferFromHost(data, [rows, cols]);
    }

    internal void RegisterTensor(PjrtBFloat16Tensor tensor) { ThrowIfDisposed(); _liveTensors.Add(tensor); }
    internal void UnregisterTensor(PjrtBFloat16Tensor tensor) { if (!_disposed) _liveTensors.Remove(tensor); }
    internal void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(PjrtBFloat16Backend)); }

    private PjrtBFloat16Tensor Binary(PjrtBFloat16Tensor a, PjrtBFloat16Tensor b, OperationKind kind, string name)
    {
        ValidateSameShapeBinary(a, b, name);
        var executable = GetOrCompile(OperationKey.Binary(kind, a.Rows, a.Cols));
        return new PjrtBFloat16Tensor(this, executable.Execute(a.Buffer, b.Buffer), a.Rows, a.Cols, PjrtElementType.BFloat16);
    }

    private PjrtLoadedExecutable GetOrCompile(OperationKey key) => _executableCache.GetOrCompile(key);

    private static string StableHlo(OperationKey key) => key.Kind switch
    {
        OperationKind.MatMul => StableHloProgram.MatMul(key.Rows, key.Cols, key.OutCols, PjrtElementType.BFloat16),
        OperationKind.Add => StableHloProgram.Binary(key.Rows, key.Cols, "stablehlo.add", PjrtElementType.BFloat16),
        OperationKind.Subtract => StableHloProgram.Binary(key.Rows, key.Cols, "stablehlo.subtract", PjrtElementType.BFloat16),
        OperationKind.Multiply => StableHloProgram.Binary(key.Rows, key.Cols, "stablehlo.multiply", PjrtElementType.BFloat16),
        OperationKind.Negate => StableHloProgram.Unary(key.Rows, key.Cols, "stablehlo.negate", PjrtElementType.BFloat16),
        OperationKind.Sum => StableHloProgram.Sum(key.Rows, key.Cols, PjrtElementType.BFloat16),
        OperationKind.Transpose => StableHloProgram.Transpose(key.Rows, key.Cols, PjrtElementType.BFloat16),
        OperationKind.Scale => StableHloProgram.Scale(key.Rows, key.Cols, key.Scalar, PjrtElementType.BFloat16),
        _ => throw new NotSupportedException($"Unsupported XLA operation: {key.Kind}")
    };

    private void ValidateUnary(PjrtBFloat16Tensor value, string operationName)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!ReferenceEquals(value.Owner, this))
            throw new ArgumentException("Tensor must be owned by this backend.", nameof(value));
        if (value.ElementType != PjrtElementType.BFloat16)
            throw new NotSupportedException($"Only BFloat16 {operationName} is supported.");
    }

    private void ValidateOwnedBinary(PjrtBFloat16Tensor a, PjrtBFloat16Tensor b, string operationName)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (!ReferenceEquals(a.Owner, this) || !ReferenceEquals(b.Owner, this))
            throw new ArgumentException("Both tensors must be owned by this backend.");
        if (a.ElementType != PjrtElementType.BFloat16 || b.ElementType != PjrtElementType.BFloat16)
            throw new NotSupportedException($"Only BFloat16 {operationName} is supported.");
    }

    private void ValidateSameShapeBinary(PjrtBFloat16Tensor a, PjrtBFloat16Tensor b, string operationName)
    {
        ValidateOwnedBinary(a, b, operationName);
        if (a.Rows != b.Rows || a.Cols != b.Cols)
            throw new ArgumentException($"Cannot {operationName} tensors with shapes {a.Rows}x{a.Cols} and {b.Rows}x{b.Cols}.");
    }

    private PjrtBFloat16Tensor RequirePjrtFloatTensor(Helium.Hardware.IHardwareTensor<BFloat16> tensor, string parameterName)
    {
        ThrowIfDisposed();
        if (tensor is not PjrtBFloat16Tensor xlaTensor || !ReferenceEquals(xlaTensor.Owner, this))
            throw new ArgumentException("Tensor must be owned by this XLA backend.", parameterName);
        return xlaTensor;
    }

    private readonly record struct OperationKey(OperationKind Kind, int Rows, int Cols, int OutCols, BFloat16 Scalar = default)
    {
        public static OperationKey MatMul(int rows, int cols, int outCols) => new(OperationKind.MatMul, rows, cols, outCols);
        public static OperationKey Binary(OperationKind kind, int rows, int cols) => new(kind, rows, cols, cols);
        public static OperationKey Unary(OperationKind kind, int rows, int cols) => new(kind, rows, cols, cols);
        public static OperationKey Scale(int rows, int cols, BFloat16 scalar) => new(OperationKind.Scale, rows, cols, cols, scalar);
    }

    private enum OperationKind { MatMul, Add, Subtract, Multiply, Negate, Sum, Transpose, Scale }
}
