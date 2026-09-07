using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using HPD.Events;

namespace HPD.Agent;

/// <summary>Selects threads relative to an anchor for an agent event subscription.</summary>
public enum AgentEventHierarchy
{
    /// <summary>Only the exact anchor thread.</summary>
    ExactThread = 0,
    /// <summary>Only direct children of the anchor; the anchor and grandchildren are excluded.</summary>
    DirectChildren = 1,
    /// <summary>The anchor and its direct children; deeper descendants are excluded.</summary>
    ThreadAndDirectChildren = 2,
    /// <summary>All transitive descendants, excluding the anchor and all sibling branches.</summary>
    Descendants = 3,
    /// <summary>The anchor and all transitive descendants rooted beneath it; sibling branches are excluded.</summary>
    ThreadAndDescendants = 4
}

/// <summary>Serializable attribution for one thread-routed agent event delivery.</summary>
/// <param name="Origin">The thread that originally emitted the event.</param>
/// <param name="Path">The root-to-origin thread path, including <paramref name="Origin"/> as its final element.</param>
/// <param name="ThreadExecutionId">The originating execution when the event belongs to a running turn.</param>
public sealed record AgentEventRoute(
    ThreadKey Origin,
    IReadOnlyList<ThreadKey> Path,
    string? ThreadExecutionId);

/// <summary>Pairs an agent event with its immutable thread route.</summary>
/// <param name="Event">The domain event delivered to the observer.</param>
/// <param name="Route">Immutable thread attribution retained across coordinator bridges.</param>
public sealed record AgentEventDelivery(AgentEvent Event, AgentEventRoute Route);

internal sealed class AgentEventRouteDescriptor : EventRouteDescriptor
{
    internal AgentEventRouteDescriptor(ThreadKey origin, ImmutableArray<ThreadKey> path, string? executionId)
    {
        if (string.IsNullOrWhiteSpace(origin.SessionId) || string.IsNullOrWhiteSpace(origin.ThreadId) ||
            path.IsDefaultOrEmpty || path[^1] != origin || path.Distinct().Count() != path.Length ||
            path.Any(key => string.IsNullOrWhiteSpace(key.SessionId) || string.IsNullOrWhiteSpace(key.ThreadId)))
            throw new InvalidOperationException("Agent event route is invalid.");
        Origin = origin;
        Path = path;
        ThreadExecutionId = executionId;
    }

    internal ThreadKey Origin { get; }
    internal ImmutableArray<ThreadKey> Path { get; }
    internal string? ThreadExecutionId { get; }
    internal AgentEventRoute ToPublic() => new(Origin, Path, ThreadExecutionId);
}

internal static class AgentEventRoutes
{
    private static readonly ConditionalWeakTable<IEventCoordinator, ResolverBinding> Bindings = new();

    internal static void Initialize(IEventCoordinator coordinator) =>
        Bindings.GetValue(coordinator, static _ => new ResolverBinding(new AgentEventLineageResolver()));

