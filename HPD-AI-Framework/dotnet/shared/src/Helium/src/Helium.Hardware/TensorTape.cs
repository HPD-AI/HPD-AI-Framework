namespace Helium.Hardware;

public enum TensorOpKind : byte
{
    Leaf,
    Add,
    Multiply,
    Negate,
    MatMul,
    MatrixInverse
}

public readonly struct TensorVar<T> where T : unmanaged
{
    private readonly TensorTape<T> _tape;

    internal TensorVar(TensorTape<T> tape, int nodeId)
    {
        _tape = tape;
        NodeId = nodeId;
    }

    internal TensorTape<T> Tape => _tape;
    internal int NodeId { get; }

    public IHardwareTensor<T> Value => _tape.GetValue(NodeId);
    public IHardwareTensor<T>? Gradient => _tape.GetGradient(NodeId);

    public TensorVar<T> Add(TensorVar<T> right) => _tape.Add(this, right);
    public TensorVar<T> Multiply(TensorVar<T> right) => _tape.Multiply(this, right);
    public TensorVar<T> Negate() => _tape.Negate(this);
    public TensorVar<T> MatMul(TensorVar<T> right) => _tape.MatMul(this, right);
    public TensorVar<T> MatrixInverse() => _tape.MatrixInverse(this);

    public static TensorVar<T> MatMul(TensorVar<T> left, TensorVar<T> right) =>
        left._tape.MatMul(left, right);

    public static TensorVar<T> operator +(TensorVar<T> left, TensorVar<T> right) =>
        left._tape.Add(left, right);

    public static TensorVar<T> operator -(TensorVar<T> value) => value._tape.Negate(value);

    public static TensorVar<T> operator -(TensorVar<T> left, TensorVar<T> right) =>
        left._tape.Add(left, right._tape.Negate(right));

    public static TensorVar<T> operator *(TensorVar<T> left, TensorVar<T> right) =>
        left._tape.Multiply(left, right);
}

/// <summary>
/// Tensor-aware reverse-mode tape. Matrix operations are recorded as one node,
/// so a matrix multiply contributes one tape entry instead of scalar tracing every multiply-add.
/// </summary>
public sealed class TensorTape<T> : IDisposable where T : unmanaged
{
    private readonly IExecutionBackend<T> _backend;
    private readonly List<Node> _nodes = [];
    private bool _disposed;

    public TensorTape(IExecutionBackend<T> backend) => _backend = backend;

    public int EntryCount
    {
        get
        {
            var count = 0;
            foreach (var node in _nodes)
            {
                if (node.Kind != TensorOpKind.Leaf)
                    count++;
            }
            return count;
        }
    }

    public TensorVar<T> Variable(IHardwareTensor<T> value)
    {
        ThrowIfDisposed();
        var node = new Node(TensorOpKind.Leaf, value, ownsValue: false);
        _nodes.Add(node);
        return new TensorVar<T>(this, _nodes.Count - 1);
    }

    public TensorVar<T> Add(TensorVar<T> left, TensorVar<T> right)
    {
        ThrowIfDisposed();
        RequireSameTape(left);
        RequireSameTape(right);

        var value = _backend.Add(left.Value, right.Value);
        var node = new Node(TensorOpKind.Add, value, ownsValue: true)
        {
            Left = left.NodeId,
            Right = right.NodeId
        };
        _nodes.Add(node);
        return new TensorVar<T>(this, _nodes.Count - 1);
    }

    public TensorVar<T> Multiply(TensorVar<T> left, TensorVar<T> right)
    {
        ThrowIfDisposed();
        RequireSameTape(left);
        RequireSameTape(right);

        var value = _backend.Multiply(left.Value, right.Value);
        var node = new Node(TensorOpKind.Multiply, value, ownsValue: true)
        {
            Left = left.NodeId,
            Right = right.NodeId
        };
        _nodes.Add(node);
        return new TensorVar<T>(this, _nodes.Count - 1);
    }

    public TensorVar<T> Negate(TensorVar<T> value)
    {
        ThrowIfDisposed();
        RequireSameTape(value);

        var result = _backend.Negate(value.Value);
        var node = new Node(TensorOpKind.Negate, result, ownsValue: true)
        {
            Left = value.NodeId
        };
        _nodes.Add(node);
        return new TensorVar<T>(this, _nodes.Count - 1);
    }

    public TensorVar<T> MatrixInverse(TensorVar<T> value)
    {
        ThrowIfDisposed();
        RequireSameTape(value);

        var inverse = _backend.MatrixInverse(value.Value);
        var node = new Node(TensorOpKind.MatrixInverse, inverse, ownsValue: true)
        {
            Left = value.NodeId
        };
        _nodes.Add(node);
        return new TensorVar<T>(this, _nodes.Count - 1);
    }

    public TensorVar<T> MatMul(TensorVar<T> left, TensorVar<T> right)
    {
        ThrowIfDisposed();
        RequireSameTape(left);
        RequireSameTape(right);

        var value = _backend.MatMul(left.Value, right.Value);
        var node = new Node(TensorOpKind.MatMul, value, ownsValue: true)
        {
            Left = left.NodeId,
            Right = right.NodeId
        };
        _nodes.Add(node);
        return new TensorVar<T>(this, _nodes.Count - 1);
    }

    public void Backward(TensorVar<T> output)
    {
        ThrowIfDisposed();
        RequireSameTape(output);
        using var seed = OnesLike(output.Value);
        Backward(output, seed);
    }

