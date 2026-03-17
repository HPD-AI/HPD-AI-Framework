namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Data transfer object for Branch metadata.
/// Represents a conversation path within a session.
/// </summary>
/// <param name="Id">Unique identifier for this branch</param>
/// <param name="SessionId">Parent session ID</param>
/// <param name="Name">Display name for this branch</param>
/// <param name="Description">Optional user-friendly description</param>
/// <param name="ForkedFrom">Source branch ID if this was forked (null for original branches)</param>
/// <param name="ForkedAtMessageIndex">Message index where fork occurred (null for original branches)</param>
/// <param name="CreatedAt">When this branch was created</param>
/// <param name="LastActivity">Last time this branch was updated</param>
/// <param name="MessageCount">Number of messages in this branch</param>
/// <param name="Tags">Optional tags for categorizing branches</param>
/// <param name="Ancestors">Full ancestry chain for multi-level fork tracking</param>
/// <param name="SiblingIndex">Position among siblings at this fork point (0-based)</param>
/// <param name="TotalSiblings">Total number of sibling branches at this fork point</param>
/// <param name="IsOriginal">True if this is the original branch (not forked from another)</param>
/// <param name="OriginalBranchId">ID of the original branch in this sibling group</param>
/// <param name="PreviousSiblingId">ID of the previous sibling (null if first)</param>
/// <param name="NextSiblingId">ID of the next sibling (null if last)</param>
/// <param name="TotalForks">Count of direct child branches</param>
public record BranchDto(
    string Id,
    string SessionId,
    string Name,
    string? Description,
    string? ForkedFrom,
    int? ForkedAtMessageIndex,
    DateTime CreatedAt,
    DateTime LastActivity,
    int MessageCount,
    List<string>? Tags,
    Dictionary<string, string>? Ancestors,
    // V3: Tree navigation metadata
    int SiblingIndex,
    int TotalSiblings,
    bool IsOriginal,
    string? OriginalBranchId,
    string? PreviousSiblingId,
    string? NextSiblingId,
    int TotalForks);

/// <summary>
/// Lightweight sibling branch metadata for navigation UI.
/// Includes only fields needed for sibling selection and display.
/// </summary>
/// <param name="Id">Unique identifier for this branch</param>
/// <param name="Name">Display name for this branch</param>
/// <param name="SiblingIndex">Position among siblings (0-based)</param>
/// <param name="TotalSiblings">Total number of siblings at this fork point</param>
/// <param name="IsOriginal">True if this is the original branch</param>
/// <param name="MessageCount">Number of messages in this branch</param>
/// <param name="CreatedAt">When this branch was created</param>
/// <param name="LastActivity">Last time this branch was updated</param>
public record SiblingBranchDto(
    string Id,
    string Name,
    int SiblingIndex,
    int TotalSiblings,
    bool IsOriginal,
    int MessageCount,
    DateTime CreatedAt,
    DateTime LastActivity);
