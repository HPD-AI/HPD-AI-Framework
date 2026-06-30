namespace HPD.Events;

/// <summary>
/// Optional annotation surface for events that need sparse diagnostic or projection metadata.
/// Domain data should remain on the event as typed properties.
/// </summary>
public interface IAnnotatedEvent
{
    /// <summary>Typed scalar annotations associated with this event.</summary>
    IReadOnlyList<EventAnnotation> Annotations { get; }
}

/// <summary>
/// One typed event annotation.
/// </summary>
public sealed record EventAnnotation
{
    /// <summary>Stable annotation key.</summary>
    public required string Key { get; init; }

    /// <summary>Scalar annotation value.</summary>
    public required EventAnnotationValue Value { get; init; }

    /// <summary>Projection visibility hint. This is not an authorization policy.</summary>
    public EventAnnotationVisibility Visibility { get; init; } =
        EventAnnotationVisibility.Internal;
}

/// <summary>
/// A source-generation-friendly scalar annotation value.
/// </summary>
public readonly record struct EventAnnotationValue
{
    /// <summary>Kind of scalar value stored by this annotation.</summary>
    public EventAnnotationValueKind Kind { get; init; }

    /// <summary>String value when <see cref="Kind"/> is <see cref="EventAnnotationValueKind.String"/>.</summary>
    public string? String { get; init; }

    /// <summary>Integer value when <see cref="Kind"/> is <see cref="EventAnnotationValueKind.Integer"/>.</summary>
    public long? Integer { get; init; }

    /// <summary>Number value when <see cref="Kind"/> is <see cref="EventAnnotationValueKind.Number"/>.</summary>
    public double? Number { get; init; }

    /// <summary>Boolean value when <see cref="Kind"/> is <see cref="EventAnnotationValueKind.Boolean"/>.</summary>
    public bool? Boolean { get; init; }

    /// <summary>Create a string annotation value.</summary>
    public static EventAnnotationValue FromString(string value) => new()
    {
        Kind = EventAnnotationValueKind.String,
        String = value
    };

    /// <summary>Create an integer annotation value.</summary>
    public static EventAnnotationValue FromInteger(long value) => new()
    {
        Kind = EventAnnotationValueKind.Integer,
        Integer = value
    };

    /// <summary>Create a number annotation value.</summary>
    public static EventAnnotationValue FromNumber(double value) => new()
    {
        Kind = EventAnnotationValueKind.Number,
        Number = value
    };

    /// <summary>Create a boolean annotation value.</summary>
    public static EventAnnotationValue FromBoolean(bool value) => new()
    {
        Kind = EventAnnotationValueKind.Boolean,
        Boolean = value
    };
}

/// <summary>
/// Supported scalar annotation value kinds.
/// </summary>
public enum EventAnnotationValueKind
{
    String,
    Integer,
    Number,
    Boolean
}

/// <summary>
/// Visibility hint for event projection layers.
/// </summary>
public enum EventAnnotationVisibility
{
    Public,
    Internal,
    Diagnostic
}
