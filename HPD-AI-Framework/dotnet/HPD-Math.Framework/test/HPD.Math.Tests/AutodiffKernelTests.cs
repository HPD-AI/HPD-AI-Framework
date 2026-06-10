using System;
using HPD.Math.Autodiff;
using HPD.Math.Core;
using HPD.Math.Numerics;

namespace HPD.Math.Tests;

public sealed class AutodiffKernelTests
{
    [Fact]
    public void DualStatusFieldOps_ComputeForwardDerivative()
    {
        var ops = new DualStatusFieldOps<Rational32, Rational32StatusFieldOps>();
        var x = new Dual<Rational32>(new Rational32(2, 1), Rational32.One);
        var three = new Dual<Rational32>(new Rational32(3, 1), Rational32.Zero);
        var xSquared = ops.Zero;
        var threeX = ops.Zero;
        var result = ops.Zero;

        Assert.Equal(AlgebraStatus.Ok, ops.TryMul(ref xSquared, x, x));
        Assert.Equal(AlgebraStatus.Ok, ops.TryMul(ref threeX, three, x));
        Assert.Equal(AlgebraStatus.Ok, ops.TryAdd(ref result, xSquared, threeX));

        Assert.Equal(new Rational32(10, 1), result.Primal);
        Assert.Equal(new Rational32(7, 1), result.Tangent);
    }

    [Fact]
    public void ReverseTapeKernels_ComputeScalarDerivativeWithoutClosures()
    {
        var ops = new Rational32StatusFieldOps();
        Span<ReverseNode<Rational32>> nodes = stackalloc ReverseNode<Rational32>[8];
        Span<Rational32> gradients = stackalloc Rational32[8];
        var tape = new ReverseTapeBuilder<Rational32>(nodes);

        Assert.Equal(AlgebraStatus.Ok, tape.TryInput(new Rational32(2, 1), out var x));
        Assert.Equal(AlgebraStatus.Ok, tape.TryConstant(new Rational32(3, 1), out var three));
        Assert.Equal(AlgebraStatus.Ok, tape.TryMul(x, x, ops, out var xSquared));
        Assert.Equal(AlgebraStatus.Ok, tape.TryMul(three, x, ops, out var threeX));
        Assert.Equal(AlgebraStatus.Ok, tape.TryAdd(xSquared, threeX, ops, out var output));

        var status = ReverseTapeKernels.TryBackward(tape.AsView(), output.Index, gradients, ops);

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(new Rational32(10, 1), output.Value);
        Assert.Equal(new Rational32(7, 1), gradients[x.Index]);
        Assert.Equal(Rational32.Zero, gradients[three.Index]);
    }

    [Fact]
    public void ReverseTapeKernels_AccumulateSharedSubexpressions()
    {
        var ops = new Rational32StatusFieldOps();
        Span<ReverseNode<Rational32>> nodes = stackalloc ReverseNode<Rational32>[4];
        Span<Rational32> gradients = stackalloc Rational32[4];
        var tape = new ReverseTapeBuilder<Rational32>(nodes);

        Assert.Equal(AlgebraStatus.Ok, tape.TryInput(new Rational32(2, 1), out var x));
        Assert.Equal(AlgebraStatus.Ok, tape.TryMul(x, x, ops, out var y));
        Assert.Equal(AlgebraStatus.Ok, tape.TryAdd(y, y, ops, out var z));

        Assert.Equal(AlgebraStatus.Ok, ReverseTapeKernels.TryBackward(tape.AsView(), z.Index, gradients, ops));
        Assert.Equal(new Rational32(8, 1), z.Value);
        Assert.Equal(new Rational32(8, 1), gradients[x.Index]);
    }

    [Fact]
    public void ReverseTapeKernels_ComputeTwoInputGradient()
    {
        var ops = new Rational32StatusFieldOps();
        Span<ReverseNode<Rational32>> nodes = stackalloc ReverseNode<Rational32>[5];
        Span<Rational32> gradients = stackalloc Rational32[5];
        var tape = new ReverseTapeBuilder<Rational32>(nodes);

        Assert.Equal(AlgebraStatus.Ok, tape.TryInput(new Rational32(2, 1), out var x));
        Assert.Equal(AlgebraStatus.Ok, tape.TryInput(new Rational32(5, 1), out var y));
        Assert.Equal(AlgebraStatus.Ok, tape.TryMul(x, y, ops, out var xy));
        Assert.Equal(AlgebraStatus.Ok, tape.TryAdd(xy, x, ops, out var output));

        Assert.Equal(AlgebraStatus.Ok, ReverseTapeKernels.TryBackward(tape.AsView(), output.Index, gradients, ops));
        Assert.Equal(new Rational32(12, 1), output.Value);
        Assert.Equal(new Rational32(6, 1), gradients[x.Index]);
        Assert.Equal(new Rational32(2, 1), gradients[y.Index]);
    }

    [Fact]
    public void ReverseTapeBuilder_ReportsCapacityAndArithmeticFailureAsStatus()
    {
        var ops = new Rational32StatusFieldOps();
        Span<ReverseNode<Rational32>> nodes = stackalloc ReverseNode<Rational32>[1];
        var tape = new ReverseTapeBuilder<Rational32>(nodes);

        Assert.Equal(AlgebraStatus.Ok, tape.TryInput(new Rational32(1, 1), out _));
        Assert.Equal(
            AlgebraStatus.InsufficientDestination,
            tape.TryConstant(new Rational32(2, 1), out _));

        Span<ReverseNode<Rational32>> overflowNodes = stackalloc ReverseNode<Rational32>[3];
        var overflowTape = new ReverseTapeBuilder<Rational32>(overflowNodes);
        Assert.Equal(AlgebraStatus.Ok, overflowTape.TryInput(new Rational32(int.MaxValue, 1), out var left));
        Assert.Equal(AlgebraStatus.Ok, overflowTape.TryConstant(Rational32.One, out var right));
        Assert.Equal(AlgebraStatus.Overflow, overflowTape.TryAdd(left, right, ops, out _));
    }
}
