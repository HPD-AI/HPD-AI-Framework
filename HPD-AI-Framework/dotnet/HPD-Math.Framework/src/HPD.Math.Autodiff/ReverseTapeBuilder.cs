using HPD.Math.Core;

namespace HPD.Math.Autodiff;

/// <summary>
/// Builder for an explicit reverse-mode tape backed by caller-owned storage.
/// </summary>
public ref struct ReverseTapeBuilder<T>
{
    private readonly Span<ReverseNode<T>> _nodes;

    public ReverseTapeBuilder(Span<ReverseNode<T>> nodes)
    {
        _nodes = nodes;
        Count = 0;
    }

    public int Count { get; private set; }

    public int Capacity => _nodes.Length;

    public void Clear() => Count = 0;

    public ReverseTapeView<T> AsView() => new(_nodes[..Count]);

    public AlgebraStatus TryInput(in T value, out ActiveVar<T> variable) =>
        TryAppend(ReverseOpCode.Input, -1, -1, value, out variable);

    public AlgebraStatus TryConstant(in T value, out ActiveVar<T> variable) =>
        TryAppend(ReverseOpCode.Constant, -1, -1, value, out variable);

    public AlgebraStatus TryAdd<TOps>(
        ActiveVar<T> left,
        ActiveVar<T> right,
        TOps ops,
        out ActiveVar<T> result)
        where TOps : struct, IStatusRingOps<T>
    {
        result = default;
        if (!Contains(left) || !Contains(right))
            return AlgebraStatus.InvalidInput;

        var value = ops.Zero;
        var status = ops.TryAdd(ref value, left.Value, right.Value);
        return status == AlgebraStatus.Ok
            ? TryAppend(ReverseOpCode.Add, left.Index, right.Index, value, out result)
            : status;
    }

    public AlgebraStatus TrySub<TOps>(
        ActiveVar<T> left,
        ActiveVar<T> right,
        TOps ops,
        out ActiveVar<T> result)
        where TOps : struct, IStatusRingOps<T>
    {
        result = default;
        if (!Contains(left) || !Contains(right))
            return AlgebraStatus.InvalidInput;

        var value = ops.Zero;
        var status = ops.TrySub(ref value, left.Value, right.Value);
        return status == AlgebraStatus.Ok
            ? TryAppend(ReverseOpCode.Sub, left.Index, right.Index, value, out result)
            : status;
    }

    public AlgebraStatus TryMul<TOps>(
        ActiveVar<T> left,
        ActiveVar<T> right,
        TOps ops,
        out ActiveVar<T> result)
        where TOps : struct, IStatusRingOps<T>
    {
        result = default;
        if (!Contains(left) || !Contains(right))
            return AlgebraStatus.InvalidInput;

        var value = ops.Zero;
        var status = ops.TryMul(ref value, left.Value, right.Value);
        return status == AlgebraStatus.Ok
            ? TryAppend(ReverseOpCode.Mul, left.Index, right.Index, value, out result)
            : status;
    }

    public AlgebraStatus TryNeg<TOps>(
        ActiveVar<T> value,
        TOps ops,
        out ActiveVar<T> result)
        where TOps : struct, IStatusRingOps<T>
    {
        result = default;
        if (!Contains(value))
            return AlgebraStatus.InvalidInput;

        var primal = ops.Zero;
        var status = ops.TryNeg(ref primal, value.Value);
        return status == AlgebraStatus.Ok
            ? TryAppend(ReverseOpCode.Neg, value.Index, -1, primal, out result)
            : status;
    }

    public AlgebraStatus TryInvert<TOps>(
        ActiveVar<T> value,
        TOps ops,
        out ActiveVar<T> result)
        where TOps : struct, IStatusFieldOps<T>
    {
        result = default;
        if (!Contains(value))
            return AlgebraStatus.InvalidInput;

        var primal = ops.Zero;
        var status = ops.TryInvert(ref primal, value.Value);
        return status == AlgebraStatus.Ok
            ? TryAppend(ReverseOpCode.Inv, value.Index, -1, primal, out result)
            : status;
    }

    private bool Contains(ActiveVar<T> value) =>
        value.Index >= 0 && value.Index < Count;

    private AlgebraStatus TryAppend(
        ReverseOpCode opCode,
        int left,
        int right,
        in T primal,
        out ActiveVar<T> variable)
    {
        variable = default;
        if (Count >= _nodes.Length)
            return AlgebraStatus.InsufficientDestination;

        var index = Count;
        _nodes[index] = new ReverseNode<T>(opCode, left, right, primal);
        Count++;
        variable = new ActiveVar<T>(primal, index);
        return AlgebraStatus.Ok;
    }
}
