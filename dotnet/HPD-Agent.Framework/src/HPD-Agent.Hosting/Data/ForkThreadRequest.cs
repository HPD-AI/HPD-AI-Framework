namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Request to fork a thread at a message boundary.
/// </summary>
/// <param name="NewThreadId">Unique identifier for the new thread</param>
/// <param name="FromMessageId">Message id where fork occurs (copies messages through that message). Null forks from the root before any messages.</param>
/// <param name="Name">Optional display name for the forked thread</param>
/// <param name="Description">Optional description</param>
/// <param name="Tags">Optional tags</param>
/// <param name="Metadata">Optional thread-level metadata</param>
/// <param name="Compaction">Optional fork-target compaction override</param>
public record ForkThreadRequest(
    string? NewThreadId,
    string? FromMessageId,
    string? Name,
    string? Description,
    List<string>? Tags,
    Dictionary<string, object>? Metadata = null,
    ThreadForkCompaction? Compaction = null);
