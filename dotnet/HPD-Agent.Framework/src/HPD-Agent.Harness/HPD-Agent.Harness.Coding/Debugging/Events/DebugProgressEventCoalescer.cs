using System.Threading.Channels;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal enum DebugProgressNotificationKind { Started, Updated, Completed }
internal sealed record DebugProgressNotification(DebugProgressNotificationKind Kind, DebugProgressSnapshot State);

internal sealed class DebugProgressEventCoalescer : IAsyncDisposable
{
    private readonly Channel<DebugProgressNotification> _queue;
    private readonly Func<DebugProgressNotification, ValueTask> _publish;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _consumer;
    private int _disposed;
    private long _dropped;

    public DebugProgressEventCoalescer(Func<DebugProgressNotification, ValueTask> publish, int capacity = 256)
    {
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _queue = Channel.CreateBounded<DebugProgressNotification>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });
        _consumer = ConsumeAsync();
    }

    public long DroppedNotifications => Interlocked.Read(ref _dropped);

    public bool TryEnqueue(DebugProgressNotification notification)
    {
        if (Volatile.Read(ref _disposed) == 0 && _queue.Writer.TryWrite(notification)) return true;
        Interlocked.Increment(ref _dropped);
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _queue.Writer.TryComplete();
        try { await _consumer.ConfigureAwait(false); } catch { }
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private async Task ConsumeAsync()
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(_lifetime.Token).ConfigureAwait(false))
            {
                if (!_queue.Reader.TryRead(out var notification)) continue;
                if (notification.Kind == DebugProgressNotificationKind.Updated)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(75), _lifetime.Token).ConfigureAwait(false);
                    while (_queue.Reader.TryPeek(out var next) &&
                           next.Kind == DebugProgressNotificationKind.Updated &&
                           next.State.ProgressId == notification.State.ProgressId &&
                           _queue.Reader.TryRead(out next))
                        notification = next;
                }
                try { await _publish(notification).ConfigureAwait(false); }
                catch { Interlocked.Increment(ref _dropped); }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }
}
