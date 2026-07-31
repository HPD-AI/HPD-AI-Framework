using System.Text.Json;
using HPD.Events;

namespace HPD.Base;

/// <summary>
/// Base type for HPD.BASE domain events flowing through HPD.Events.
/// </summary>
public abstract record BaseEvent : Event
{
    /// <summary>Stable BASE event identifier for result references and external correlation.</summary>
    public required string EventId { get; init; }

    /// <summary>Stable BASE event type name.</summary>
    public required string Type { get; init; }

    /// <summary>Version of the BASE event contract shape.</summary>
    public required string SchemaVersion { get; init; }

    /// <summary>Tenant associated with the event when known.</summary>
    public string? TenantId { get; init; }

    /// <summary>Correlation id associated with the originating operation.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Causation id associated with the originating operation.</summary>
    public string? CausationId { get; init; }

    /// <summary>Visibility level for safe projection of this event.</summary>
    public VisibilityLevel Visibility { get; init; }

    /// <summary>Optional audience hints for event projection or delivery.</summary>
    public string[]? Audience { get; init; }

    /// <summary>Summary of the principal that caused the event.</summary>
    public EventPrincipalSummary? Principal { get; init; }

    /// <summary>Namespaced module extension data carried by the event contract.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
