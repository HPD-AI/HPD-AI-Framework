namespace HPD.Agent;

/// <summary>
/// Describes how an agent event should be persisted into the content store.
/// This is event type policy, not serialized event payload.
/// </summary>
public sealed record ContentPersistenceRequest
{
    /// <summary>
    /// Optional content scope. When omitted, the current session scope is used.
    /// </summary>
    public string? Scope { get; init; }

    /// <summary>
    /// Content folder tag, such as /memory/events or /artifacts.
    /// </summary>
    public required string Folder { get; init; }

    /// <summary>
    /// Content item name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// MIME type of the persisted event envelope.
    /// </summary>
    public string ContentType { get; init; } = "application/json";

    /// <summary>
    /// Description stored with the content item.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Origin stored with the content item.
    /// </summary>
    public ContentSource Origin { get; init; } = ContentSource.System;

    /// <summary>
    /// Additional tags to merge with standard event and folder tags.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Tags { get; init; }

    /// <summary>
    /// Explicit content write behavior. Defaults to create-only with name collision protection.
    /// </summary>
    public ContentWriteOptions Options { get; init; } = new()
    {
        Mode = ContentWriteMode.Create,
        FailIfNameExists = true
    };
}
