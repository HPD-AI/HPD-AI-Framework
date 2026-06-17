using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Lifecycle;

public interface IAgentThreadService
{
    Task<AgentServiceResult<IReadOnlyList<ThreadDto>>> ListThreadsAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<ThreadDto>> GetThreadAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<ThreadDto>> CreateThreadAsync(
        string agentId,
        string sessionId,
        CreateThreadRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<ThreadDto>> ForkThreadAsync(
        string agentId,
        string sessionId,
        string threadId,
        ForkThreadRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<ThreadDto>> UpdateThreadAsync(
        string sessionId,
        string threadId,
        UpdateThreadRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult> DeleteThreadAsync(
        string sessionId,
        string threadId,
        bool recursive = false,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<IReadOnlyList<AgentEvent>>> GetEventsAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<IReadOnlyList<ThreadDto>>> GetSiblingsAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default);
}
