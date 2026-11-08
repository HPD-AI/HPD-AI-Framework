namespace HPD.Agent.Serialization;

/// <summary>
/// Marks an <see cref="AgentInputEvent"/> as eligible for the public input HTTP surface.
/// </summary>
/// <remarks>
/// Custom agent inputs are trusted-runtime-only unless this attribute is applied.
/// Public inputs are still validated by the generated input codec before dispatch.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AgentInputAttribute : Attribute;
