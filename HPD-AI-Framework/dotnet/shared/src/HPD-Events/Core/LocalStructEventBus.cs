using System.Collections.Concurrent;

namespace HPD.Events;

/// <summary>Default process-local struct event route registry.</summary>
public sealed class LocalStructEventBus : ILocalStructEventBus, IDisposable
{
    private readonly ConcurrentDictionary<Type, object> _routes = new();
    private int _disposed;

    /// <inheritdoc />
    public LocalStructEventRoute<TEvent> Route<TEvent>()
        where TEvent : struct, IStructEvent
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        return (LocalStructEventRoute<TEvent>)_routes.GetOrAdd(
            typeof(TEvent),
            static _ => new LocalStructEventRoute<TEvent>());
    }

    /// <inheritdoc />
    public IReadOnlyList<LocalStructEventTypeStats> GetRouteStats()
    {
        var stats = new List<LocalStructEventTypeStats>(_routes.Count);
        foreach (var route in _routes.Values)
            stats.Add(GetRouteStats(route));

        return stats;
    }

    /// <inheritdoc />
    public LocalStructEventBusStats GetStats()
    {
        var routeCount = 0;
        var maxQueued = 0;
        var subscriberCount = 0;
        var inboxCount = 0;
        var observerCount = 0;
        var currentQueued = 0;
        long emitted = 0;
        long accepted = 0;
        long dropped = 0;
        long filtered = 0;
        long subscriberWrites = 0;
        long subscriberDrops = 0;

        foreach (var route in _routes.Values)
        {
            var stats = GetRouteStats(route);
            routeCount++;
            subscriberCount += stats.SubscriberCount;
            inboxCount += stats.InboxCount;
            observerCount += stats.ObserverCount;
            currentQueued += stats.CurrentQueued;
            maxQueued = Math.Max(maxQueued, stats.MaxQueued);
            emitted += stats.Emitted;
            accepted += stats.Accepted;
            dropped += stats.Dropped;
            filtered += stats.Filtered;
            subscriberWrites += stats.SubscriberWrites;
            subscriberDrops += stats.SubscriberDrops;
        }

        return new LocalStructEventBusStats(
            routeCount,
            subscriberCount,
            inboxCount,
            observerCount,
            currentQueued,
            maxQueued,
            emitted,
            accepted,
            dropped,
            filtered,
            subscriberWrites,
            subscriberDrops);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        foreach (var route in _routes.Values)
            DisposeRoute(route);

        _routes.Clear();
    }

    private static LocalStructEventTypeStats GetRouteStats(object route)
    {
        if (route is ILocalStructEventRoute localRoute)
            return localRoute.GetUntypedStats();

        throw new InvalidOperationException("Unknown local struct route type.");
    }

    private static void DisposeRoute(object route)
    {
        if (route is IDisposable disposable)
            disposable.Dispose();
    }
}
