namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Data transfer object for Thread metadata.
/// Represents a conversation path within a session.
/// </summary>
/// <param name="Id">Unique identifier for this thread</param>
/// <param name="SessionId">Parent session ID</param>
/// <param name="Name">Display name for this thread</param>
/// <param name="Description">Optional user-friendly description</param>
/// <param name="ForkedFrom">Source thread ID if this was forked (null for original threads)</param>
/// <param name="ForkedAtMessageId">Message id where fork occurred (null for original threads)</param>
/// <param name="ForkedAtMessageIndex">Resolved message index where fork occurred (diagnostic; null for original threads)</param>
/// <param name="CreatedAt">When this thread was created</param>
/// <param name="LastActivity">Last time this thread was updated</param>
/// <param name="MessageCount">Number of messages in this thread</param>
/// <param name="Tags">Optional tags for categorizing threads</param>
/// <param name="Metadata">Arbitrary thread-level metadata (optional)</param>
/// <param name="Kind">Runtime classification for this thread</param>
/// <param name="Visibility">Default list visibility for this thread</param>
/// <param name="ParentSessionId">Parent session for runtime child threads</param>
/// <param name="ParentThreadId">Parent thread for runtime child threads</param>
/// <param name="SubAgentName">Subagent name when this is a subagent thread</param>
/// <param name="SubAgentRunId">Subagent run id when this is a subagent thread</param>
/// <param name="Ancestors">Full ancestry chain for multi-level fork tracking</param>
/// <param name="SiblingIndex">Position among siblings at this fork point (0-based)</param>
/// <param name="TotalSiblings">Total number of sibling threads at this fork point</param>
/// <param name="IsOriginal">True if this is the original thread (not forked from another)</param>
/// <param name="OriginalThreadId">ID of the original thread in this sibling group</param>
/// <param name="PreviousSiblingId">ID of the previous sibling (null if first)</param>
/// <param name="NextSiblingId">ID of the next sibling (null if last)</param>
/// <param name="TotalForks">Count of direct child threads</param>
public record ThreadDto(
    string Id,
    string SessionId,
    string Name,
    string? Description,
    string? ForkedFrom,
    string? ForkedAtMessageId,
    int? ForkedAtMessageIndex,
    DateTime CreatedAt,
    DateTime LastActivity,
    int MessageCount,
    List<string>? Tags,
    Dictionary<string, string>? Ancestors,
    //  Tree navigation metadata
    int SiblingIndex,
    int TotalSiblings,
    bool IsOriginal,
    string? OriginalThreadId,
    string? PreviousSiblingId,
    string? NextSiblingId,
    int TotalForks,
    Dictionary<string, object>? Metadata = null,
    ThreadKind Kind = ThreadKind.MainAgent,
    ThreadVisibility Visibility = ThreadVisibility.Visible,
    string? ParentSessionId = null,
    string? ParentThreadId = null,
    string? SubAgentName = null,
    string? SubAgentRunId = null);

/// <summary>
/// Lightweight sibling thread metadata for navigation UI.
/// Includes only fields needed for sibling selection and display.
/// </summary>
/// <param name="Id">Unique identifier for this thread</param>
/// <param name="Name">Display name for this thread</param>
/// <param name="SiblingIndex">Position among siblings (0-based)</param>
/// <param name="TotalSiblings">Total number of siblings at this fork point</param>
/// <param name="IsOriginal">True if this is the original thread</param>
/// <param name="MessageCount">Number of messages in this thread</param>
/// <param name="CreatedAt">When this thread was created</param>
/// <param name="LastActivity">Last time this thread was updated</param>
public record SiblingThreadDto(
    string Id,
    string Name,
    int SiblingIndex,
    int TotalSiblings,
    bool IsOriginal,
    int MessageCount,
    DateTime CreatedAt,
    DateTime LastActivity);
