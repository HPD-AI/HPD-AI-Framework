namespace HPD.Events.Struct;

/// <summary>
/// Direct reader subscription for a struct event route.
/// </summary>
/// <remarks>
/// The subscription is a reference handle over one route registration. Disposal is
/// idempotent and removes that registration from the route.
/// </remarks>
public sealed class StructEventSubscription<TEvent> : IDisposable
    where TEvent : struct, IStructEvent
{
    private readonly StructEventRoute<TEvent>? _route;
    private readonly StructEventSubscriber<TEvent>? _subscriber;
    private int _disposed;

    internal StructEventSubscription(
        StructEventRoute<TEvent> route,
        StructEventSubscriber<TEvent> subscriber)
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
        if (Interlocked.Exchange(ref _disposed, 1) == 0 &&
            _route is not null &&
            _subscriber is not null)
        {
            _route.RemoveSubscriber(_subscriber.Id);
        }
    }
}
