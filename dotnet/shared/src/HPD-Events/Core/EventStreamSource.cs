using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace HPD.Events.Core;

/// <summary>
/// Default live event stream source backed by HPD.Events inbox subscriptions.
/// </summary>
/// <typeparam name="TEvent">Event type yielded by the stream.</typeparam>
public sealed class EventStreamSource<TEvent> : IEventStreamSource<TEvent>
    where TEvent : Event
{
    private readonly IEventInboxSource _inboxes;

    /// <summary>Create a stream source over an inbox provider.</summary>
    public EventStreamSource(IEventInboxSource inboxes)
    {
        _inboxes = inboxes ?? throw new ArgumentNullException(nameof(inboxes));
    }

    /// <inheritdoc />
    public ValueTask<AsyncStreamOpenResult<AsyncStream<TEvent>>> OpenAsync(
        EventStreamRequest<TEvent> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromResult(AsyncStreamOpenResult<AsyncStream<TEvent>>.Failed(
                AsyncStreamOpenStatus.Cancelled,
                new AsyncStreamError
                {
                    Code = "event.stream.cancelled",
                    Message = "The event stream open operation was cancelled.",
                    Category = AsyncStreamErrorCategory.Cancellation
                }));
        }

        if (request.Capacity <= 0)
        {
            return ValueTask.FromResult(AsyncStreamOpenResult<AsyncStream<TEvent>>.Failed(
                AsyncStreamOpenStatus.ValidationFailed,
                new AsyncStreamError
                {
                    Code = "event.stream.capacity.invalid",
                    Message = "Event stream capacity must be greater than zero.",
                    Target = nameof(EventStreamRequest<TEvent>.Capacity),
                    Category = AsyncStreamErrorCategory.Validation
                }));
        }

        var fullMode = MapBackpressure(request.Backpressure);
        if (fullMode is null)
        {
            return ValueTask.FromResult(AsyncStreamOpenResult<AsyncStream<TEvent>>.Failed(
                AsyncStreamOpenStatus.ValidationFailed,
                new AsyncStreamError
                {
                    Code = "event.stream.backpressure.invalid",
                    Message = "The requested event stream backpressure mode is not supported.",
                    Target = nameof(EventStreamRequest<TEvent>.Backpressure),
                    Category = AsyncStreamErrorCategory.Validation
                }));
        }

        var inbox = _inboxes.CreateInbox<TEvent>(new EventInboxOptions
        {
            Capacity = request.Capacity,
            FullMode = fullMode.Value,
            IncludeDerivedTypes = request.IncludeDerivedTypes,
            Channel = request.Channel
        });

        var stream = new AsyncStream<TEvent>
        {
            Items = ReadAndDisposeAsync(inbox, cancellationToken),
            Descriptor = new AsyncStreamDescriptor
            {
                StreamId = request.StreamId,
                Replayable = false,
                Resumable = false,
                Backpressure = request.Backpressure,
                DeliveryGuarantee = AsyncStreamDeliveryGuarantee.AtMostOnce
            }
        };

        return ValueTask.FromResult(AsyncStreamOpenResult<AsyncStream<TEvent>>.Opened(stream));
    }

    private static BoundedChannelFullMode? MapBackpressure(AsyncStreamBackpressureMode mode) =>
        mode switch
        {
            AsyncStreamBackpressureMode.Wait => BoundedChannelFullMode.Wait,
            AsyncStreamBackpressureMode.DropOldest => BoundedChannelFullMode.DropOldest,
            AsyncStreamBackpressureMode.DropNewest => BoundedChannelFullMode.DropNewest,
            AsyncStreamBackpressureMode.DropWrite => BoundedChannelFullMode.DropWrite,
            AsyncStreamBackpressureMode.LatestOnly => BoundedChannelFullMode.DropOldest,
            _ => null
        };

    private static async IAsyncEnumerable<TEvent> ReadAndDisposeAsync(
        EventInbox<TEvent> inbox,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (inbox.ConfigureAwait(false))
        {
            await foreach (var evt in inbox.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return evt;
        }
    }
}
