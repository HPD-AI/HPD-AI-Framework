using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HPD.Base.Realtime.Feeds;

namespace HPD.Base.Realtime.AspNetCore.Tests.Endpoints;

internal sealed class TrackingRealtimeFeedSource : IBaseRealtimeFeedSource
{
    private readonly Channel<BaseRealtimeEvent> _events = Channel.CreateUnbounded<BaseRealtimeEvent>();
    private int _openCount;

    public int OpenCount => Volatile.Read(ref _openCount);
    public bool Replayable { get; init; }
    public bool Resumable { get; init; }
    public string? Cursor { get; init; }

    public TaskCompletionSource EnumerationStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource EnumerationStopped { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Fail() =>
        _events.Writer.TryComplete(new InvalidOperationException("adversarial-feed-failure"));

    public bool Emit(BaseRealtimeEvent realtimeEvent) =>
        _events.Writer.TryWrite(realtimeEvent);

    public ValueTask<AsyncStreamOpenResult<AsyncStream<BaseRealtimeEvent>>> OpenAsync(
        BaseRealtimeFeedRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _openCount);
        return ValueTask.FromResult(AsyncStreamOpenResult<AsyncStream<BaseRealtimeEvent>>.Opened(
            new AsyncStream<BaseRealtimeEvent>
            {
                Descriptor = new AsyncStreamDescriptor
                {
                    StreamId = "test",
                    Replayable = Replayable,
                    Resumable = Resumable,
                    Cursor = Cursor,
                    DeliveryGuarantee = Replayable
                        ? AsyncStreamDeliveryGuarantee.AtLeastOnce
                        : AsyncStreamDeliveryGuarantee.AtMostOnce
                },
                Items = ReadAsync(cancellationToken)
            }));
    }

    private async IAsyncEnumerable<BaseRealtimeEvent> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnumerationStarted.TrySetResult();
        try
        {
            await foreach (var item in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally
        {
            EnumerationStopped.TrySetResult();
        }
    }
}
