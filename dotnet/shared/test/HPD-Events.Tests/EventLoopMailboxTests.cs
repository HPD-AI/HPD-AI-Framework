using HPD.Events.Signals;

namespace HPD.Events.Tests;

public class EventLoopMailboxTests
{
    [Fact]
    public async Task TryWrite_AcceptsItem_AndSignalsWaiter()
    {
        using var mailbox = new EventLoopMailbox<int>();
        var wait = mailbox.Signal.WaitAsync().AsTask();

        Assert.True(mailbox.TryWrite(42));

        await wait;
        Assert.True(mailbox.TryRead(out var item));
        Assert.Equal(42, item);
    }

    [Fact]
    public void TryReadBatch_ReadsQueuedItemsInOrder()
    {
        using var mailbox = new EventLoopMailbox<int>(
            new EventLoopMailboxOptions { Capacity = 4 });

        Assert.True(mailbox.TryWrite(1));
        Assert.True(mailbox.TryWrite(2));
        Assert.True(mailbox.TryWrite(3));

        Span<int> items = stackalloc int[2];
        var read = mailbox.TryReadBatch(items);

        Assert.Equal(2, read);
        Assert.Equal(1, items[0]);
        Assert.Equal(2, items[1]);
        Assert.True(mailbox.TryRead(out var remaining));
        Assert.Equal(3, remaining);
    }

    [Theory]
    [InlineData(EventLoopMailboxOverflowMode.Backpressure, false, 1)]
    [InlineData(EventLoopMailboxOverflowMode.Reject, false, 1)]
    [InlineData(EventLoopMailboxOverflowMode.DropNewest, false, 1)]
    [InlineData(EventLoopMailboxOverflowMode.DropOldest, true, 2)]
    public void TryWrite_AppliesOverflowMode(
        EventLoopMailboxOverflowMode overflowMode,
        bool expectedSecondWrite,
        int expectedValue)
    {
        using var mailbox = new EventLoopMailbox<int>(new EventLoopMailboxOptions
        {
            Capacity = 1,
            OverflowMode = overflowMode
        });

        Assert.True(mailbox.TryWrite(1));
        Assert.Equal(expectedSecondWrite, mailbox.TryWrite(2));

        Assert.True(mailbox.TryRead(out var value));
        Assert.Equal(expectedValue, value);
    }

    [Fact]
    public void GetStats_ReportsWritesDropsReadsAndDisposal()
    {
        var mailbox = new EventLoopMailbox<int>(new EventLoopMailboxOptions
        {
            Capacity = 1,
            OverflowMode = EventLoopMailboxOverflowMode.DropOldest
        });

        Assert.True(mailbox.TryWrite(1));
        Assert.True(mailbox.TryWrite(2));
        Assert.True(mailbox.TryRead(out _));

        var stats = mailbox.GetStats();
        Assert.Equal(1, stats.Capacity);
        Assert.Equal(0, stats.Count);
        Assert.Equal(2, stats.AcceptedWrites);
        Assert.Equal(1, stats.DroppedWrites);
        Assert.Equal(1, stats.Reads);
        Assert.False(stats.IsDisposed);

        mailbox.Dispose();

        Assert.True(mailbox.GetStats().IsDisposed);
        Assert.False(mailbox.TryWrite(3));
    }

    [Fact]
    public async Task DisposeAsync_ClearsMailbox()
    {
        var mailbox = new EventLoopMailbox<int>();

        Assert.True(mailbox.TryWrite(1));
        await mailbox.DisposeAsync();

        Assert.False(mailbox.TryRead(out _));
        Assert.True(mailbox.GetStats().IsDisposed);
    }
}
