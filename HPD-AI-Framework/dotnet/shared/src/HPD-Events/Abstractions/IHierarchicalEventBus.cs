namespace HPD.Events;

/// <summary>
/// Hierarchy setup surface for parent event bubbling.
/// </summary>
public interface IHierarchicalEventBus
{
    /// <summary>
    /// Set the parent bus for class-event bubbling.
    /// </summary>
    void SetParent(IEventBus parent);
}
