using System.Threading.Channels;

namespace HPD.Events;

/// <summary>Identifies the component instance that originally published an event.</summary>
public readonly record struct EventOwnerId(Guid Value)
{
    /// <summary>Creates a new process-local event owner identity.</summary>
    public static EventOwnerId Create() => new(Guid.NewGuid());
}

/// <summary>Base class for process-local, domain-specific event route information.</summary>
public abstract class EventRouteDescriptor;

/// <summary>Describes the immutable origin and route of one event delivery.</summary>
/// <param name="OriginOwner">The coordinator owner that originally emitted the event.</param>
/// <param name="Route">Optional domain-specific route information.</param>
/// <param name="HopCount">The number of coordinator edges crossed since emission.</param>
public readonly record struct EventDeliveryContext(
    EventOwnerId OriginOwner,
    EventRouteDescriptor? Route,
    int HopCount);

/// <summary>Selects event owners relative to the coordinator receiving a subscription.</summary>
public enum EventOwnerScope
{
    /// <summary>Only events originating from the receiving coordinator's owner.</summary>
    SameOwner = 0,

    /// <summary>Events originating from any owner reachable by the configured transport.</summary>
    AllOwners = 1
}

/// <summary>Controls whether a child coordinator inherits or creates event ownership.</summary>
public enum EventChildOwnership
{
    /// <summary>The child represents the same component owner as its parent.</summary>
    InheritOwner = 0,

    /// <summary>The child represents an independently owned component.</summary>
    NewOwner = 1
}

/// <summary>Options for a provenance-preserving coordinator bridge.</summary>
public sealed record EventForwardingOptions
{
    /// <summary>Optional event base type accepted by the bridge.</summary>
    public Type? EventType { get; init; }

    /// <summary>Whether events derived from <see cref="EventType"/> are accepted.</summary>
    public bool IncludeDerivedTypes { get; init; } = true;

    /// <summary>Optional event channel accepted by the bridge.</summary>
    public EventChannel? Channel { get; init; }

    /// <summary>Bridge mailbox capacity.</summary>
    public int Capacity { get; init; } = 4096;

    /// <summary>Bridge mailbox backpressure behavior.</summary>
    public BoundedChannelFullMode FullMode { get; init; } = BoundedChannelFullMode.Wait;
}

/// <summary>A caller-owned inbox for projected delivery values that are not domain events.</summary>
/// <remarks>
/// Values retain source publication order within each originating coordinator; no ordering is
/// promised across independent origins. Disposing the inbox stops observation and completes its
/// reader without affecting event publication, bubbling, forwarding, or the observed operation.
/// Disposal is idempotent.
/// </remarks>
public sealed class DeliveryInbox<TDelivery> : IAsyncDisposable
{
    private readonly ChannelWriter<TDelivery>? _writer;
    private Action<ChannelWriter<TDelivery>>? _dispose;

    internal DeliveryInbox(
        ChannelReader<TDelivery> reader,
        ChannelWriter<TDelivery> writer,
        Action<ChannelWriter<TDelivery>> dispose)
    {
        Reader = reader;
        _writer = writer;
        _dispose = dispose;
    }

    /// <summary>Gets the reader owned by the caller.</summary>
    public ChannelReader<TDelivery> Reader { get; }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        var dispose = Interlocked.Exchange(ref _dispose, null);
        if (_writer is not null && dispose is not null)
            dispose(_writer);
        return ValueTask.CompletedTask;
    }
}

internal interface IEventDeliveryPolicy
{
    bool Includes(in EventDeliveryContext context);
}

internal interface IEventDeliveryProjector<in TEvent, out TDelivery>
    where TEvent : Event
{
    TDelivery Project(TEvent evt, in EventDeliveryContext context);
}
