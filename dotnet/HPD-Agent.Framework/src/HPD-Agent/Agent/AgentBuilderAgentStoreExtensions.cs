namespace HPD.Agent;

/// <summary>
/// <see cref="AgentBuilder"/> extension methods for configuring an <see cref="IAgentStore"/>.
/// </summary>
public static class AgentBuilderAgentStoreExtensions
{
    /// <summary>
    /// Configures the stable stored-agent identity used by <see cref="AgentBuilder"/>
    /// to load and optionally persist <see cref="StoredAgent"/> definitions.
    /// </summary>
    public static AgentBuilder WithAgentId(this AgentBuilder builder, string agentId)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        builder.Config.AgentId = agentId;
        return builder;
    }

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

    /// <summary>
    /// Configures the agent store and whether <see cref="AgentBuilder.BuildAsync"/>
    /// should save the final definition back to it.
    /// </summary>
    public static AgentBuilder WithAgentStore(
        this AgentBuilder builder,
        IAgentStore store,
        bool persistOnBuild)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(store);

        builder.Config.AgentStore = store;
        builder.Config.AgentStoreOptions = new AgentStoreOptions { PersistOnBuild = persistOnBuild };
        return builder;
    }

    /// <summary>
    /// Configures the agent store with full options control.
    /// </summary>
    public static AgentBuilder WithAgentStore(
        this AgentBuilder builder,
        IAgentStore store,
        Action<AgentStoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new AgentStoreOptions();
        configure(options);

        builder.Config.AgentStore = store;
        builder.Config.AgentStoreOptions = options;
        return builder;
    }

    /// <summary>
    /// Convenience overload with file-based agent definition storage.
    /// </summary>
    public static AgentBuilder WithAgentStore(
        this AgentBuilder builder,
        string storagePath,
        bool persistOnBuild = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);

        var store = new JsonAgentStore(storagePath);
        return builder.WithAgentStore(store, persistOnBuild);
    }
}
