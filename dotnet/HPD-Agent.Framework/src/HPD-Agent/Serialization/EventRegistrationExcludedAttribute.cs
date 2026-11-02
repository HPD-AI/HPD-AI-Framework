namespace HPD.Agent.Serialization;

/// <summary>
/// Excludes an event contract from source-generated event registration.
/// </summary>
/// <remarks>
/// Use this attribute for generic event contracts that are registered with
/// a closed type and JSON metadata at their concrete construction site.
/// Generic event types cannot be registered as an open type by the source generator.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EventRegistrationExcludedAttribute : Attribute
{
}
