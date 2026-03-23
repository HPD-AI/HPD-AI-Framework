namespace HPD.Agent.Serialization;

/// <summary>
/// Specifies a custom type discriminator for an event type.
/// Use this attribute to override the auto-generated SCREAMING_SNAKE_CASE name.
/// </summary>
/// <remarks>
/// <para>
/// By default, the source generator converts event type names to SCREAMING_SNAKE_CASE:
/// - <c>AnalysisProgressEvent</c> → <c>"ANALYSIS_PROGRESS"</c>
/// - <c>DataLoadedEvent</c> → <c>"DATA_LOADED"</c>
/// </para>
/// <para>
/// Use this attribute when you need a custom discriminator, for example:
/// - To resolve naming conflicts between events with similar names
/// - To maintain compatibility with existing event formats
/// - To use a custom naming convention
/// </para>
/// <example>
/// <code>
/// // Override the default auto-generated name
/// [EventType("CUSTOM_ANALYSIS")]
/// public record AnalysisProgressEvent(string Stage, int Percentage) : AgentEvent;
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EventTypeAttribute : Attribute
{
    /// <summary>
    /// Gets the custom type discriminator.
    /// </summary>
    public string Discriminator { get; }

    /// <summary>
    /// Creates a new EventTypeAttribute with the specified discriminator.
    /// </summary>
    /// <param name="discriminator">
    /// The custom type discriminator. Should follow SCREAMING_SNAKE_CASE convention.
    /// </param>
    public EventTypeAttribute(string discriminator)
    {
        Discriminator = discriminator ?? throw new ArgumentNullException(nameof(discriminator));
    }
}
