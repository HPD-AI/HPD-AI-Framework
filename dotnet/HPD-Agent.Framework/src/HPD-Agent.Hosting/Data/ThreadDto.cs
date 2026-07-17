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
/// <param name="SubAgentSourceKind">Subagent definition source kind when this is a subagent thread</param>
/// <param name="ParentToolCallId">Parent tool call id that created this runtime child thread</param>
/// <param name="SessionPolicy">Subagent session policy captured for inspection and routing</param>
/// <param name="ThreadPolicy">Subagent thread policy captured for inspection and routing</param>
/// <param name="Ancestors">Full ancestry chain for multi-level fork tracking</param>
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
    int TotalForks,
    Dictionary<string, object>? Metadata = null,
    ThreadKind Kind = ThreadKind.MainAgent,
    ThreadVisibility Visibility = ThreadVisibility.Visible,
    string? ParentSessionId = null,
    string? ParentThreadId = null,
    string? SubAgentName = null,
    string? SubAgentTaskName = null,
    string? SubAgentRunId = null,
    string? SubAgentSourceKind = null,
    string? ParentToolCallId = null,
    string? SessionPolicy = null,
    string? ThreadPolicy = null);

/// <summary>
/// Session-level thread graph for branch navigation.
/// </summary>
/// <param name="Threads">All thread metadata in the session.</param>
/// <param name="ForkGroups">Fork-point groups derived from thread lineage.</param>
/// <param name="RuntimeChildren">Runtime child threads attached to parent threads.</param>
public record ThreadGraphDto(
    IReadOnlyList<ThreadDto> Threads,
    IReadOnlyList<ThreadForkGroupDto> ForkGroups,
    IReadOnlyList<ThreadRuntimeChildDto> RuntimeChildren);

/// <summary>
/// A set of branch choices that diverge from the same semantic fork point.
/// </summary>
/// <param name="Id">Stable graph-local id for this fork group.</param>
/// <param name="SourceThreadId">Canonical visible thread that owns the shared context.</param>
/// <param name="ForkedAtMessageId">Last shared message id before divergence.</param>
/// <param name="ForkedAtMessageIndex">Resolved index of the last shared message.</param>
/// <param name="ChoiceMessageIndex">Transcript message index where users choose between this group's branches.</param>
/// <param name="Members">Source thread followed by forks in stable display order.</param>
public record ThreadForkGroupDto(
    string Id,
    string SourceThreadId,
    string? ForkedAtMessageId,
    int? ForkedAtMessageIndex,
    int ChoiceMessageIndex,
    IReadOnlyList<ThreadForkGroupMemberDto> Members);

/// <summary>
/// Lightweight member metadata for branch navigation UI.
/// </summary>
/// <param name="ThreadId">Thread id to select when this member is chosen.</param>
/// <param name="Name">Display name for this thread.</param>
/// <param name="Index">Position within this fork group.</param>
/// <param name="IsSource">True when this member is the source thread.</param>
/// <param name="ChoiceMessageId">Message row in this member where the branch control belongs.</param>
/// <param name="ChoiceMessageIndex">Transcript index in this member where the branch control belongs.</param>
/// <param name="MessageCount">Number of messages in the thread.</param>
/// <param name="CreatedAt">When this thread was created.</param>
/// <param name="LastActivity">Last time this thread was updated.</param>
public record ThreadForkGroupMemberDto(
    string ThreadId,
    string Name,
    int Index,
    bool IsSource,
    string? ChoiceMessageId,
    int? ChoiceMessageIndex,
    int MessageCount,
    DateTime CreatedAt,
    DateTime LastActivity);

/// <summary>
/// Runtime child thread metadata, such as hidden subagent threads attached to a parent thread.
/// </summary>
/// <param name="ThreadId">Runtime child thread id.</param>
/// <param name="ParentSessionId">Parent session id.</param>
/// <param name="ParentThreadId">Parent thread id.</param>
/// <param name="Name">Display name for this runtime child.</param>
/// <param name="Kind">Runtime classification.</param>
/// <param name="Visibility">Default list visibility.</param>
/// <param name="SubAgentName">Subagent name when applicable.</param>
/// <param name="SubAgentRunId">Subagent run id when applicable.</param>
/// <param name="SubAgentSourceKind">Subagent definition source kind when applicable.</param>
/// <param name="ParentToolCallId">Parent tool call id when applicable.</param>
/// <param name="SessionPolicy">Subagent session policy when applicable.</param>
/// <param name="ThreadPolicy">Subagent thread policy when applicable.</param>
/// <param name="MessageCount">Number of messages in the runtime child thread.</param>
/// <param name="CreatedAt">When this thread was created.</param>
/// <param name="LastActivity">Last time this thread was updated.</param>
public record ThreadRuntimeChildDto(
    string ThreadId,
    string ParentSessionId,
    string ParentThreadId,
    string Name,
    ThreadKind Kind,
    ThreadVisibility Visibility,
    string? SubAgentName,
    string? SubAgentTaskName,
    string? SubAgentRunId,
    string? SubAgentSourceKind,
    string? ParentToolCallId,
    string? SessionPolicy,
    string? ThreadPolicy,
    string? Status,
    int MessageCount,
    DateTime CreatedAt,
    DateTime LastActivity);