    public void Backward(TensorVar<T> output, IHardwareTensor<T> seedGradient)
    {
        ThrowIfDisposed();
        RequireSameTape(output);
        if (seedGradient.Rows != output.Value.Rows || seedGradient.Cols != output.Value.Cols)
            throw new ArgumentException("Seed gradient shape must match the output tensor.", nameof(seedGradient));

        ClearGradients();
        _nodes[output.NodeId].Gradient = Clone(seedGradient);

        for (var i = _nodes.Count - 1; i >= 0; i--)
        {
            var node = _nodes[i];
            if (node.Gradient is null)
                continue;

            if (node.Kind == TensorOpKind.Add)
                BackpropAdd(node);
            else if (node.Kind == TensorOpKind.Multiply)
                BackpropMultiply(node);
            else if (node.Kind == TensorOpKind.Negate)
                BackpropNegate(node);
            else if (node.Kind == TensorOpKind.MatMul)
                BackpropMatMul(node);
            else if (node.Kind == TensorOpKind.MatrixInverse)
                BackpropMatrixInverse(node);
        }
    }

    public IHardwareTensor<T> GetValue(int nodeId)
    {
        ThrowIfDisposed();
        return _nodes[nodeId].Value;
    }

    public IHardwareTensor<T>? GetGradient(int nodeId)
    {
        ThrowIfDisposed();
        return _nodes[nodeId].Gradient;
    }

    public IHardwareTensor<T> RequireGradient(TensorVar<T> variable)
    {
        ThrowIfDisposed();
        RequireSameTape(variable);
        return _nodes[variable.NodeId].Gradient
            ?? throw new InvalidOperationException("No gradient has been computed for this tensor.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        ClearGradients();
        foreach (var node in _nodes)
        {
            if (node.OwnsValue)
                node.Value.Dispose();
        }
        _disposed = true;
    }

    private void BackpropAdd(Node node)
    {
        AccumulateGradient(node.Left, Clone(node.Gradient!));
        AccumulateGradient(node.Right, Clone(node.Gradient!));
    }

    private void BackpropMultiply(Node node)
    {
        var left = _nodes[node.Left];
        var right = _nodes[node.Right];
        var dy = node.Gradient!;

        var leftContribution = _backend.Multiply(dy, right.Value);
        var rightContribution = _backend.Multiply(dy, left.Value);

        AccumulateGradient(node.Left, leftContribution);
        AccumulateGradient(node.Right, rightContribution);
    }

    private void BackpropNegate(Node node)
    {
        var contribution = _backend.Negate(node.Gradient!);
        AccumulateGradient(node.Left, contribution);
    }

    private void BackpropMatMul(Node node)
    {
        var left = _nodes[node.Left];
        var right = _nodes[node.Right];
        var dy = node.Gradient!;

        using var rightT = _backend.Transpose(right.Value);
        using var leftT = _backend.Transpose(left.Value);
        var leftContribution = _backend.MatMul(dy, rightT);
        var rightContribution = _backend.MatMul(leftT, dy);

        AccumulateGradient(node.Left, leftContribution);
        AccumulateGradient(node.Right, rightContribution);
    }

    private void BackpropMatrixInverse(Node node)
    {
        var dy = node.Gradient!;

        using var inverseT = _backend.Transpose(node.Value);
        using var left = _backend.MatMul(inverseT, dy);
        using var positive = _backend.MatMul(left, inverseT);
        var contribution = _backend.Negate(positive);

        AccumulateGradient(node.Left, contribution);
    }

    private void AccumulateGradient(int nodeId, IHardwareTensor<T> contribution)
    {
        var node = _nodes[nodeId];
        if (node.Gradient is null)
        {
            node.Gradient = contribution;
            return;
        }

        var sum = _backend.Add(node.Gradient, contribution);
        node.Gradient.Dispose();
        contribution.Dispose();
        node.Gradient = sum;
    }

    private IHardwareTensor<T> Clone(IHardwareTensor<T> tensor)
    {
        var data = new T[checked(tensor.Rows * tensor.Cols)];
        tensor.CopyToHost(data);
        return _backend.CreateMatrix(tensor.Rows, tensor.Cols, data);
    }

    private IHardwareTensor<T> OnesLike(IHardwareTensor<T> tensor)
    {
        var data = new T[checked(tensor.Rows * tensor.Cols)];

        if (typeof(T) == typeof(double))
        {
            Span<T> dataSpan = data;
            var values = System.Runtime.InteropServices.MemoryMarshal.Cast<T, double>(dataSpan);
            values.Fill(1.0);
            return _backend.CreateMatrix(tensor.Rows, tensor.Cols, data);
        }

        if (typeof(T) == typeof(float))
        {
            Span<T> dataSpan = data;
            var values = System.Runtime.InteropServices.MemoryMarshal.Cast<T, float>(dataSpan);
            values.Fill(1.0f);
            return _backend.CreateMatrix(tensor.Rows, tensor.Cols, data);
        }

        throw new NotSupportedException("Default tensor gradient seeds are currently supported for double and float tensors.");
    }

    private void ClearGradients()
    {
        foreach (var node in _nodes)
        {
            node.Gradient?.Dispose();
            node.Gradient = null;
        }
    }

    private void RequireSameTape(TensorVar<T> variable)
    {
        if (!ReferenceEquals(variable.Tape, this))
            throw new ArgumentException("Tensor variable belongs to a different tape.", nameof(variable));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TensorTape<T>));
    }

    private sealed class Node
    {
        public Node(TensorOpKind kind, IHardwareTensor<T> value, bool ownsValue)
        {
            Kind = kind;
            Value = value;
            OwnsValue = ownsValue;
        }

        public TensorOpKind Kind { get; }
        public IHardwareTensor<T> Value { get; }
        public bool OwnsValue { get; }
        public int Left { get; init; }
        public int Right { get; init; }
        public IHardwareTensor<T>? Gradient { get; set; }
    }
}
