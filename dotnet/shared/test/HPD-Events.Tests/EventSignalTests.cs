using HPD.Events.Signals;

namespace HPD.Events.Tests;

public class EventSignalTests
{
    [Fact]
    public async Task WaitAsync_CompletesImmediately_WhenAlreadySignaled()
    {
        var signal = new EventSignal();

        signal.Signal();
        var wait = signal.WaitAsync();

        Assert.True(wait.IsCompletedSuccessfully);
        await wait;
        Assert.True(signal.TryConsume());
        Assert.False(signal.TryConsume());
    }

    [Fact]
    public async Task Signal_WakesPendingWaiters()
    {
        var signal = new EventSignal();
        var first = signal.WaitAsync().AsTask();
        var second = signal.WaitAsync().AsTask();

        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        signal.Signal();

        await Task.WhenAll(first, second);
        Assert.True(signal.TryConsume());
        Assert.False(signal.TryConsume());
    }

    [Fact]
    public async Task TryConsume_ReturnsTrueOnce_ForCoalescedSignals()
    {
        var signal = new EventSignal();

        signal.Signal();
        signal.Signal();

        Assert.True(signal.TryConsume());
        Assert.False(signal.TryConsume());

        var wait = signal.WaitAsync().AsTask();
        Assert.False(wait.IsCompleted);
        signal.Signal();
        await wait;
    }

    [Fact]
    public void TryConsume_PreservesOccurrences_InCountingMode()
    {
        var signal = new EventSignal(new EventSignalOptions
        {
            Mode = EventSignalMode.Counting
        });

        signal.Signal();
        signal.Signal();

        Assert.True(signal.TryConsume());
        Assert.True(signal.TryConsume());
        Assert.False(signal.TryConsume());
    }

    [Fact]
    public async Task Cancellation_DoesNotConsumeFutureSignal()
    {
        var signal = new EventSignal();
        using var cts = new CancellationTokenSource();
        var wait = signal.WaitAsync(cts.Token).AsTask();

        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(async () => await wait);

        signal.Signal();
        var next = signal.WaitAsync();
        Assert.True(next.IsCompletedSuccessfully);
        await next;
        Assert.True(signal.TryConsume());
    }

    [Fact]
    public async Task WaitAsync_DoesNotConsumeSignal()
    {
        var signal = new EventSignal();

        signal.Signal();
        await signal.WaitAsync();

        Assert.True(signal.TryConsume());
        Assert.False(signal.TryConsume());
    }
}
