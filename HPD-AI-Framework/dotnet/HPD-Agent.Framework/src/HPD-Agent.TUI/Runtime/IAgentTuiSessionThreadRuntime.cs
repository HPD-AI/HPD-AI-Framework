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

    Task<AgentTuiThreadInfo> ForkThreadAsync(
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

    Task<IReadOnlyList<AgentTuiThreadInfo>> GetSiblingThreadsAsync(
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
    string FromMessageId,
    string? NewThreadId = null,
    string? Name = null,
    string? Description = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public sealed record AgentTuiThreadUpdate(
    string? Name = null,
    string? Description = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public sealed record AgentTuiThreadInfo(
    string Id,
    string SessionId,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivity,
    int MessageCount = 0,
    bool IsOriginal = false,
    string? ForkedFrom = null,
    string? ForkedAtMessageId = null,
    int? ForkedAtMessageIndex = null,
    int TotalForks = 0,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyDictionary<string, string>? Ancestors = null,
    int SiblingIndex = 0,
    int TotalSiblings = 1,
    string? OriginalThreadId = null,
    string? PreviousSiblingId = null,
    string? NextSiblingId = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);
