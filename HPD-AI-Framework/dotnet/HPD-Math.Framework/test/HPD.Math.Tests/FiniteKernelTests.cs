using System;
using HPD.Math.Core;
using HPD.Math.Finite;

namespace HPD.Math.Tests;

public sealed class FiniteKernelTests
{
    [Fact]
    public void FiniteListKernels_MapAndFoldWithoutDelegates()
    {
        ReadOnlySpan<int> values = [1, 2, 3];
        Span<int> mappedItems = stackalloc int[3];
        var mapped = new FiniteListBuilder<int>(mappedItems);

        var status = FiniteListKernels.TryMap(
            new FiniteListView<int>(values),
            ref mapped,
            new DoubleIntMapOps());

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(6, mapped.AsView()[2]);

        var sum = 0;
        FiniteListKernels.Fold(mapped.AsView(), ref sum, new IntSumFoldOps());
        Assert.Equal(12, sum);
    }

    [Fact]
    public void FinsetKernels_MergeCanonicalSortedSets()
    {
        ReadOnlySpan<int> left = [1, 3, 5];
        ReadOnlySpan<int> right = [3, 4];
        Span<int> unionItems = stackalloc int[5];
        Span<int> interItems = stackalloc int[2];
        Span<int> exceptItems = stackalloc int[3];
        var union = new FinsetBuilder<int>(unionItems);
        var inter = new FinsetBuilder<int>(interItems);
        var except = new FinsetBuilder<int>(exceptItems);

        Assert.Equal(AlgebraStatus.Ok, FinsetKernels.TryUnion(new FinsetView<int>(left), new FinsetView<int>(right), ref union, new Int32OrderOps()));
        Assert.Equal(AlgebraStatus.Ok, FinsetKernels.TryIntersect(new FinsetView<int>(left), new FinsetView<int>(right), ref inter, new Int32OrderOps()));
        Assert.Equal(AlgebraStatus.Ok, FinsetKernels.TryExcept(new FinsetView<int>(left), new FinsetView<int>(right), ref except, new Int32OrderOps()));

        Assert.Equal(4, union.Count);
        Assert.Equal(4, union.AsView()[2]);
        Assert.Equal(1, inter.Count);
        Assert.Equal(3, inter.AsView()[0]);
        Assert.Equal(2, except.Count);
        Assert.Equal(5, except.AsView()[1]);
    }

    [Fact]
    public void MultisetKernels_MergeCounts()
    {
        ReadOnlySpan<int> leftElements = [1, 3];
        ReadOnlySpan<int> leftCounts = [2, 1];
        ReadOnlySpan<int> rightElements = [1, 2, 3];
        ReadOnlySpan<int> rightCounts = [1, 4, 5];
        Span<int> destinationElements = stackalloc int[3];
        Span<int> destinationCounts = stackalloc int[3];
        var destination = new MultisetBuilder<int>(destinationElements, destinationCounts);

        var status = MultisetKernels.TrySum(
            new MultisetView<int>(leftElements, leftCounts),
            new MultisetView<int>(rightElements, rightCounts),
            ref destination,
            new Int32OrderOps());

        var result = destination.AsView();
        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(3, result.Count);
        Assert.Equal(3, result.CountAt(0));
        Assert.Equal(4, result.CountAt(1));
        Assert.Equal(6, result.CountAt(2));
    }

    [Fact]
    public void FinsuppKernels_MapRemapAndFold()
    {
        ReadOnlySpan<int> keys = [0, 1, 2];
        ReadOnlySpan<int> values = [1, 2, -1];
        Span<int> mappedKeys = stackalloc int[3];
        Span<int> mappedValues = stackalloc int[3];
        var mapped = new FinsuppBuilder<int, int>(mappedKeys, mappedValues);

        var status = FinsuppKernels.TryMapValues(
            new FinsuppView<int, int>(keys, values),
            ref mapped,
            new Int32OrderOps(),
            new CheckedInt32RingOps(),
            new CheckedInt32RingOps(),
            new DoubleIntMapOps());

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(4, mapped.AsView().Values[1]);

        Span<int> remappedKeys = stackalloc int[2];
        Span<int> remappedValues = stackalloc int[2];
        Span<int> workspaceKeys = stackalloc int[3];
        Span<int> workspaceValues = stackalloc int[3];
        var remapped = new FinsuppBuilder<int, int>(remappedKeys, remappedValues);

        status = FinsuppKernels.TryRemapKeys(
            new FinsuppView<int, int>(keys, values),
            ref remapped,
            workspaceKeys,
            workspaceValues,
            new Int32OrderOps(),
            new Int32OrderOps(),
            new CheckedInt32RingOps(),
            new Mod2KeyMapOps());

        var remappedView = remapped.AsView();
        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(1, remappedView.Count);
        Assert.Equal(1, remappedView.Keys[0]);
        Assert.Equal(2, remappedView.Values[0]);

        var weighted = 0;
        FinsuppKernels.Fold(new FinsuppView<int, int>(keys, values), ref weighted, new WeightedFinsuppFoldOps());
        Assert.Equal(0, weighted);
    }

    [Fact]
    public void FiniteEnumerationKernels_FillEnumerationAsList()
    {
        Span<bool> values = stackalloc bool[2];
        var status = FiniteEnumerationKernels.TryFill(values, new BoolAlgebraOps());
        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.False(values[0]);
        Assert.True(values[1]);

        Span<bool> listItems = stackalloc bool[2];
        var list = new FiniteListBuilder<bool>(listItems);
        status = FiniteEnumerationKernels.TryAsList(ref list, new BoolAlgebraOps());
        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.True(FiniteEnumerationKernels.Contains(true, new BoolAlgebraOps()));
        Assert.Equal(1, FiniteEnumerationKernels.IndexOf(true, new BoolAlgebraOps()));
    }

    private readonly struct DoubleIntMapOps : IMapOps<int, int>
    {
        public void Map(ref int destination, in int input) => destination = checked(input * 2);
    }

    private readonly struct Mod2KeyMapOps : IMapOps<int, int>
    {
        public void Map(ref int destination, in int input) => destination = input % 2;
    }

    private readonly struct IntSumFoldOps : IListFoldOps<int, int>
    {
        public void Step(ref int accumulator, in int element) => accumulator = checked(accumulator + element);
    }

    private readonly struct WeightedFinsuppFoldOps : IFinsuppFoldOps<int, int, int>
    {
        public void Step(ref int accumulator, in int key, in int value) =>
            accumulator = checked(accumulator + key * value);
    }
}
