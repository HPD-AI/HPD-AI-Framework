using System.Text;
using System.Threading.Channels;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal sealed record DebugOutputBatch(
    long FirstSequence,
    long LastSequence,
    DebugOutputCategory Category,
    string Text,
    long DroppedPublications);

internal sealed class DebugOutputEventCoalescer : IAsyncDisposable
{
    public const int MaximumLiveEventBytes = 16 * 1024;
    private readonly Channel<DebugOutputRecord> _queue;
    private readonly Func<DebugOutputBatch, ValueTask> _publish;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _consumer;
    private long _droppedPublications;
    private int _disposed;

    public long DroppedPublications => Interlocked.Read(ref _droppedPublications);

    public DebugOutputEventCoalescer(Func<DebugOutputBatch, ValueTask> publish, int capacity = 128)
    {
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        if (capacity is <= 0 or > 4096) throw new ArgumentOutOfRangeException(nameof(capacity));
        _queue = Channel.CreateBounded<DebugOutputRecord>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });
        _consumer = ConsumeAsync();
    }

    public bool TryEnqueue(DebugOutputRecord record)
    {
        if (Volatile.Read(ref _disposed) != 0 || !_queue.Writer.TryWrite(record))
        {
            Interlocked.Increment(ref _droppedPublications);
            return false;
        }
        return true;
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
                if (!_queue.Reader.TryRead(out var first)) continue;
                var records = new List<DebugOutputRecord> { first };
                var bytes = first.Utf8Bytes;
                await Task.Delay(TimeSpan.FromMilliseconds(40), _lifetime.Token).ConfigureAwait(false);
                while (_queue.Reader.TryPeek(out var next) && next.Category == first.Category &&
                       string.Equals(next.Group, first.Group, StringComparison.Ordinal) &&
                       bytes + next.Utf8Bytes <= MaximumLiveEventBytes && _queue.Reader.TryRead(out next))
                {
                    records.Add(next);
                    bytes += next.Utf8Bytes;
                }
                var text = string.Concat(records.Select(x => x.Text));
                try
                {
                    await _publish(new(records[0].Sequence, records[^1].Sequence, first.Category, text,
                        Interlocked.Read(ref _droppedPublications))).ConfigureAwait(false);
                }
                catch
                {
                    Interlocked.Increment(ref _droppedPublications);
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }
}
