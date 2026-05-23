using System.Runtime.CompilerServices;

namespace HPD.Events.Core;

/// <summary>
/// Replay source backed by an async enumerable.
/// </summary>
/// <typeparam name="TEvent">Event type produced by this source.</typeparam>
public sealed class AsyncEnumerableReplaySource<TEvent> : IReplaySource<TEvent>
    where TEvent : Event
{
    private readonly IAsyncEnumerable<TEvent> _events;

    /// <summary>
    /// Create a source backed by an async enumerable.
    /// </summary>
    public AsyncEnumerableReplaySource(IAsyncEnumerable<TEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        _events = events;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TEvent> ReadAsync(
        ReplayReadOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var emitted = 0;
        await foreach (var evt in _events.WithCancellation(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            if (!ReplayTime.Matches(evt, options))
                continue;

            yield return evt;
            emitted++;

            if (options.Limit is { } limit && emitted >= limit)
                yield break;
        }
    }
}
