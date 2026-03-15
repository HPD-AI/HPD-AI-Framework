using Rhodium.Data;

namespace Rhodium.Data.Tests;

public class ShadowBufferTests
{
    [Fact]
    public void ShadowBuffer_ReturnsEmptyDuringWarmup()
    {
        var buffer = new ShadowBuffer(lookback: 5);

        // Before warmup
        buffer.Push(1.0);
        buffer.Push(2.0);
        buffer.Push(3.0);
        buffer.Push(4.0);

        var window = buffer.GetWindow();

        Assert.True(window.IsEmpty);
    }

    [Fact]
    public void ShadowBuffer_ReturnsWindowAfterWarmup()
    {
        var buffer = new ShadowBuffer(lookback: 5);

        // Push 5 values to complete warmup
        buffer.Push(1.0);
        buffer.Push(2.0);
        buffer.Push(3.0);
        buffer.Push(4.0);
        buffer.Push(5.0);

        var window = buffer.GetWindow();

        Assert.Equal(5, window.Length);
        Assert.Equal(1.0, window[0]);
        Assert.Equal(2.0, window[1]);
        Assert.Equal(3.0, window[2]);
        Assert.Equal(4.0, window[3]);
        Assert.Equal(5.0, window[4]);
    }

    [Fact]
    public void ShadowBuffer_MaintainsContiguousWindow()
    {
        var buffer = new ShadowBuffer(lookback: 3);

        // Warmup
        buffer.Push(10.0);
        buffer.Push(20.0);
        buffer.Push(30.0);

        // Add more values
        buffer.Push(40.0);
        buffer.Push(50.0);

        var window = buffer.GetWindow();

        // Should have last 3 values
        Assert.Equal(3, window.Length);
        Assert.Equal(30.0, window[0]);
        Assert.Equal(40.0, window[1]);
        Assert.Equal(50.0, window[2]);
    }

    [Fact]
    public void ShadowBuffer_HandlesLinearRewind()
    {
        var buffer = new ShadowBuffer(lookback: 3);

        // Push enough values to trigger rewind (3 * 2 = 6 capacity)
        for (int i = 1; i <= 10; i++)
        {
            buffer.Push(i * 10.0);
        }

        var window = buffer.GetWindow();

        // Should have last 3 values
        Assert.Equal(3, window.Length);
        Assert.Equal(80.0, window[0]);
        Assert.Equal(90.0, window[1]);
        Assert.Equal(100.0, window[2]);
    }

    [Fact]
    public void ShadowBuffer_ProvidesSlidingWindow()
    {
        var buffer = new ShadowBuffer(lookback: 4);

        // Warmup with initial values
        buffer.Push(1.0);
        buffer.Push(2.0);
        buffer.Push(3.0);
        buffer.Push(4.0);

        var window1 = buffer.GetWindow();
        Assert.Equal(new double[] { 1.0, 2.0, 3.0, 4.0 }, window1.ToArray());

        // Push new value and check window slides
        buffer.Push(5.0);
        var window2 = buffer.GetWindow();
        Assert.Equal(new double[] { 2.0, 3.0, 4.0, 5.0 }, window2.ToArray());

        buffer.Push(6.0);
        var window3 = buffer.GetWindow();
        Assert.Equal(new double[] { 3.0, 4.0, 5.0, 6.0 }, window3.ToArray());
    }

    [Fact]
    public void ShadowBuffer_ZeroAllocationAccess()
    {
        var buffer = new ShadowBuffer(lookback: 10);

        // Warmup
        for (int i = 0; i < 10; i++)
            buffer.Push(i);

        // Multiple accesses should return spans over the same memory
        var window1 = buffer.GetWindow();
        var window2 = buffer.GetWindow();

        // Both spans should reference the same underlying data
        Assert.Equal(window1.Length, window2.Length);
        for (int i = 0; i < window1.Length; i++)
        {
            Assert.Equal(window1[i], window2[i]);
        }
    }

    [Fact]
    public void ShadowBuffer_LargeBufferStressTest()
    {
        var buffer = new ShadowBuffer(lookback: 100);

        // Push many values beyond double buffer size
        for (int i = 0; i < 500; i++)
        {
            buffer.Push(i);
        }

        var window = buffer.GetWindow();

        // Should have last 100 values
        Assert.Equal(100, window.Length);
        Assert.Equal(400.0, window[0]);
        Assert.Equal(499.0, window[99]);
    }
}
