namespace HPD.Agent.TUI.Runtime;

public sealed record AgentTuiRuntimeScope
{
    public AgentTuiRuntimeScope(
        string agentId,
        string sessionId,
        string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        AgentId = agentId;
        SessionId = sessionId;
        ThreadId = threadId;
    }

    public string AgentId { get; init; }

    public string SessionId { get; init; }

    public string ThreadId { get; init; }
}
