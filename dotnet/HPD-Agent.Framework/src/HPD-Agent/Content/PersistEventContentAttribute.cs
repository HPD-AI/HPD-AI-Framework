namespace HPD.Agent;

/// <summary>Declares that canonical serialized copies of an event are archived to content storage.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PersistEventContentAttribute(string kind) : Attribute
{
    /// <summary>Gets the normalized archive kind.</summary>
    public string Kind { get; } = string.IsNullOrWhiteSpace(kind)
        ? throw new ArgumentException("Content archive kind cannot be empty.", nameof(kind))
        : kind.Trim();

    /// <summary>Gets or sets the archived media type.</summary>
    public string ContentType { get; init; } = "application/json";

    /// <summary>Gets or sets the content origin.</summary>
    public ContentSource Origin { get; init; } = ContentSource.Agent;

    /// <summary>Gets or sets an explicit content scope. When absent, the event session is required.</summary>
    public string? Scope { get; init; }
}
