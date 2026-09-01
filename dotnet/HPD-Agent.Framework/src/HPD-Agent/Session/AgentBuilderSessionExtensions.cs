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

        if (builder._sessionStoreFactory is not null)
            throw new InvalidOperationException("An explicit session store cannot be combined with a session-store factory.");

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

        if (builder._sessionStoreFactory is not null)
            throw new InvalidOperationException("An explicit session store cannot be combined with a session-store factory.");

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

        if (builder._sessionStoreFactory is not null)
            throw new InvalidOperationException("An explicit session store cannot be combined with a session-store factory.");

        var options = new SessionStoreOptions();
        configure(options);

        builder.Config.SessionStore = store;
        builder.Config.SessionStoreOptions = options;
        return builder;
    }

    /// <summary>Creates an in-memory session store from the resolved application event codec.</summary>
    public static AgentBuilder WithInMemorySessionStore(this AgentBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        EnsureFactoryCanBeSelected(builder);
        builder._sessionStoreFactory = composition => new InMemorySessionStore(composition.Codec);
        builder.Config.SessionStoreOptions = new SessionStoreOptions { PersistAfterTurn = true };
        return builder;
    }

    /// <summary>
    /// Creates a file session store from the resolved application event codec and selects a
    /// restart-durable sibling content store unless an explicit content store is supplied.
    /// </summary>
    public static AgentBuilder WithFileSessionStore(this AgentBuilder builder, string path)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsureFactoryCanBeSelected(builder);
        builder._sessionStoreFactory = composition => new FileSessionStore(path, composition.Codec);
        builder._implicitContentStoreFactory = () => new LocalFileContentStore(Path.Combine(path, "content"));
        builder.Config.SessionStoreOptions = new SessionStoreOptions { PersistAfterTurn = true };
        return builder;
    }

    private static void EnsureFactoryCanBeSelected(AgentBuilder builder)
    {
        if (builder.Config.SessionStore is not null)
            throw new InvalidOperationException("A session-store factory cannot be combined with an explicit session store.");
        if (builder._sessionStoreFactory is not null)
            throw new InvalidOperationException("Only one session-store factory may be configured.");
    }

}
