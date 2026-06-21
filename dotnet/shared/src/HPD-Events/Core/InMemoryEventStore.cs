using System.Runtime.CompilerServices;

namespace HPD.Events.Core;

/// <summary>
/// Process-local append/read event store for tests and deterministic sessions.
/// </summary>
/// <typeparam name="TEvent">Event type stored by this store.</typeparam>
public sealed class InMemoryEventStore<TEvent> : IEventStore<TEvent>
    where TEvent : Event
{
    private readonly object _gate = new();
    private readonly List<TEvent> _events = [];

    /// <inheritdoc />
    public ValueTask AppendAsync(TEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ct.ThrowIfCancellationRequested();

        lock (_gate)
            _events.Add(evt);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TEvent> ReadAsync(
        ReplayReadOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        TEvent[] snapshot;
        lock (_gate)
            snapshot = _events.ToArray();

        var emitted = 0;
        foreach (var evt in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            if (!ReplayTime.Matches(evt, options))
                continue;

            yield return evt;
            emitted++;

            if (options.Limit is { } limit && emitted >= limit)
                yield break;

            await Task.Yield();
        }
    }
}
