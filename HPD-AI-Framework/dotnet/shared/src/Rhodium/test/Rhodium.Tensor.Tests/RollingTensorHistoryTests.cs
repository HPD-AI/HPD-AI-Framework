using Rhodium.Tensor;

namespace Rhodium.Tensor.Tests;

public sealed class RollingTensorHistoryTests
{
    [Fact]
    public void Window_ReturnsLatestValuesInChronologicalOrder()
    {
        var history = new RollingTensorHistory(8);

        for (var i = 1; i <= 5; i++)
            history.Push(i);

        var window = history.Window(3);

        Assert.False(window.IsSplit);
        Assert.Equal(3, window.Length);
        Assert.Equal(3, window[0]);
        Assert.Equal(4, window[1]);
        Assert.Equal(5, window[2]);
    }

    [Fact]
    public void Window_SpansRingBoundary()
    {
        var history = new RollingTensorHistory(5);

        for (var i = 1; i <= 7; i++)
            history.Push(i);

        var window = history.Window(4);

        Assert.True(window.IsSplit);
        Assert.Equal(4, window.Length);
        Assert.Equal(4, window[0]);
        Assert.Equal(5, window[1]);
        Assert.Equal(6, window[2]);
        Assert.Equal(7, window[3]);
    }

    [Fact]
    public void TensorWindow_ComputesStatistics()
    {
        var history = new RollingTensorHistory(4);
        history.Push(2);
        history.Push(4);
        history.Push(4);
        history.Push(4);

        var window = history.Window(4);

        Assert.Equal(14, window.Sum());
        Assert.Equal(3.5, window.Mean());
        Assert.Equal(4, window.Max());
        Assert.Equal(2, window.Min());
        Assert.Equal(Math.Sqrt(0.75), window.StdDev(), precision: 12);
    }

    [Fact]
    public void TensorWindow_CopyTo_WritesChronologicalValues()
    {
        var history = new RollingTensorHistory(3);
        history.Push(10);
        history.Push(20);
        history.Push(30);
        history.Push(40);

        Span<double> destination = stackalloc double[3];
        history.Window(3).CopyTo(destination);

        Assert.Equal(20, destination[0]);
        Assert.Equal(30, destination[1]);
        Assert.Equal(40, destination[2]);
    }

    [Fact]
    public void TensorWindow_HotPathReadsAllocateZeroBytes()
    {
        var history = new RollingTensorHistory(5);
        for (var i = 1; i <= 8; i++)
            history.Push(i);

        Span<double> destination = stackalloc double[4];
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            var window = history.Window(4);
            _ = window[0];
            _ = window.Sum();
            _ = window.Mean();
            _ = window.Max();
            _ = window.Min();
            _ = window.StdDev();
            window.CopyTo(destination);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(before, after);
    }
}
