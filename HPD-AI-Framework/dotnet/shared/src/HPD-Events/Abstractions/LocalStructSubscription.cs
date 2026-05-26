using HPD.Events.Core;

namespace HPD.Events;

/// <summary>Direct reader subscription for a local struct route.</summary>
public readonly struct LocalStructSubscription<TEvent> : IDisposable
    where TEvent : struct, IStructEvent
{
    private readonly LocalStructEventRoute<TEvent>? _route;
    private readonly LocalStructSubscriber<TEvent>? _subscriber;

    internal LocalStructSubscription(
        LocalStructEventRoute<TEvent> route,
        LocalStructSubscriber<TEvent> subscriber)
    {
        _route = route;
        _subscriber = subscriber;
    }

    /// <summary>Try to read one event.</summary>
    public bool TryRead(out TEvent evt)
    {
        if (_subscriber is not null && _subscriber.TryRead(out evt))
            return true;

        evt = default;
        return false;
    }

    /// <summary>Read up to <paramref name="destination" />.Length events.</summary>
    public int TryReadBatch(Span<TEvent> destination) =>
        _subscriber?.TryReadBatch(destination) ?? 0;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_route is not null && _subscriber is not null)
            _route.RemoveSubscriber(_subscriber.Id);
    }
}
