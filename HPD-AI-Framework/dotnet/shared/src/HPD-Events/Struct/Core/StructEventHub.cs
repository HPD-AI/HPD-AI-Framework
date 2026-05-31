using System.Collections.Concurrent;

namespace HPD.Events.Struct;

/// <summary>Default process-local struct event route registry.</summary>
public sealed class StructEventHub : IStructEventHub, IDisposable
{
    private readonly ConcurrentDictionary<Type, object> _routes = new();
    private int _disposed;

    /// <inheritdoc />
    public StructEventRoute<TEvent> Route<TEvent>(
        StructEventRouteOptions? options = null)
        where TEvent : struct, IStructEvent
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        options ??= new StructEventRouteOptions();

        var route = (StructEventRoute<TEvent>)_routes.GetOrAdd(
            typeof(TEvent),
            _ => new StructEventRoute<TEvent>(options));

        if (route.Options != options)
        {
            throw new InvalidOperationException(
                $"Struct event route '{typeof(TEvent).FullName}' already exists with different options.");
        }

        return route;
    }

    /// <inheritdoc />
    public IReadOnlyList<StructEventRouteStats> GetRouteStats()
    {
        var stats = new List<StructEventRouteStats>(_routes.Count);
        foreach (var route in _routes.Values)
            stats.Add(GetRouteStats(route));

        return stats;
    }

    /// <inheritdoc />
    public StructEventHubStats GetStats()
    {
        var routeCount = 0;
        var maxQueued = 0;
        var subscriberCount = 0;
        var inboxCount = 0;
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
            currentQueued += stats.CurrentQueued;
            maxQueued = Math.Max(maxQueued, stats.MaxQueued);
            emitted += stats.Emitted;
            accepted += stats.Accepted;
            dropped += stats.Dropped;
            filtered += stats.Filtered;
            subscriberWrites += stats.SubscriberWrites;
            subscriberDrops += stats.SubscriberDrops;
        }

        return new StructEventHubStats(
            routeCount,
            subscriberCount,
            inboxCount,
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

    private static StructEventRouteStats GetRouteStats(object route)
    {
        if (route is IStructEventRoute localRoute)
            return localRoute.GetUntypedStats();

        throw new InvalidOperationException("Unknown struct event route type.");
    }

    private static void DisposeRoute(object route)
    {
        if (route is IDisposable disposable)
            disposable.Dispose();
    }
}
