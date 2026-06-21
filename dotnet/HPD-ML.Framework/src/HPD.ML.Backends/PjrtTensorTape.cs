namespace HPD.ML.Backends.Pjrt;

public sealed class PjrtTensorTape : IDisposable
{
    private readonly PjrtFloatBackend _backend;
    private readonly List<Node> _nodes = [];
    private readonly List<PjrtFloatTensor> _ownedTensors = [];
    private bool _disposed;

    public PjrtTensorTape(PjrtFloatBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public PjrtFloatTensorVar Watch(PjrtFloatTensor value)
    {
        ThrowIfDisposed();
        ValidateTensor(value);
        var id = AddNode(new Node(OpKind.Input, value, -1, -1));
        return new PjrtFloatTensorVar(this, id, value);
    }

    public PjrtFloatTensorVar Constant(int rows, int cols, ReadOnlySpan<float> values)
    {
        ThrowIfDisposed();
        if (values.Length != rows * cols)
            throw new ArgumentException($"Value length must be {rows * cols} for a {rows}x{cols} tensor.", nameof(values));

        var tensor = Own(_backend.CreateMatrix(rows, cols, values));
        var id = AddNode(new Node(OpKind.Constant, tensor, -1, -1));
        return new PjrtFloatTensorVar(this, id, tensor);
    }

    public PjrtFloatTensorVar MatMul(PjrtFloatTensorVar left, PjrtFloatTensorVar right)
    {
        ThrowIfDisposed();
        EnsureSameTape(left, right);
        var value = _backend.MatMul(left.Value, right.Value);
        return Var(OpKind.MatMul, value, left.Id, right.Id);
    }

    public PjrtFloatTensorVar Add(PjrtFloatTensorVar left, PjrtFloatTensorVar right)
    {
        ThrowIfDisposed();
        EnsureSameTape(left, right);
        var value = _backend.Add(left.Value, right.Value);
        return Var(OpKind.Add, value, left.Id, right.Id);
    }

    public PjrtFloatTensorVar Subtract(PjrtFloatTensorVar left, PjrtFloatTensorVar right)
    {
        ThrowIfDisposed();
        EnsureSameTape(left, right);
        var value = _backend.Subtract(left.Value, right.Value);
        return Var(OpKind.Subtract, value, left.Id, right.Id);
    }

    public PjrtFloatTensorVar Multiply(PjrtFloatTensorVar left, PjrtFloatTensorVar right)
    {
        ThrowIfDisposed();
        EnsureSameTape(left, right);
        var value = _backend.Multiply(left.Value, right.Value);
        return Var(OpKind.Multiply, value, left.Id, right.Id);
    }

    public PjrtFloatTensorVar Negate(PjrtFloatTensorVar value)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        return Var(OpKind.Negate, _backend.Negate(value.Value), value.Id, -1);
    }

