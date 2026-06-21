namespace HPD.ML.TimeSeries.Tests;

public class SlidingWindowTests
{
    [Fact]
    public void Push_SingleElement_CountIsOne()
    {
        var window = new SlidingWindow<double>(5);
        window.Push(42);
        Assert.Equal(1, window.Count);
        Assert.False(window.IsFull);
    }

    [Fact]
    public void Push_FillToCapacity_IsFull()
    {
        var window = new SlidingWindow<double>(3);
        window.Push(1); window.Push(2); window.Push(3);
        Assert.True(window.IsFull);
        Assert.Equal(3, window.Count);
    }

    [Fact]
    public void Push_OverCapacity_OverwritesOldest()
    {
        var window = new SlidingWindow<double>(3);
        for (int i = 1; i <= 5; i++) window.Push(i);
        Assert.Equal(3, window.Count);
        Assert.Equal(3, window[0]);
        Assert.Equal(4, window[1]);
        Assert.Equal(5, window[2]);
    }

    [Fact]
    public void Indexer_ReturnsInsertionOrder()
    {
        var window = new SlidingWindow<double>(4);
        window.Push(10); window.Push(20); window.Push(30);
        Assert.Equal(10, window[0]);
        Assert.Equal(20, window[1]);
        Assert.Equal(30, window[2]);
    }

    [Fact]
    public void Indexer_OutOfRange_Throws()
    {
        var window = new SlidingWindow<double>(3);
        window.Push(1); window.Push(2);
        Assert.Throws<ArgumentOutOfRangeException>(() => window[2]);
        Assert.Throws<ArgumentOutOfRangeException>(() => window[-1]);
    }

    [Fact]
    public void CopyTo_CopiesInOrder()
    {
        var window = new SlidingWindow<double>(4);
        window.Push(1); window.Push(2); window.Push(3); window.Push(4);
        var span = new double[4];
        window.CopyTo(span);
        Assert.Equal([1, 2, 3, 4], span);
    }

    [Fact]
    public void Clone_IndependentCopy()
    {
        var window = new SlidingWindow<double>(3);
        window.Push(1); window.Push(2);
        var clone = window.Clone();
        window.Push(99);
        Assert.Equal(2, clone[1]);
        Assert.Equal(2, clone.Count);
    }

    [Fact]
    public void Constructor_ZeroCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlidingWindow<double>(0));
    }
}
