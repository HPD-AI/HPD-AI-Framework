namespace HPD.Agent;

/// <summary>
/// Extension methods for AgentBuilder to configure session persistence.
/// </summary>
public static class AgentBuilderSessionExtensions
{
    public static AgentBuilder WithSessionRepository(
        this AgentBuilder builder,
        ISessionRepository repository)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(repository);

        builder.Config.SessionRepository = repository;
        builder.Config.SessionRepositoryOptions = new SessionRepositoryOptions { PersistAfterTurn = true };
        return builder;
    }

    public static AgentBuilder WithSessionRepository(
        this AgentBuilder builder,
        ISessionRepository repository,
        bool persistAfterTurn)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(repository);

        builder.Config.SessionRepository = repository;
        builder.Config.SessionRepositoryOptions = new SessionRepositoryOptions { PersistAfterTurn = persistAfterTurn };
        return builder;
    }

    public static AgentBuilder WithSessionRepository(
        this AgentBuilder builder,
        ISessionRepository repository,
        Action<SessionRepositoryOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SessionRepositoryOptions();
        configure(options);

        builder.Config.SessionRepository = repository;
        builder.Config.SessionRepositoryOptions = options;
        return builder;
    }
}
