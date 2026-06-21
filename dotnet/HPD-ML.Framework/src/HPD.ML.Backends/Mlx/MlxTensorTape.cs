namespace HPD.ML.Backends.Mlx;

public sealed class MlxTensorTape : IDisposable
{
    private readonly MlxFloatBackend _backend;
    private readonly List<Node> _nodes = [];
    private readonly List<MlxFloatTensor> _ownedTensors = [];
    private bool _disposed;

    public MlxTensorTape(MlxFloatBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public MlxFloatTensorVar Watch(MlxFloatTensor value)
    {
        ThrowIfDisposed();
        ValidateTensor(value);
        var id = AddNode(new Node(OpKind.Input, value, -1, -1));
        return new MlxFloatTensorVar(this, id, value);
    }

    public MlxFloatTensorVar Constant(int rows, int cols, ReadOnlySpan<float> values)
    {
        ThrowIfDisposed();
        if (values.Length != rows * cols)
            throw new ArgumentException($"Value length must be {rows * cols} for a {rows}x{cols} tensor.", nameof(values));

        var tensor = Own(_backend.CreateMatrix(rows, cols, values));
        var id = AddNode(new Node(OpKind.Constant, tensor, -1, -1));
        return new MlxFloatTensorVar(this, id, tensor);
    }

    public MlxFloatTensorVar ConstantLike(MlxFloatTensorVar value, float scalar)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        var values = new float[value.Value.Rows * value.Value.Cols];
        Array.Fill(values, scalar);
        return Constant(value.Value.Rows, value.Value.Cols, values);
    }

    public MlxFloatTensorVar MatMul(MlxFloatTensorVar left, MlxFloatTensorVar right)
    {
        ThrowIfDisposed();
        EnsureSameTape(left, right);
        return Var(OpKind.MatMul, _backend.MatMul(left.Value, right.Value), left.Id, right.Id);
    }

    public MlxFloatTensorVar Add(MlxFloatTensorVar left, MlxFloatTensorVar right)
    {
        ThrowIfDisposed();
        EnsureSameTape(left, right);
        return Var(OpKind.Add, _backend.Add(left.Value, right.Value), left.Id, right.Id);
    }

    public MlxFloatTensorVar Subtract(MlxFloatTensorVar left, MlxFloatTensorVar right)
    {
        ThrowIfDisposed();
        EnsureSameTape(left, right);
        return Var(OpKind.Subtract, _backend.Subtract(left.Value, right.Value), left.Id, right.Id);
    }

    public MlxFloatTensorVar Multiply(MlxFloatTensorVar left, MlxFloatTensorVar right)
    {
        ThrowIfDisposed();
        EnsureSameTape(left, right);
        return Var(OpKind.Multiply, _backend.Multiply(left.Value, right.Value), left.Id, right.Id);
    }

    public MlxFloatTensorVar Divide(MlxFloatTensorVar left, MlxFloatTensorVar right)
    {
        ThrowIfDisposed();
        EnsureSameTape(left, right);
        return Var(OpKind.Divide, _backend.Divide(left.Value, right.Value), left.Id, right.Id);
    }

