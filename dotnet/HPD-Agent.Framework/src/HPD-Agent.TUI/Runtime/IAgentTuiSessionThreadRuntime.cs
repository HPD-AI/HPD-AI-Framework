namespace HPD.Agent.TUI.Runtime;

public interface IAgentTuiSessionRuntime
{
    Task<IReadOnlyList<AgentTuiSessionInfo>> ListSessionsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentTuiSessionInfo>> SearchSessionsAsync(
        AgentTuiSessionSearch? search = null,
        CancellationToken cancellationToken = default);

    Task<AgentTuiSessionInfo?> GetSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<AgentTuiSessionInfo> CreateSessionAsync(
        string? sessionId = null,
        string? title = null,
        CancellationToken cancellationToken = default);

    Task RenameSessionAsync(
        string sessionId,
        string title,
        CancellationToken cancellationToken = default);

    Task<AgentTuiSessionInfo> UpdateSessionAsync(
        string sessionId,
        AgentTuiSessionUpdate update,
        CancellationToken cancellationToken = default);

    Task DeleteSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);
}

public interface IAgentTuiThreadRuntime
{
    Task<IReadOnlyList<AgentTuiThreadInfo>> ListThreadsAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<AgentTuiThreadInfo?> GetThreadAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default);

    Task<AgentTuiThreadInfo> CreateThreadAsync(
        string agentId,
        string sessionId,
        string? threadId = null,
        string? name = null,
        CancellationToken cancellationToken = default);

    Task<AgentTuiThreadInfo> CreateThreadAsync(
        string agentId,
        string sessionId,
        AgentTuiCreateThreadRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentTuiThreadForkInfo> ForkThreadAsync(
        string agentId,
        string sessionId,
        string sourceThreadId,
        AgentTuiForkThreadRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentTuiThreadInfo> UpdateThreadAsync(
        string sessionId,
        string threadId,
        AgentTuiThreadUpdate update,
        CancellationToken cancellationToken = default);

    Task<AgentTuiThreadGraph> GetThreadGraphAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentTuiSubAgentInfo>> ListSubAgentsAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default);

    Task DeleteThreadAsync(
        string sessionId,
        string threadId,
        bool recursive = false,
        CancellationToken cancellationToken = default);
}

public interface IAgentTuiSessionThreadRuntime : IAgentTuiSessionRuntime, IAgentTuiThreadRuntime
{
}

public sealed record AgentTuiSessionSearch(
    IReadOnlyDictionary<string, object?>? Metadata = null,
    int Offset = 0,
    int Limit = 50);

public sealed record AgentTuiSessionUpdate(
    IReadOnlyDictionary<string, object?> Metadata);

public sealed record AgentTuiSessionInfo(
    string Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivity,
    string? Title = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public sealed record AgentTuiCreateThreadRequest(
    string? ThreadId = null,
    string? Name = null,
    string? Description = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public sealed record AgentTuiForkThreadRequest(
    string? FromMessageId,
    string? NewThreadId = null,
    string? Name = null,
    string? Description = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyDictionary<string, object?>? Metadata = null,
    SubAgentForkOptions? SubAgents = null,
    string? OperationId = null);

public sealed record AgentTuiThreadForkInfo(
    string OperationId,
    AgentTuiThreadInfo Target,
    long SourceGeneration,
    long SourceSequence,
    SubAgentForkPolicy SubAgentPolicy,
    ThreadForkOperationStatus Status,
    IReadOnlyList<SubAgentForkChildOutcome> Children);

public sealed record AgentTuiSubAgentInfo(
    string LocalId,
    string Role,
    SubAgentChildAvailability Availability,
    string AgentId,
    string? SessionId,
    string? ThreadId,
    string? Status,
    int MessageCount,
    string? Reason);

public sealed record AgentTuiThreadUpdate(
    string? Name = null,
    string? Description = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public sealed record AgentTuiThreadInfo(
    string Id,
    string SessionId,
    string DefaultAgentId,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivity,
    int MessageCount = 0,
    string? ForkedFrom = null,
    string? ForkedAtMessageId = null,
    int? ForkedAtMessageIndex = null,
    int TotalForks = 0,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyDictionary<string, string>? Ancestors = null,
    ThreadKind Kind = ThreadKind.MainAgent,
    ThreadVisibility Visibility = ThreadVisibility.Visible,
    string? ParentSessionId = null,
    string? ParentThreadId = null,
    string? SubAgentName = null,
    string? InvocationId = null,
    string? SubAgentSourceKind = null,
    string? ParentToolCallId = null,
    string? ContextPolicy = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public sealed record AgentTuiThreadGraph(
    IReadOnlyList<AgentTuiThreadInfo> Threads,
    IReadOnlyList<AgentTuiThreadForkGroup> ForkGroups,
    IReadOnlyList<AgentTuiThreadRuntimeChild> RuntimeChildren);

public sealed record AgentTuiThreadForkGroup(
    string Id,
    string SourceThreadId,
    string? ForkedAtMessageId,
    int? ForkedAtMessageIndex,
    int ChoiceMessageIndex,
    IReadOnlyList<AgentTuiThreadForkGroupMember> Members);

public sealed record AgentTuiThreadForkGroupMember(
    string ThreadId,
    string Name,
    int Index,
    bool IsSource,
    string? ChoiceMessageId,
    int? ChoiceMessageIndex,
    int MessageCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivity);

public sealed record AgentTuiThreadRuntimeChild(
    string ThreadId,
    string SessionId,
    string DefaultAgentId,
    string ParentSessionId,
    string ParentThreadId,
    string Name,
    ThreadKind Kind,
    ThreadVisibility Visibility,
    string? SubAgentName,
    string? InvocationId,
    string? SubAgentSourceKind,
    string? ParentToolCallId,
    string? ContextPolicy,
    string? Status,
    int MessageCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivity);
