using System;
using HPD.Math.Core;
using HPD.Math.Finite;

namespace HPD.Math.Tests;

public sealed class OrderLatticeKernelTests
{
    [Fact]
    public void CompleteFiniteLatticeKernels_FoldFiniteListView()
    {
        Span<bool> items = [false, true, false];
        var values = new FiniteListView<bool>(items);
        var result = false;
        var ops = new BoolAlgebraOps();

        Assert.Equal(AlgebraStatus.Ok, CompleteFiniteLatticeKernels.TrySupremum(values, ref result, ops));
        Assert.True(result);

        Assert.Equal(AlgebraStatus.Ok, CompleteFiniteLatticeKernels.TryInfimum(values, ref result, ops));
        Assert.False(result);
    }

    [Fact]
    public void GeneratedFinitePowerSet_ComputesBooleanAlgebraOverFiniteUniverse()
    {
        var ops = new TestBoolPowerSet.Ops();

        Assert.True(ops.Top.ContainsIndex(0));
        Assert.True(ops.Top.ContainsIndex(1));
        Assert.Equal(default, ops.Bottom);

        Assert.Equal(AlgebraStatus.Ok, TestBoolPowerSet.TrySingletonIndex(0, out var left));
        Assert.Equal(AlgebraStatus.Ok, TestBoolPowerSet.TrySingletonIndex(1, out var right));
        var result = default(TestBoolPowerSet.Set);

        ops.Join(ref result, left, right);
        Assert.Equal(ops.Top, result);

        ops.Meet(ref result, left, ops.Top);
        Assert.Equal(left, result);

        ops.Complement(ref result, left);
        Assert.Equal(right, result);

        Assert.True(ops.LessEqual(left, ops.Top));
        Assert.False(ops.LessEqual(ops.Top, left));
    }

    [Fact]
    public void GeneratedFinitePowerSet_ComputesSupremumAndInfimum()
    {
        var ops = new TestBoolPowerSet.Ops();
        Assert.Equal(AlgebraStatus.Ok, TestBoolPowerSet.TrySingletonIndex(0, out var left));
        Assert.Equal(AlgebraStatus.Ok, TestBoolPowerSet.TrySingletonIndex(1, out var right));
        var top = TestBoolPowerSet.Top;
        Span<TestBoolPowerSet.Set> values = [left, top, right];

        var result = default(TestBoolPowerSet.Set);

        Assert.Equal(AlgebraStatus.Ok, ops.TrySupremum(ref result, values));
        Assert.Equal(top, result);

        Assert.Equal(AlgebraStatus.Ok, ops.TryInfimum(ref result, values));
        Assert.Equal(default, result);
    }

    [Fact]
    public void GeneratedFinitePowerSet_ConstructsFromIndices()
    {
        var status = TestBoolPowerSet.TrySingletonIndex(1, out var singleton);
        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.True(singleton.ContainsIndex(1));
        Assert.False(singleton.ContainsIndex(0));

        status = TestBoolPowerSet.TryFromIndices([0, 1], out var top);
        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(TestBoolPowerSet.Top, top);

        status = TestBoolPowerSet.TrySingletonIndex(2, out _);
        Assert.Equal(AlgebraStatus.InvalidInput, status);
    }

    [Fact]
    public void GeneratedFinitePowerSet_CoversLargeFirstClassValuePowersets()
    {
        var ops = new TestLargePowerSet.Ops();

        Assert.True(ops.Top.ContainsIndex(199));
        Assert.Equal(default, ops.Bottom);

        Assert.Equal(AlgebraStatus.Ok, TestLargePowerSet.TrySingletonIndex(0, out var low));
        Assert.Equal(AlgebraStatus.Ok, TestLargePowerSet.TrySingletonIndex(199, out var high));
        var result = default(TestLargePowerSet.Set);

        ops.Join(ref result, low, high);
        Assert.True(result.ContainsIndex(0));
        Assert.True(result.ContainsIndex(199));

        ops.Complement(ref result, low);
        Assert.True(result.ContainsIndex(199));
        Assert.False(result.ContainsIndex(0));

        Span<TestLargePowerSet.Set> values = [low, high];
        Assert.Equal(AlgebraStatus.Ok, ops.TrySupremum(ref result, values));
        Assert.True(result.ContainsIndex(0));
        Assert.True(result.ContainsIndex(199));
    }

    [Fact]
    public void OrderHomomorphismKernels_ValidateMonotoneWitnesses()
    {
        Assert.Equal(
            AlgebraStatus.Ok,
            OrderHomomorphismKernels.TryValidateMonotone<bool, bool, IdentityBoolHomOps, BoolAlgebraOps, BoolAlgebraOps, BoolAlgebraOps>(
                new IdentityBoolHomOps(),
                new BoolAlgebraOps(),
                new BoolAlgebraOps(),
                new BoolAlgebraOps()));

        Assert.Equal(
            AlgebraStatus.InvalidInput,
            OrderHomomorphismKernels.TryValidateMonotone<bool, bool, NotBoolHomOps, BoolAlgebraOps, BoolAlgebraOps, BoolAlgebraOps>(
                new NotBoolHomOps(),
                new BoolAlgebraOps(),
                new BoolAlgebraOps(),
                new BoolAlgebraOps()));
    }

    private readonly struct IdentityBoolHomOps : IOrderHomOps<bool, bool>
    {
        public void Apply(ref bool destination, in bool source) => destination = source;
    }

    private readonly struct NotBoolHomOps : IOrderHomOps<bool, bool>
    {
        public void Apply(ref bool destination, in bool source) => destination = !source;
    }

}

[FinitePowerSetContext(2)]
public readonly partial struct TestBoolPowerSet;

[FinitePowerSetContext(200)]
public readonly partial struct TestLargePowerSet;
