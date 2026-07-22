using System.Text.Json.Serialization.Metadata;

namespace HPD.Agent.Serialization;

/// <summary>
/// Immutable diagnostic view of a registered agent-event wire type.
/// </summary>
public readonly record struct EventTypeRegistration(
    Type EventType,
    string Discriminator,
    JsonTypeInfo? TypeInfo);