    public MlxFloatTensorVar Negate(MlxFloatTensorVar value)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        return Var(OpKind.Negate, _backend.Negate(value.Value), value.Id, -1);
    }

    public MlxFloatTensorVar Sum(MlxFloatTensorVar value)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        return Var(OpKind.Sum, _backend.CreateMatrix(1, 1, [_backend.Sum(value.Value)]), value.Id, -1);
    }

    public MlxFloatTensorVar Mean(MlxFloatTensorVar value)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        return Var(OpKind.Mean, _backend.CreateMatrix(1, 1, [_backend.Mean(value.Value)]), value.Id, -1);
    }

    public MlxFloatTensorVar Transpose(MlxFloatTensorVar value)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        return Var(OpKind.Transpose, _backend.Transpose(value.Value), value.Id, -1);
    }

    public MlxFloatTensorVar Scale(MlxFloatTensorVar value, float scalar)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        return Var(OpKind.Scale, _backend.Scale(value.Value, scalar), value.Id, -1, scalar: scalar);
    }

    public MlxFloatTensorVar Reshape(MlxFloatTensorVar value, int rows, int cols)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        return Var(OpKind.Reshape, _backend.Reshape(value.Value, rows, cols), value.Id, -1);
    }

    public MlxFloatTensorVar Broadcast(MlxFloatTensorVar scalar, int rows, int cols)
    {
        ThrowIfDisposed();
        EnsureSameTape(scalar);
        return Var(OpKind.Broadcast, _backend.Broadcast(scalar.Value, rows, cols), scalar.Id, -1);
    }

    public MlxFloatTensorVar BroadcastTo(MlxFloatTensorVar value, int rows, int cols)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        return Var(OpKind.BroadcastTo, _backend.BroadcastTo(value.Value, rows, cols), value.Id, -1);
    }

    public MlxFloatTensorVar Slice(MlxFloatTensorVar value, int startRow, int startCol, int rowCount, int colCount)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        return Var(
            OpKind.Slice,
            _backend.Slice(value.Value, startRow, startCol, rowCount, colCount),
            value.Id,
            -1,
            startRow: startRow,
            startCol: startCol);
    }

    public MlxFloatTensorVar Concatenate(MlxFloatTensorVar left, MlxFloatTensorVar right, int axis)
    {
        ThrowIfDisposed();
        EnsureSameTape(left, right);
        return Var(OpKind.Concatenate, _backend.Concatenate(left.Value, right.Value, axis), left.Id, right.Id, axis: axis);
    }

    public MlxFloatTensorVar Exp(MlxFloatTensorVar value)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        return Var(OpKind.Exp, _backend.Exp(value.Value), value.Id, -1);
    }

    public MlxFloatTensorVar Log(MlxFloatTensorVar value)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        return Var(OpKind.Log, _backend.Log(value.Value), value.Id, -1);
    }

    public MlxFloatTensorVar Sqrt(MlxFloatTensorVar value)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        return Var(OpKind.Sqrt, _backend.Sqrt(value.Value), value.Id, -1);
    }

    public MlxFloatTensorVar Tanh(MlxFloatTensorVar value)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        return Var(OpKind.Tanh, _backend.Tanh(value.Value), value.Id, -1);
    }

    public MlxFloatTensorVar Sigmoid(MlxFloatTensorVar value)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        return Var(OpKind.Sigmoid, _backend.Sigmoid(value.Value), value.Id, -1);
    }

    public MlxFloatTensorVar ReLU(MlxFloatTensorVar value)
        => LeakyReLU(value, 0.0f);

    public MlxFloatTensorVar LeakyReLU(MlxFloatTensorVar value, float negativeSlope = 0.01f)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        if (!float.IsFinite(negativeSlope) || negativeSlope < 0.0f)
            throw new ArgumentOutOfRangeException(nameof(negativeSlope), "Negative slope must be finite and non-negative.");

        var scaled = Own(_backend.Scale(value.Value, negativeSlope));
        var result = _backend.Maximum(value.Value, scaled);
        return Var(OpKind.LeakyReLU, result, value.Id, -1, scalar: negativeSlope);
    }

    public MlxFloatTensorVar Softmax(MlxFloatTensorVar value, int axis, bool precise = true)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        if (axis is not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(axis), "Axis must be 0 or 1.");

        return Var(OpKind.Softmax, _backend.Softmax(value.Value, axis, precise), value.Id, -1, axis: axis);
    }

    public MlxFloatTensor Gradient(MlxFloatTensorVar output, MlxFloatTensorVar input)
    {
        ThrowIfDisposed();
        EnsureSameTape(output, input);
        var gradients = Backward(output);
        return gradients.TryGetValue(input.Id, out var gradient)
            ? gradient
            : Zeros(input.Value.Rows, input.Value.Cols);
    }

    public IReadOnlyDictionary<int, MlxFloatTensor> Gradients(MlxFloatTensorVar output)
    {
        ThrowIfDisposed();
        EnsureSameTape(output);
        return Backward(output);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        for (var i = _ownedTensors.Count - 1; i >= 0; i--)
            _ownedTensors[i].Dispose();
        _ownedTensors.Clear();
        _nodes.Clear();
    }

    private Dictionary<int, MlxFloatTensor> Backward(MlxFloatTensorVar output)
    {
        var gradients = new Dictionary<int, MlxFloatTensor>
        {
            [output.Id] = Ones(output.Value.Rows, output.Value.Cols)
        };

        for (var id = _nodes.Count - 1; id >= 0; id--)
        {
            if (!gradients.TryGetValue(id, out var upstream))
                continue;

            var node = _nodes[id];
            switch (node.Kind)
            {
                case OpKind.Input:
                case OpKind.Constant:
                    break;
                case OpKind.MatMul:
                    var rightT = Own(_backend.Transpose(_nodes[node.Right].Value));
                    var leftT = Own(_backend.Transpose(_nodes[node.Left].Value));
                    AddGradient(gradients, node.Left, _backend.MatMul(upstream, rightT));
                    AddGradient(gradients, node.Right, _backend.MatMul(leftT, upstream));
                    break;
                case OpKind.Add:
                    AddGradient(gradients, node.Left, upstream);
                    AddGradient(gradients, node.Right, upstream);
                    break;
                case OpKind.Subtract:
                    AddGradient(gradients, node.Left, upstream);
                    AddGradient(gradients, node.Right, _backend.Negate(upstream));
                    break;
                case OpKind.Multiply:
                    AddGradient(gradients, node.Left, _backend.Multiply(upstream, _nodes[node.Right].Value));
                    AddGradient(gradients, node.Right, _backend.Multiply(upstream, _nodes[node.Left].Value));
                    break;
                case OpKind.Divide:
                    AddGradient(gradients, node.Left, _backend.Divide(upstream, _nodes[node.Right].Value));
                    var divideNumerator = Own(_backend.Multiply(upstream, _nodes[node.Left].Value));
                    var divideDenominator = Own(_backend.Square(_nodes[node.Right].Value));
                    var divideQuotient = Own(_backend.Divide(divideNumerator, divideDenominator));
                    AddGradient(
                        gradients,
                        node.Right,
                        _backend.Negate(divideQuotient));
                    break;
                case OpKind.Negate:
                    AddGradient(gradients, node.Left, _backend.Negate(upstream));
                    break;
                case OpKind.Sum:
                    AddGradient(gradients, node.Left, _backend.Broadcast(upstream, _nodes[node.Left].Value.Rows, _nodes[node.Left].Value.Cols));
                    break;
                case OpKind.Mean:
                    var meanBroadcast = Own(_backend.Broadcast(upstream, _nodes[node.Left].Value.Rows, _nodes[node.Left].Value.Cols));
                    AddGradient(
                        gradients,
                        node.Left,
                        _backend.Scale(
                            meanBroadcast,
                            1.0f / (_nodes[node.Left].Value.Rows * _nodes[node.Left].Value.Cols)));
                    break;
                case OpKind.Transpose:
                    AddGradient(gradients, node.Left, _backend.Transpose(upstream));
                    break;
                case OpKind.Scale:
                    AddGradient(gradients, node.Left, _backend.Scale(upstream, node.Scalar));
                    break;
                case OpKind.Reshape:
                    AddGradient(gradients, node.Left, _backend.Reshape(upstream, _nodes[node.Left].Value.Rows, _nodes[node.Left].Value.Cols));
                    break;
                case OpKind.Broadcast:
                case OpKind.BroadcastTo:
                    AddGradient(gradients, node.Left, BroadcastGradient(upstream, _nodes[node.Left].Value.Rows, _nodes[node.Left].Value.Cols));
                    break;
                case OpKind.Slice:
                    AddGradient(gradients, node.Left, ScatterSliceGradient(upstream, _nodes[node.Left].Value.Rows, _nodes[node.Left].Value.Cols, node.StartRow, node.StartCol));
                    break;
                case OpKind.Concatenate:
                    if (node.Axis == 0)
                    {
                        AddGradient(gradients, node.Left, _backend.Slice(upstream, 0, 0, _nodes[node.Left].Value.Rows, _nodes[node.Left].Value.Cols));
                        AddGradient(gradients, node.Right, _backend.Slice(upstream, _nodes[node.Left].Value.Rows, 0, _nodes[node.Right].Value.Rows, _nodes[node.Right].Value.Cols));
                    }
                    else
                    {
                        AddGradient(gradients, node.Left, _backend.Slice(upstream, 0, 0, _nodes[node.Left].Value.Rows, _nodes[node.Left].Value.Cols));
                        AddGradient(gradients, node.Right, _backend.Slice(upstream, 0, _nodes[node.Left].Value.Cols, _nodes[node.Right].Value.Rows, _nodes[node.Right].Value.Cols));
                    }
                    break;
                case OpKind.Exp:
                    AddGradient(gradients, node.Left, _backend.Multiply(upstream, node.Value));
                    break;
                case OpKind.Log:
                    AddGradient(gradients, node.Left, _backend.Divide(upstream, _nodes[node.Left].Value));
                    break;
                case OpKind.Sqrt:
                    var sqrtDenominator = Own(_backend.Scale(node.Value, 2.0f));
                    AddGradient(gradients, node.Left, _backend.Divide(upstream, sqrtDenominator));
                    break;
                case OpKind.Tanh:
                    var tanhSquared = Own(_backend.Multiply(node.Value, node.Value));
                    var tanhDerivative = Own(_backend.Subtract(Ones(node.Value.Rows, node.Value.Cols), tanhSquared));
                    AddGradient(gradients, node.Left, _backend.Multiply(upstream, tanhDerivative));
                    break;
                case OpKind.Sigmoid:
                    var oneMinusSigmoid = Own(_backend.Subtract(Ones(node.Value.Rows, node.Value.Cols), node.Value));
                    var sigmoidDerivative = Own(_backend.Multiply(node.Value, oneMinusSigmoid));
                    AddGradient(gradients, node.Left, _backend.Multiply(upstream, sigmoidDerivative));
                    break;
                case OpKind.LeakyReLU:
                    AddGradient(gradients, node.Left, _backend.Multiply(upstream, LeakyReluMask(_nodes[node.Left].Value, node.Scalar)));
                    break;
                case OpKind.Softmax:
                    var weightedUpstream = Own(_backend.Multiply(upstream, node.Value));
                    var axisSum = Own(_backend.SumAxis(weightedUpstream, node.Axis));
                    var broadcastAxisSum = Own(_backend.BroadcastTo(axisSum, node.Value.Rows, node.Value.Cols));
                    var centeredUpstream = Own(_backend.Subtract(upstream, broadcastAxisSum));
                    AddGradient(gradients, node.Left, _backend.Multiply(node.Value, centeredUpstream));
                    break;
                default:
                    throw new NotSupportedException($"Unsupported MLX tensor tape operation: {node.Kind}");
            }
        }

        return gradients;
    }

    private void AddGradient(Dictionary<int, MlxFloatTensor> gradients, int id, MlxFloatTensor contribution)
    {
        if (id < 0)
            return;
        Own(contribution);
        if (gradients.TryGetValue(id, out var existing))
            gradients[id] = Own(_backend.Add(existing, contribution));
        else
            gradients[id] = contribution;
    }

    private MlxFloatTensor ScatterSliceGradient(MlxFloatTensor upstream, int rows, int cols, int startRow, int startCol)
    {
        var source = upstream.ToArray();
        var target = new float[rows * cols];
        for (var row = 0; row < upstream.Rows; row++)
        {
            for (var col = 0; col < upstream.Cols; col++)
                target[(startRow + row) * cols + startCol + col] = source[row * upstream.Cols + col];
        }

        return _backend.CreateMatrix(rows, cols, target);
    }

    private MlxFloatTensor BroadcastGradient(MlxFloatTensor upstream, int sourceRows, int sourceCols)
    {
        if (sourceRows == upstream.Rows && sourceCols == upstream.Cols)
            return upstream;
        if (sourceRows == 1 && sourceCols == 1)
            return Own(_backend.CreateMatrix(1, 1, [_backend.Sum(upstream)]));
        if (sourceRows == 1 && sourceCols == upstream.Cols)
            return Own(_backend.SumAxis(upstream, axis: 0));
        if (sourceCols == 1 && sourceRows == upstream.Rows)
            return Own(_backend.SumAxis(upstream, axis: 1));

        throw new NotSupportedException($"Unsupported broadcast adjoint from {upstream.Rows}x{upstream.Cols} to {sourceRows}x{sourceCols}.");
    }

    private MlxFloatTensor LeakyReluMask(MlxFloatTensor value, float negativeSlope)
    {
        var data = value.ToArray();
        for (var i = 0; i < data.Length; i++)
            data[i] = data[i] >= 0.0f ? 1.0f : negativeSlope;

        return Own(_backend.CreateMatrix(value.Rows, value.Cols, data));
    }

    private MlxFloatTensorVar Var(
        OpKind kind,
        MlxFloatTensor value,
        int left,
        int right,
        float scalar = 0.0f,
        int axis = 0,
        int startRow = 0,
        int startCol = 0)
    {
        _ownedTensors.Add(value);
        var id = AddNode(new Node(kind, value, left, right, scalar, axis, startRow, startCol));
        return new MlxFloatTensorVar(this, id, value);
    }

    private int AddNode(Node node)
    {
        _nodes.Add(node);
        return _nodes.Count - 1;
    }

    private MlxFloatTensor Ones(int rows, int cols)
    {
        var values = new float[rows * cols];
        Array.Fill(values, 1.0f);
        return Own(_backend.CreateMatrix(rows, cols, values));
    }

    private MlxFloatTensor Zeros(int rows, int cols) => Own(_backend.CreateMatrix(rows, cols));

    private MlxFloatTensor Own(MlxFloatTensor tensor)
    {
        _ownedTensors.Add(tensor);
        return tensor;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MlxTensorTape));
    }

    private void EnsureSameTape(MlxFloatTensorVar value)
    {
        if (!ReferenceEquals(value.Tape, this))
            throw new ArgumentException("Tensor variable belongs to a different tape.");
    }

    private void EnsureSameTape(MlxFloatTensorVar left, MlxFloatTensorVar right)
    {
        EnsureSameTape(left);
        EnsureSameTape(right);
    }

    private void ValidateTensor(MlxFloatTensor value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!ReferenceEquals(value.Owner, _backend))
            throw new ArgumentException("Tensor must be owned by this tape's backend.", nameof(value));
    }

    private readonly record struct Node(
        OpKind Kind,
        MlxFloatTensor Value,
        int Left,
        int Right,
        float Scalar = 0.0f,
        int Axis = 0,
        int StartRow = 0,
        int StartCol = 0);

    private enum OpKind
    {
        Input,
        Constant,
        MatMul,
        Add,
        Subtract,
        Multiply,
        Divide,
        Negate,
        Sum,
        Mean,
        Transpose,
        Scale,
        Reshape,
        Broadcast,
        BroadcastTo,
        Slice,
        Concatenate,
        Exp,
        Log,
        Sqrt,
        Tanh,
        Sigmoid,
        LeakyReLU,
        Softmax
    }
}

public readonly struct MlxFloatTensorVar
{
    internal MlxFloatTensorVar(MlxTensorTape tape, int id, MlxFloatTensor value)
    {
        Tape = tape;
        Id = id;
        Value = value;
    }

    internal MlxTensorTape Tape { get; }
    internal int Id { get; }
    public MlxFloatTensor Value { get; }
}
