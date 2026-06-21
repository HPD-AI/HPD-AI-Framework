namespace HPD.Events;

/// <summary>
/// Direction of event flow in hierarchical coordinator systems.
/// </summary>
public enum EventDirection
{
    /// <summary>
    /// Event flows downstream (parent to child, normal flow).
    /// Default direction for most events.
    /// </summary>
    Downstream = 0,

    /// <summary>
    /// Event flows upstream (child to parent, bubbling up).
    /// Used for hierarchical event propagation and interruptions.
    /// </summary>
    Upstream = 1
}
