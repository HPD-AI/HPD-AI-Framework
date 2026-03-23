namespace HPD.Agent;

/// <summary>
/// Extension methods for AgentBuilder to configure session persistence.
/// </summary>
public static class AgentBuilderSessionExtensions
{
    /// <summary>
    /// Configures the session store for the agent.
    /// Auto-save after each turn is enabled by default when a store is explicitly configured.
    /// Crash recovery via uncommitted turns is automatic when a store is configured.
    /// </summary>
    public static AgentBuilder WithSessionStore(
        this AgentBuilder builder,
        ISessionStore store)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(store);

        builder.Config.SessionStore = store;
        builder.Config.SessionStoreOptions = new SessionStoreOptions { PersistAfterTurn = true };
        return builder;
    }

    /// <summary>
    /// Configures the session store for the agent with optional auto-persistence.
    /// Crash recovery via uncommitted turns is automatic when a store is configured.
    /// </summary>
    public static AgentBuilder WithSessionStore(
        this AgentBuilder builder,
        ISessionStore store,
        bool persistAfterTurn)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(store);

        builder.Config.SessionStore = store;
        builder.Config.SessionStoreOptions = new SessionStoreOptions { PersistAfterTurn = persistAfterTurn };
        return builder;
    }

    /// <summary>
    /// Configures the session store for the agent with full options control.
    /// </summary>
    public static AgentBuilder WithSessionStore(
        this AgentBuilder builder,
        ISessionStore store,
        Action<SessionStoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SessionStoreOptions();
        configure(options);

        builder.Config.SessionStore = store;
        builder.Config.SessionStoreOptions = options;
        return builder;
    }

    /// <summary>
    /// Convenience overload with file-based storage.
    /// </summary>
    public static AgentBuilder WithSessionStore(
        this AgentBuilder builder,
        string storagePath,
        bool persistAfterTurn = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);

        var store = new JsonSessionStore(storagePath);
        return builder.WithSessionStore(store, persistAfterTurn);
    }
}
