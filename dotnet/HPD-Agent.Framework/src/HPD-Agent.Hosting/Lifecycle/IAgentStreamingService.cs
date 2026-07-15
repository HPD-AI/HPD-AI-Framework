using HPD.Agent;
using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Lifecycle;

public interface IAgentStreamingService
{
    Task<AgentServiceResult<AgentStreamLease>> GetAgentForThreadAsync(
        string agentId,
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<InputSubmissionDto>> SubmitInputAsync(
        string agentId,
        string sessionId,
        string threadId,
        AgentInputEvent input,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<ThreadRuntimeStateDto>> GetThreadStateAsync(
        string agentId,
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<ThreadContextUsage>> EstimateContextUsageAsync(
        string agentId,
        string sessionId,
        string threadId,
        AgentRunConfig? runConfig,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<InterruptionSubmissionDto>> InterruptAsync(
        string agentId,
        string sessionId,
        string threadId,
        string? expectedRuntimeRunId,
        InterruptionRequestEvent interruption,
        CancellationToken cancellationToken = default);

    AgentInputEvent ApplyRouteScope(
        AgentInputEvent input,
        string agentId,
        string sessionId,
        string threadId,
        string? runtimeRunId = null);
}

public sealed record AgentStreamLease(Agent Agent);
