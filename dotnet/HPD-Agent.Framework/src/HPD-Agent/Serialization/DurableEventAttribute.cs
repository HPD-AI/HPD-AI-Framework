namespace HPD.Agent.Serialization;

/// <summary>
/// Admits an <see cref="AgentEvent"/> type to the canonical thread journal.
/// Unannotated agent events are live-only.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DurableEventAttribute : Attribute
{
    /// <summary>
    /// Gets or sets whether the event is required by runtime recovery or persistence
    /// invariants. Required durable events cannot be demoted to live-only by host policy.
    /// </summary>
    public bool Required { get; set; }
}
