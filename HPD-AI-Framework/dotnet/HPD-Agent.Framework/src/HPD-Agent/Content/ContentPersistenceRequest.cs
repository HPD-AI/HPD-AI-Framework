namespace HPD.Agent;

/// <summary>
/// Describes how an agent event should be persisted into the workspace store.
/// This is event type policy, not serialized event payload.
/// </summary>
public sealed record ContentPersistenceRequest
{
    /// <summary>
    /// Optional content scope. When omitted, the current session scope is used.
    /// </summary>
    public string? Scope { get; init; }

    /// <summary>
    /// Workspace content role, such as memory or artifact.
    /// </summary>
    public required string Role { get; init; }

    /// <summary>
    /// Agent-facing path hint, such as /memory/events or /artifacts.
    /// </summary>
    public string? PathHint { get; init; }

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
    /// Additional tags to merge with standard event tags.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Tags { get; init; }

    /// <summary>
    /// Optional expected attachment version when replacing an existing event content attachment.
    /// </summary>
    public string? IfMatchAttachmentVersion { get; init; }

    /// <summary>
    /// Optional expected content version when replacing an existing event content object.
    /// </summary>
    public string? IfMatchContentVersion { get; init; }
}
