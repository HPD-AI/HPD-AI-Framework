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

public interface IAgentTuiBranchRuntime
{
    Task<IReadOnlyList<AgentTuiBranchInfo>> ListBranchesAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<AgentTuiBranchInfo?> GetBranchAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default);

    Task<AgentTuiBranchInfo> CreateBranchAsync(
        string agentId,
        string sessionId,
        string? branchId = null,
        string? name = null,
        CancellationToken cancellationToken = default);

    Task<AgentTuiBranchInfo> CreateBranchAsync(
        string agentId,
        string sessionId,
        AgentTuiCreateBranchRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentTuiBranchInfo> ForkBranchAsync(
        string agentId,
        string sessionId,
        string sourceBranchId,
        AgentTuiForkBranchRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentTuiBranchInfo> UpdateBranchAsync(
        string sessionId,
        string branchId,
        AgentTuiBranchUpdate update,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentTuiBranchInfo>> GetSiblingBranchesAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default);

    Task DeleteBranchAsync(
        string sessionId,
        string branchId,
        bool recursive = false,
        CancellationToken cancellationToken = default);
}

public interface IAgentTuiSessionBranchRuntime : IAgentTuiSessionRuntime, IAgentTuiBranchRuntime
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

public sealed record AgentTuiCreateBranchRequest(
    string? BranchId = null,
    string? Name = null,
    string? Description = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public sealed record AgentTuiForkBranchRequest(
    string FromMessageId,
    string? NewBranchId = null,
    string? Name = null,
    string? Description = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public sealed record AgentTuiBranchUpdate(
    string? Name = null,
    string? Description = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public sealed record AgentTuiBranchInfo(
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
    string? OriginalBranchId = null,
    string? PreviousSiblingId = null,
    string? NextSiblingId = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);
