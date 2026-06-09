using HPD.Math.Core;

namespace HPD.Math.Autodiff;

/// <summary>
/// Reverse-mode kernels over explicit operation-code tapes.
/// </summary>
public static class ReverseTapeKernels
{
    public static AlgebraStatus TryBackward<T, TOps>(
        ReverseTapeView<T> tape,
        int outputIndex,
        Span<T> gradients,
        TOps ops)
        where TOps : struct, IStatusRingOps<T>
    {
        if (outputIndex < 0 || outputIndex >= tape.Count)
            return AlgebraStatus.InvalidInput;
        if (gradients.Length < tape.Count)
            return AlgebraStatus.InsufficientDestination;

        for (var i = 0; i < tape.Count; i++)
            gradients[i] = ops.Zero;
        gradients[outputIndex] = ops.One;

        for (var i = tape.Count - 1; i >= 0; i--)
        {
            var node = tape[i];
            var upstream = gradients[i];
            if (ops.Eq(upstream, ops.Zero))
                continue;

            var status = node.OpCode switch
            {
                ReverseOpCode.Input => AlgebraStatus.Ok,
                ReverseOpCode.Constant => AlgebraStatus.Ok,
                ReverseOpCode.Add => BackwardAdd(tape, node, gradients, upstream, ops),
                ReverseOpCode.Sub => BackwardSub(tape, node, gradients, upstream, ops),
                ReverseOpCode.Mul => BackwardMul(tape, node, gradients, upstream, ops),
                ReverseOpCode.Neg => BackwardNeg(tape, node, gradients, upstream, ops),
                ReverseOpCode.Inv => BackwardInv(tape, node, gradients, upstream, ops),
                _ => AlgebraStatus.InvalidInput
            };

            if (status != AlgebraStatus.Ok)
                return status;
        }

        return AlgebraStatus.Ok;
    }

    private static AlgebraStatus BackwardAdd<T, TOps>(
        ReverseTapeView<T> tape,
        ReverseNode<T> node,
        Span<T> gradients,
        in T upstream,
        TOps ops)
        where TOps : struct, IStatusRingOps<T>
    {
        if (!HasBinaryInputs(tape, node))
            return AlgebraStatus.InvalidInput;

        var status = AddToInput(tape, node.Left, gradients, upstream, ops);
        return status == AlgebraStatus.Ok
            ? AddToInput(tape, node.Right, gradients, upstream, ops)
            : status;
    }

    private static AlgebraStatus BackwardSub<T, TOps>(
        ReverseTapeView<T> tape,
        ReverseNode<T> node,
        Span<T> gradients,
        in T upstream,
        TOps ops)
        where TOps : struct, IStatusRingOps<T>
    {
        if (!HasBinaryInputs(tape, node))
            return AlgebraStatus.InvalidInput;

        var negative = ops.Zero;
        var status = ops.TryNeg(ref negative, upstream);
        if (status != AlgebraStatus.Ok)
            return status;

        status = AddToInput(tape, node.Left, gradients, upstream, ops);
        return status == AlgebraStatus.Ok
            ? AddToInput(tape, node.Right, gradients, negative, ops)
            : status;
    }

    private static AlgebraStatus BackwardMul<T, TOps>(
        ReverseTapeView<T> tape,
        ReverseNode<T> node,
        Span<T> gradients,
        in T upstream,
        TOps ops)
        where TOps : struct, IStatusRingOps<T>
    {
        if (!HasBinaryInputs(tape, node))
            return AlgebraStatus.InvalidInput;

        var leftIncrement = ops.Zero;
        var rightIncrement = ops.Zero;
        var status = ops.TryMul(ref leftIncrement, upstream, tape[node.Right].Primal);
        if (status != AlgebraStatus.Ok)
            return status;
        status = ops.TryMul(ref rightIncrement, upstream, tape[node.Left].Primal);
        if (status != AlgebraStatus.Ok)
            return status;

        status = AddToInput(tape, node.Left, gradients, leftIncrement, ops);
        return status == AlgebraStatus.Ok
            ? AddToInput(tape, node.Right, gradients, rightIncrement, ops)
            : status;
    }

    private static AlgebraStatus BackwardNeg<T, TOps>(
        ReverseTapeView<T> tape,
        ReverseNode<T> node,
        Span<T> gradients,
        in T upstream,
        TOps ops)
        where TOps : struct, IStatusRingOps<T>
    {
        if (!HasUnaryInput(tape, node))
            return AlgebraStatus.InvalidInput;

        var negative = ops.Zero;
        var status = ops.TryNeg(ref negative, upstream);
        return status == AlgebraStatus.Ok
            ? AddToInput(tape, node.Left, gradients, negative, ops)
            : status;
    }

    private static AlgebraStatus BackwardInv<T, TOps>(
        ReverseTapeView<T> tape,
        ReverseNode<T> node,
        Span<T> gradients,
        in T upstream,
        TOps ops)
        where TOps : struct, IStatusRingOps<T>
    {
        if (node.Left < 0 || node.Left >= gradients.Length)
            return AlgebraStatus.InvalidInput;

        var invSquared = ops.Zero;
        var derivative = ops.Zero;
        var increment = ops.Zero;

        var status = ops.TryMul(ref invSquared, node.Primal, node.Primal);
        if (status != AlgebraStatus.Ok)
            return status;
        status = ops.TryNeg(ref derivative, invSquared);
        if (status != AlgebraStatus.Ok)
            return status;
        status = ops.TryMul(ref increment, upstream, derivative);
        return status == AlgebraStatus.Ok
            ? AddToInput(tape, node.Left, gradients, increment, ops)
            : status;
    }

    private static AlgebraStatus AddToInput<T, TOps>(
        ReverseTapeView<T> tape,
        int index,
        Span<T> gradients,
        in T increment,
        TOps ops)
        where TOps : struct, IStatusRingOps<T>
    {
        if (index < 0 || index >= tape.Count)
            return AlgebraStatus.InvalidInput;
        return tape[index].OpCode == ReverseOpCode.Constant
            ? AlgebraStatus.Ok
            : AddTo(ref gradients[index], increment, ops);
    }

    private static AlgebraStatus AddTo<T, TOps>(ref T destination, in T increment, TOps ops)
        where TOps : struct, IStatusRingOps<T>
    {
        var sum = ops.Zero;
        var status = ops.TryAdd(ref sum, destination, increment);
        if (status != AlgebraStatus.Ok)
            return status;

        destination = sum;
        return AlgebraStatus.Ok;
    }

    private static bool HasUnaryInput<T>(ReverseTapeView<T> tape, ReverseNode<T> node) =>
        node.Left >= 0 && node.Left < tape.Count;

    private static bool HasBinaryInputs<T>(ReverseTapeView<T> tape, ReverseNode<T> node) =>
        HasUnaryInput(tape, node) && node.Right >= 0 && node.Right < tape.Count;
}
