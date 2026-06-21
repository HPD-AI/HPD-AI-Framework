namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Request to create a new thread.
/// </summary>
/// <param name="ThreadId">Unique identifier for the new thread</param>
/// <param name="Name">Optional display name</param>
/// <param name="Description">Optional description</param>
/// <param name="Tags">Optional tags</param>
/// <param name="Metadata">Optional thread-level metadata</param>
public record CreateThreadRequest(
    string ThreadId,
    string? Name,
    string? Description,
    List<string>? Tags,
    Dictionary<string, object>? Metadata = null);
