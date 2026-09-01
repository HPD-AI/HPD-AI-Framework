#pragma warning restore CS1591

namespace HPD.Events;

/// <summary>One replay event together with the exact key and source evidence used by its timeline.</summary>
/// <typeparam name="TEvent">The replayed event type.</typeparam>
/// <param name="Event">The replayed event.</param>
/// <param name="Key">The effective ordering key computed exactly once by the timeline.</param>
/// <param name="Source">The registered source identity and priority.</param>
public sealed record ReplayEntry<TEvent>(TEvent Event, ReplayKey Key, ReplaySourceInfo Source)
    where TEvent : Event;
