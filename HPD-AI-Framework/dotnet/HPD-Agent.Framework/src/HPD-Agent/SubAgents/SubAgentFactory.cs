using HPD.Agent;

namespace HPD.Agent;

/// <summary>
/// Factory for creating SubAgent objects with AgentConfig
/// </summary>
public static class SubAgentFactory
{
    /// <summary>
    /// Creates a sub-agent that can be invoked as a tool/function by parent agents.
    /// The sub-agent is configured using AgentConfig which defines its behavior, provider, Toolkits, etc.
    /// </summary>
    /// <param name="name">Sub-agent name (REQUIRED - becomes AIFunction name shown to parent agent)</param>
    /// <param name="description">Description shown in tool list (REQUIRED - becomes AIFunction description)</param>
    /// <param name="agentConfig">Agent configuration defining the sub-agent's behavior</param>
    /// <param name="Toolkits">Optional Toolkit types to register with the sub-agent (e.g., typeof(FileSystemToolkit))</param>
    /// <returns>SubAgent object processed by source generator and converted to AIFunction at runtime</returns>
    public static SubAgent Create(
        string name,
        string description,
        AgentConfig agentConfig,
        params Type[] Toolkits)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Sub-agent name cannot be empty", nameof(name));

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Sub-agent description cannot be empty", nameof(description));

        if (agentConfig == null)
            throw new ArgumentNullException(nameof(agentConfig), "AgentConfig cannot be null");

        return new SubAgent
        {
            Name = name,
            Description = description,
            AgentConfig = agentConfig,
            SessionMode = SubAgentSessionMode.Stateless,  // Default to stateless
            ToolkitTypes = Toolkits ?? Array.Empty<Type>()
        };
    }

    /// <summary>
    /// Creates a sub-agent with a shared session for stateful multi-turn conversations.
    /// A new shared session will be created automatically for maintaining context.
    /// WARNING: Shared sessions are not thread-safe - avoid concurrent usage!
    /// </summary>
    /// <param name="name">Sub-agent name (REQUIRED - becomes AIFunction name shown to parent agent)</param>
    /// <param name="description">Description shown in tool list (REQUIRED - becomes AIFunction description)</param>
    /// <param name="agentConfig">Agent configuration defining the sub-agent's behavior</param>
    /// <param name="toolTypes">Optional Toolkit types to register with the sub-agent (e.g., typeof(FileSystemToolkit))</param>
    /// <returns>SubAgent object configured with shared session</returns>
    public static SubAgent CreateStateful(
        string name,
        string description,
        AgentConfig agentConfig,
        params Type[] toolTypes)
    {
        var subAgent = Create(name, description, agentConfig, toolTypes);
        subAgent.SessionMode = SubAgentSessionMode.SharedSession;
        subAgent.SharedSessionId = Guid.NewGuid().ToString("N");
        return subAgent;
    }

    /// <summary>
    /// Creates a sub-agent that inherits the parent agent's current session and branch as read-only context.
    /// The sub-agent sees the parent's conversation history but does not write back to it.
    /// Falls back to stateless if no parent session is available at invocation time.
    /// </summary>
    /// <param name="name">Sub-agent name (REQUIRED - becomes AIFunction name shown to parent agent)</param>
    /// <param name="description">Description shown in tool list (REQUIRED - becomes AIFunction description)</param>
    /// <param name="agentConfig">Agent configuration defining the sub-agent's behavior</param>
    /// <param name="toolTypes">Optional Toolkit types to register with the sub-agent (e.g., typeof(FileSystemToolkit))</param>
    /// <returns>SubAgent object configured for parent-context inheritance</returns>
    public static SubAgent CreatePerSession(
        string name,
        string description,
        AgentConfig agentConfig,
        params Type[] toolTypes)
    {
        var subAgent = Create(name, description, agentConfig, toolTypes);
        subAgent.SessionMode = SubAgentSessionMode.PerSession;
        return subAgent;
    }
}
