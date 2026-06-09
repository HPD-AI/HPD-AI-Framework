using System;
using HPD.Math.Core;
using HPD.Math.Finite;

namespace HPD.Math.Tests;

public sealed class FinsuppKernelTests
{
    [Fact]
    public void ValidateCanonical_RejectsExplicitZeroAndUnsortedKeys()
    {
        ReadOnlySpan<int> zeroKeys = [0, 1];
        ReadOnlySpan<int> zeroValues = [2, 0];
        var withZero = new FinsuppView<int, int>(zeroKeys, zeroValues);

        Assert.Equal(
            AlgebraStatus.InvalidInput,
            withZero.ValidateCanonical(new Int32OrderOps(), new CheckedInt32RingOps()));

        ReadOnlySpan<int> unsortedKeys = [2, 1];
        ReadOnlySpan<int> unsortedValues = [2, 3];
        var unsorted = new FinsuppView<int, int>(unsortedKeys, unsortedValues);

        Assert.Equal(
            AlgebraStatus.InvalidInput,
            unsorted.ValidateCanonical(new Int32OrderOps(), new CheckedInt32RingOps()));
    }

    [Fact]
    public void TryAdd_MergesSortedSupportAndDropsZeroSums()
    {
        ReadOnlySpan<int> leftKeys = [0, 2];
        ReadOnlySpan<int> leftValues = [3, 5];
        ReadOnlySpan<int> rightKeys = [1, 2];
        ReadOnlySpan<int> rightValues = [7, -5];

        Span<int> destinationKeys = stackalloc int[4];
        Span<int> destinationValues = stackalloc int[4];
        var destination = new FinsuppBuilder<int, int>(destinationKeys, destinationValues);

        var status = new FinsuppView<int, int>(leftKeys, leftValues).TryAdd(
            new FinsuppView<int, int>(rightKeys, rightValues),
            ref destination,
            new Int32OrderOps(),
            new CheckedInt32RingOps());

        Assert.Equal(AlgebraStatus.Ok, status);

        var result = destination.AsView();
        Assert.Equal(2, result.Count);
        Assert.Equal(0, result.Keys[0]);
        Assert.Equal(3, result.Values[0]);
        Assert.Equal(1, result.Keys[1]);
        Assert.Equal(7, result.Values[1]);
    }
}
