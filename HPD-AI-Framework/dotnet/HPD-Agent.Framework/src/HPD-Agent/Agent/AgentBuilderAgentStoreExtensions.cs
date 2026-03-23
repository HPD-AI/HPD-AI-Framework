namespace HPD.Agent;

/// <summary>
/// <see cref="AgentBuilder"/> extension methods for configuring an <see cref="IAgentStore"/>.
/// </summary>
public static class AgentBuilderAgentStoreExtensions
{
    /// <summary>
    /// Configures the agent store used to resolve <see cref="StoredAgent"/> definitions at runtime.
    /// Required when using sub-agents via <c>StoredAgentId</c>.
    /// </summary>
    public static AgentBuilder WithAgentStore(this AgentBuilder builder, IAgentStore store)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(store);
        builder.Config.AgentStore = store;
        return builder;
    }
}
