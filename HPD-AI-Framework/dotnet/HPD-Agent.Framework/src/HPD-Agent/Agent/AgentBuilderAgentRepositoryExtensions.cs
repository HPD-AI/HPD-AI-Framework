namespace HPD.Agent;

/// <summary>
/// <see cref="AgentBuilder"/> extension methods for configuring stored agent definitions.
/// </summary>
public static class AgentBuilderAgentRepositoryExtensions
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

    public static AgentBuilder WithAgentRepository(this AgentBuilder builder, IAgentRepository repository)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(repository);

        builder.Config.AgentRepository = repository;
        return builder;
    }

    public static AgentBuilder WithAgentRepository(
        this AgentBuilder builder,
        IAgentRepository repository,
        bool persistOnBuild)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(repository);

        builder.Config.AgentRepository = repository;
        builder.Config.AgentRepositoryOptions = new AgentRepositoryOptions { PersistOnBuild = persistOnBuild };
        return builder;
    }

    public static AgentBuilder WithAgentRepository(
        this AgentBuilder builder,
        IAgentRepository repository,
        Action<AgentRepositoryOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new AgentRepositoryOptions();
        configure(options);

        builder.Config.AgentRepository = repository;
        builder.Config.AgentRepositoryOptions = options;
        return builder;
    }

    /// <summary>
    /// Configures agent definitions, sessions, branches, and content to use one
    /// file-backed workspace substrate.
    /// </summary>
    public static AgentBuilder WithJsonWorkspace(
        this AgentBuilder builder,
        string storagePath,
        bool persistOnBuild = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);

        var workspace = new JsonWorkspaceStore(storagePath);
        builder.Config.AgentRepository = new WorkspaceAgentRepository(workspace);
        builder.Config.AgentRepositoryOptions = new AgentRepositoryOptions { PersistOnBuild = persistOnBuild };
        builder.Config.SessionRepository = new WorkspaceSessionRepository(workspace);
        builder.Config.SessionRepositoryOptions = new SessionRepositoryOptions { PersistAfterTurn = true };
        builder.ConfigureWorkspaceStore(workspace);
        return builder;
    }
}
