using HPD.Agent;

namespace HPD.Agent.Hosting.Configuration;

/// <summary>
/// Configuration options for hosting an HPD Agent.
/// Used by both AspNetCore and MAUI hosting platforms.
/// </summary>
public class HPDAgentConfig
{
    /// <summary>
    /// The session store to use for this agent.
    /// Owns session lifecycle (list, create, delete) and is shared with the agent for thread persistence.
    /// Defaults to <see cref="InMemorySessionStore"/> if not set.
    /// Use <see cref="FileSessionStore"/> for persistence across restarts.
    /// </summary>
    /// <remarks>
    /// The hosting layer owns the store, not the AgentBuilder. The store is created at startup
    /// so that session/thread endpoints work before any agent is built. When a stream request
    /// arrives, the same store is passed into the AgentBuilder automatically — do not also
    /// call WithSessionStore() inside <see cref="ConfigureAgent"/>.
    /// </remarks>
    /// <summary>
    /// Path to a directory where sessions are persisted as JSON files.
    /// When set, a <see cref="FileSessionStore"/> is created automatically.
    /// Ignored when <see cref="SessionStore"/> is set explicitly.
    /// </summary>
    public string? SessionStorePath { get; set; }

    public ISessionStore? SessionStore { get; set; }

    /// <summary>
    /// The content store used by content upload/download endpoints and agent content-reference resolution.
    /// Defaults to <see cref="InMemoryContentStore"/> if not set.
    /// Use <see cref="LocalFileContentStore"/> for persistence across restarts.
    /// </summary>
    /// <remarks>
    /// The hosting layer owns the content store so uploaded content and model-call resolution
    /// use the same storage instance. Do not also call WithContentStore() inside
    /// <see cref="ConfigureAgent"/>; set this property instead.
    /// </remarks>
    public IContentStore? ContentStore { get; set; }

    /// <summary>
    /// Whether to automatically persist conversation history after each completed turn.
    /// Only meaningful when <see cref="SessionStore"/> is a durable store (e.g. <see cref="FileSessionStore"/>).
    /// Default: false.
    /// </summary>
    public bool PersistAfterTurn { get; set; } = false;

    /// <summary>
    /// Serializable default agent definition.
    /// If set, seeds the AgentBuilder before ConfigureAgent runs.
    /// Because AgentConfig is JSON-serializable, it can be loaded from files,
    /// databases, or API payloads — enabling no-code agent definition.
    /// Takes priority over <see cref="DefaultAgentPath"/>.
    /// </summary>
    public AgentConfig? DefaultAgent { get; set; }

    /// <summary>
    /// Path to a JSON or YAML file containing the default agent definition.
    /// Loaded once per agent build. Ignored if <see cref="DefaultAgent"/> is set.
    /// </summary>
    public string? DefaultAgentPath { get; set; }

    /// <summary>
    /// JSON agent definition deferred until the hosting container can bind provider-specific
    /// payloads through its generated provider composition.
    /// </summary>
    /// <remarks>
    /// Normally populated by the ASP.NET Core configuration registration extensions.
    /// <see cref="DefaultAgent"/> takes precedence; this property takes precedence over
    /// <see cref="DefaultAgentPath"/>.
    /// </remarks>
    public string? DefaultAgentDocument { get; set; }

    /// <summary>
    /// Agent store for resolving stored agent definitions.
    /// Defaults to <see cref="InMemoryAgentStore"/> if not set.
    /// </summary>
    public IAgentStore? AgentStore { get; set; }

    /// <summary>
    /// Whether hosted agent builds should persist synthesized or updated definitions back
    /// to the configured <see cref="AgentStore"/> after a successful build.
    /// Default: true.
    /// </summary>
    public bool PersistAgentDefinitionsOnBuild { get; set; } = true;

    /// <summary>
    /// Callback to configure the AgentBuilder for each new session.
    /// Called after DefaultAgent/DefaultAgentPath are applied.
    /// Use this for runtime-only concerns (compiled type references, DI services).
    /// </summary>
    /// <remarks>
    /// The AgentBuilder is pre-configured with the <see cref="SessionStore"/> and any
    /// default agent definition. Use this callback for runtime-only enrichment such as
    /// DI-backed services, compiled tools, or server policy. Do not call WithSessionStore()
    /// here; set <see cref="SessionStore"/> directly instead.
    /// </remarks>
    public Action<AgentBuilder>? ConfigureAgent { get; set; }

    /// <summary>
    /// How long an agent can sit idle before eviction from the in-memory cache.
    /// Only agents that are not actively streaming are eligible for eviction.
    /// Default: 30 minutes.
    /// </summary>
    public TimeSpan AgentIdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Whether to allow recursive thread deletion via DELETE /threads/{id}?recursive=true.
    /// When false (default), deleting a thread with children is rejected — callers must
    /// delete leaf threads manually. When true, the entire subtree is deleted atomically.
    /// Enable only if your UI explicitly surfaces this as a deliberate "delete subtree" action.
    /// Default: false.
    /// </summary>
    public bool AllowRecursiveThreadDelete { get; set; } = false;
}