    internal static void AttachCoordinator(IEventCoordinator coordinator, IEventCoordinator parent)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(parent);
        var parentResolver = GetResolver(parent);
        Bindings.GetValue(coordinator, static _ => new ResolverBinding(new AgentEventLineageResolver())).Resolver = parentResolver;
    }

    internal static void RegisterChild(IEventCoordinator coordinator, ThreadKey child, ThreadKey parent)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        Validate(child, nameof(child));
        Validate(parent, nameof(parent));
        GetResolver(coordinator).RegisterChild(child, parent);
    }

    internal static ThreadKey? ValidateParentPair(string? parentSessionId, string? parentThreadId)
    {
        var hasSession = !string.IsNullOrWhiteSpace(parentSessionId);
        var hasThread = !string.IsNullOrWhiteSpace(parentThreadId);
        if (hasSession != hasThread)
            throw new InvalidOperationException("Runtime thread lineage requires both parent session and parent thread identifiers.");
        return hasSession ? new ThreadKey(parentSessionId!, parentThreadId!) : null;
    }

    private static void Validate(ThreadKey key, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key.SessionId, parameterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.ThreadId, parameterName);
    }

    internal static AgentEventRouteDescriptor? Create(IEventCoordinator coordinator, AgentEvent evt)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        if (string.IsNullOrWhiteSpace(evt.SessionId) || string.IsNullOrWhiteSpace(evt.ThreadId))
            return null;
        var origin = new ThreadKey(evt.SessionId, evt.ThreadId);
        return new AgentEventRouteDescriptor(origin, GetResolver(coordinator).Resolve(origin), evt.ThreadExecutionId);
    }

    internal static async ValueTask<AgentEventRouteDescriptor> CreateFromStoreAsync(
        ISessionStore store,
        AgentEvent evt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (string.IsNullOrWhiteSpace(evt.SessionId) || string.IsNullOrWhiteSpace(evt.ThreadId))
            throw new InvalidOperationException("A keyed delivery requires complete thread attribution.");
        var origin = new ThreadKey(evt.SessionId, evt.ThreadId);
        var reversed = new List<ThreadKey> { origin };
        var seen = new HashSet<ThreadKey> { origin };
        var current = origin;
        while (await store.GetThreadAsync(current, cancellationToken).ConfigureAwait(false) is { } descriptor &&
            ValidateParentPair(descriptor.RuntimeChild?.ParentSessionId, descriptor.RuntimeChild?.ParentThreadId) is { } parent)
        {
            if (!seen.Add(parent))
                throw new InvalidOperationException("A cycle was detected in persisted runtime thread lineage.");
            reversed.Add(parent);
            current = parent;
        }
        reversed.Reverse();
        return new AgentEventRouteDescriptor(origin, [.. reversed], evt.ThreadExecutionId);
    }

    internal static DeliveryInbox<AgentEventDelivery> CreateDeliveryInbox(
        HPD.Events.Core.EventCoordinator coordinator,
        ThreadKey anchor,
        AgentEventHierarchy hierarchy,
        EventInboxOptions? options = null) =>
        coordinator.CreateProjectedDeliveryInbox<AgentEvent, AgentEventDelivery>(
            EventOwnerScope.AllOwners,
            new AgentHierarchyDeliveryPolicy(anchor, hierarchy),
            AgentDeliveryProjector.Instance,
            options);

    private static AgentEventLineageResolver GetResolver(IEventCoordinator coordinator) =>
        Bindings.GetValue(coordinator, static _ => new ResolverBinding(new AgentEventLineageResolver())).Resolver;

    private sealed class ResolverBinding(AgentEventLineageResolver resolver)
    {
        private AgentEventLineageResolver _resolver = resolver;

        internal AgentEventLineageResolver Resolver
        {
            get => Volatile.Read(ref _resolver);
            set => Volatile.Write(ref _resolver, value);
        }
    }
}

internal sealed class AgentEventLineageResolver
{
    private readonly object _gate = new();
    private readonly Dictionary<ThreadKey, ThreadKey> _parents = [];

    internal void RegisterChild(ThreadKey child, ThreadKey parent)
    {
        lock (_gate)
        {
            if (child == parent)
                throw new InvalidOperationException("A thread cannot be its own runtime parent.");
            var cursor = parent;
            var visited = new HashSet<ThreadKey> { child };
            while (true)
            {
                if (!visited.Add(cursor))
                    throw new InvalidOperationException("Registering this runtime parent would create a thread lineage cycle.");
                if (!_parents.TryGetValue(cursor, out cursor))
                    break;
            }
            if (_parents.TryGetValue(child, out var existing) && existing != parent)
                throw new InvalidOperationException("A child thread already has a different runtime parent.");
            _parents[child] = parent;
        }
    }

    internal ImmutableArray<ThreadKey> Resolve(ThreadKey origin)
    {
        lock (_gate)
        {
            var reversed = new List<ThreadKey> { origin };
            var seen = new HashSet<ThreadKey> { origin };
            var current = origin;
            while (_parents.TryGetValue(current, out var parent))
            {
                if (!seen.Add(parent))
                    throw new InvalidOperationException("A cycle was detected in runtime thread lineage.");
                reversed.Add(parent);
                current = parent;
            }
            reversed.Reverse();
            return [.. reversed];
        }
    }
}

internal sealed class AgentHierarchyDeliveryPolicy(ThreadKey anchor, AgentEventHierarchy hierarchy)
    : IEventDeliveryPolicy
{
    public bool Includes(in EventDeliveryContext context)
    {
        if (context.Route is not AgentEventRouteDescriptor route)
            return false;
        var index = route.Path.IndexOf(anchor);
        if (index < 0)
            return false;
        var depth = route.Path.Length - index - 1;
        return hierarchy switch
        {
            AgentEventHierarchy.ExactThread => depth == 0,
            AgentEventHierarchy.DirectChildren => depth == 1,
            AgentEventHierarchy.ThreadAndDirectChildren => depth <= 1,
            AgentEventHierarchy.Descendants => depth >= 1,
            AgentEventHierarchy.ThreadAndDescendants => depth >= 0,
            _ => throw new ArgumentOutOfRangeException(nameof(hierarchy), hierarchy, "Unknown agent event hierarchy.")
        };
    }
}

internal sealed class AgentDeliveryProjector : IEventDeliveryProjector<AgentEvent, AgentEventDelivery>
{
    internal static AgentDeliveryProjector Instance { get; } = new();
    public AgentEventDelivery Project(AgentEvent evt, in EventDeliveryContext context)
    {
        if (context.Route is not AgentEventRouteDescriptor route)
            throw new InvalidOperationException("A keyed agent delivery requires a thread route.");
        return new AgentEventDelivery(evt, route.ToPublic());
    }
}
