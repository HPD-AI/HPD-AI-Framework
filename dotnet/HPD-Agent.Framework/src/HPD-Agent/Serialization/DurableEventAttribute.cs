namespace HPD.Agent.Serialization;

/// <summary>
/// Admits an <see cref="AgentEvent"/> type to the canonical thread journal.
/// Unannotated agent events are live-only.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DurableEventAttribute : Attribute
{
}