    public PjrtFloatTensorVar Sum(PjrtFloatTensorVar value)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        return Var(OpKind.Sum, _backend.Sum(value.Value), value.Id, -1);
    }

    public PjrtFloatTensorVar Mean(PjrtFloatTensorVar value)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        return Var(OpKind.Mean, _backend.Mean(value.Value), value.Id, -1);
    }

    public PjrtFloatTensorVar Transpose(PjrtFloatTensorVar value)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        return Var(OpKind.Transpose, _backend.Transpose(value.Value), value.Id, -1);
    }

    public PjrtFloatTensorVar Scale(PjrtFloatTensorVar value, float scalar)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        return Var(OpKind.Scale, _backend.Scale(value.Value, scalar), value.Id, -1, scalar: scalar);
    }

    public PjrtFloatTensorVar Reshape(PjrtFloatTensorVar value, int rows, int cols)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        return Var(OpKind.Reshape, _backend.Reshape(value.Value, rows, cols), value.Id, -1);
    }

    public PjrtFloatTensorVar Broadcast(PjrtFloatTensorVar scalar, int rows, int cols)
    {
        ThrowIfDisposed();
        EnsureSameTape(scalar);
        return Var(OpKind.Broadcast, _backend.Broadcast(scalar.Value, rows, cols), scalar.Id, -1);
    }

    public PjrtFloatTensorVar BroadcastTo(PjrtFloatTensorVar value, int rows, int cols)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        return Var(OpKind.BroadcastTo, _backend.BroadcastTo(value.Value, rows, cols), value.Id, -1);
    }

    public PjrtFloatTensorVar ReLU(PjrtFloatTensorVar value)
    {
        ThrowIfDisposed();
        EnsureSameTape(value);
        return Var(OpKind.ReLU, _backend.ReLU(value.Value), value.Id, -1);
    }

    public PjrtFloatTensorVar Slice(PjrtFloatTensorVar value, int startRow, int startCol, int rowCount, int colCount)
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

    public PjrtFloatTensorVar Concatenate(PjrtFloatTensorVar left, PjrtFloatTensorVar right, int axis)
    {
        ThrowIfDisposed();
        EnsureSameTape(left, right);
        return Var(OpKind.Concatenate, _backend.Concatenate(left.Value, right.Value, axis), left.Id, right.Id, axis: axis);
    }

    public PjrtFloatTensor Gradient(PjrtFloatTensorVar output, PjrtFloatTensorVar input)
    {
        ThrowIfDisposed();
        EnsureSameTape(output, input);
        var gradients = Backward(output);
        if (gradients.TryGetValue(input.Id, out var gradient))
            return gradient;
        return Zeros(input.Value.Rows, input.Value.Cols);
    }

    public IReadOnlyDictionary<int, PjrtFloatTensor> Gradients(PjrtFloatTensorVar output)
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

    private Dictionary<int, PjrtFloatTensor> Backward(PjrtFloatTensorVar output)
    {
        var gradients = new Dictionary<int, PjrtFloatTensor>
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
                case OpKind.ReLU:
                    AddGradient(gradients, node.Left, _backend.Multiply(upstream, ReluMask(_nodes[node.Left].Value)));
                    break;
                default:
                    throw new NotSupportedException($"Unsupported tensor tape operation: {node.Kind}");
            }
        }

        return gradients;
    }

    private void AddGradient(Dictionary<int, PjrtFloatTensor> gradients, int id, PjrtFloatTensor contribution)
    {
        if (id < 0)
            return;
        Own(contribution);
        if (gradients.TryGetValue(id, out var existing))
            gradients[id] = Own(_backend.Add(existing, contribution));
        else
            gradients[id] = contribution;
    }

    private PjrtFloatTensor ScatterSliceGradient(PjrtFloatTensor upstream, int rows, int cols, int startRow, int startCol)
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

    private PjrtFloatTensor BroadcastGradient(PjrtFloatTensor upstream, int sourceRows, int sourceCols)
    {
        if (sourceRows == upstream.Rows && sourceCols == upstream.Cols)
            return upstream;

        var data = upstream.ToArray();
        var target = new float[checked(sourceRows * sourceCols)];
        if (sourceRows == 1 && sourceCols == 1)
        {
            var sum = 0.0f;
            for (var i = 0; i < data.Length; i++)
                sum += data[i];
            target[0] = sum;
            return _backend.CreateMatrix(1, 1, target);
        }

        if (sourceRows == 1 && sourceCols == upstream.Cols)
        {
            for (var row = 0; row < upstream.Rows; row++)
            {
                for (var col = 0; col < upstream.Cols; col++)
                    target[col] += data[row * upstream.Cols + col];
            }

            return _backend.CreateMatrix(1, sourceCols, target);
        }

        if (sourceCols == 1 && sourceRows == upstream.Rows)
        {
            for (var row = 0; row < upstream.Rows; row++)
            {
                var sum = 0.0f;
                for (var col = 0; col < upstream.Cols; col++)
                    sum += data[row * upstream.Cols + col];
                target[row] = sum;
            }

            return _backend.CreateMatrix(sourceRows, 1, target);
        }

        throw new NotSupportedException($"Unsupported broadcast adjoint from {upstream.Rows}x{upstream.Cols} to {sourceRows}x{sourceCols}.");
    }

    private PjrtFloatTensor ReluMask(PjrtFloatTensor value)
    {
        var data = value.ToArray();
        for (var i = 0; i < data.Length; i++)
            data[i] = data[i] >= 0.0f ? 1.0f : 0.0f;

        return Own(_backend.CreateMatrix(value.Rows, value.Cols, data));
    }

    private PjrtFloatTensorVar Var(
        OpKind kind,
        PjrtFloatTensor value,
        int left,
        int right,
        float scalar = 0.0f,
        int axis = 0,
        int startRow = 0,
        int startCol = 0)
    {
        value = Own(value);
        var id = AddNode(new Node(kind, value, left, right, scalar, axis, startRow, startCol));
        return new PjrtFloatTensorVar(this, id, value);
    }

    private int AddNode(Node node)
    {
        _nodes.Add(node);
        return _nodes.Count - 1;
    }

    private PjrtFloatTensor Ones(int rows, int cols)
    {
        var values = new float[rows * cols];
        Array.Fill(values, 1.0f);
        return Own(_backend.CreateMatrix(rows, cols, values));
    }

    private PjrtFloatTensor Zeros(int rows, int cols) => Own(_backend.CreateMatrix(rows, cols));

    private PjrtFloatTensor Own(PjrtFloatTensor tensor)
    {
        _ownedTensors.Add(tensor);
        return tensor;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PjrtTensorTape));
    }

    private void EnsureSameTape(PjrtFloatTensorVar value)
    {
        if (!ReferenceEquals(value.Tape, this))
            throw new ArgumentException("Tensor variable belongs to a different tape.");
    }

    private void EnsureSameTape(PjrtFloatTensorVar left, PjrtFloatTensorVar right)
    {
        EnsureSameTape(left);
        EnsureSameTape(right);
    }

    private void ValidateTensor(PjrtFloatTensor value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!ReferenceEquals(value.Owner, _backend))
            throw new ArgumentException("Tensor must be owned by this tape's backend.", nameof(value));
    }

    private readonly record struct Node(
        OpKind Kind,
        PjrtFloatTensor Value,
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
        ReLU
    }
}

public readonly struct PjrtFloatTensorVar
{
    internal PjrtFloatTensorVar(PjrtTensorTape tape, int id, PjrtFloatTensor value)
    {
        Tape = tape;
        Id = id;
        Value = value;
    }

    internal PjrtTensorTape Tape { get; }
    internal int Id { get; }
    public PjrtFloatTensor Value { get; }
}
