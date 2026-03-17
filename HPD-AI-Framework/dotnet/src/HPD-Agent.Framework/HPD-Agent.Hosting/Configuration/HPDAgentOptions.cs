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
    /// Owns session lifecycle (list, create, delete) and is shared with the agent for branch persistence.
    /// Defaults to <see cref="InMemorySessionStore"/> if not set.
    /// Use <see cref="JsonSessionStore"/> for persistence across restarts.
    /// </summary>
    /// <remarks>
    /// The hosting layer owns the store, not the AgentBuilder. The store is created at startup
    /// so that session/branch endpoints work before any agent is built. When a stream request
    /// arrives, the same store is passed into the AgentBuilder automatically — do not also
    /// call WithSessionStore() inside <see cref="ConfigureAgent"/>.
    /// </remarks>
    /// <summary>
    /// Path to a directory where sessions are persisted as JSON files.
    /// When set, a <see cref="JsonSessionStore"/> is created automatically.
    /// Ignored when <see cref="SessionStore"/> is set explicitly.
    /// </summary>
    public string? SessionStorePath { get; set; }

    public ISessionStore? SessionStore { get; set; }

    /// <summary>
    /// Whether to automatically persist conversation history after each completed turn.
    /// Only meaningful when <see cref="SessionStore"/> is a durable store (e.g. <see cref="JsonSessionStore"/>).
    /// Default: false.
    /// </summary>
    public bool PersistAfterTurn { get; set; } = false;

    /// <summary>
    /// Serializable agent configuration.
    /// If set, seeds the AgentBuilder before ConfigureAgent runs.
    /// Because AgentConfig is JSON-serializable, it can be loaded from files,
    /// databases, or API payloads — enabling no-code agent definition.
    /// Takes priority over AgentConfigPath.
    /// </summary>
    public AgentConfig? AgentConfig { get; set; }

    /// <summary>Alias for <see cref="AgentConfig"/>. Used by the agent manager pipeline.</summary>
    public AgentConfig? DefaultAgentConfig
    {
        get => AgentConfig;
        set => AgentConfig = value;
    }

    /// <summary>
    /// Path to a JSON file containing an AgentConfig.
    /// Loaded once per agent build. Ignored if AgentConfig is set.
    /// </summary>
    public string? AgentConfigPath { get; set; }

    /// <summary>Alias for <see cref="AgentConfigPath"/>. Used by the agent manager pipeline.</summary>
    public string? DefaultAgentConfigPath
    {
        get => AgentConfigPath;
        set => AgentConfigPath = value;
    }

    /// <summary>
    /// Agent store for resolving stored agent definitions.
    /// Defaults to <see cref="InMemoryAgentStore"/> if not set.
    /// </summary>
    public IAgentStore? AgentStore { get; set; }

    /// <summary>
    /// Callback to configure the AgentBuilder for each new session.
    /// Called after AgentConfig/AgentConfigPath are applied.
    /// Use this for runtime-only concerns (compiled type references, DI services).
    /// </summary>
    /// <remarks>
    /// The AgentBuilder is pre-configured with the <see cref="SessionStore"/> and any
    /// AgentConfig/AgentConfigPath settings. Use this callback for agent behavior only —
    /// providers, tools, middleware, instructions. Do not call WithSessionStore() here;
    /// set <see cref="SessionStore"/> directly instead.
    /// </remarks>
    public Action<AgentBuilder>? ConfigureAgent { get; set; }

    /// <summary>
    /// How long an agent can sit idle before eviction from the in-memory cache.
    /// Only agents that are not actively streaming are eligible for eviction.
    /// Default: 30 minutes.
    /// </summary>
    public TimeSpan AgentIdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Whether to allow recursive branch deletion via DELETE /branches/{id}?recursive=true.
    /// When false (default), deleting a branch with children is rejected — callers must
    /// delete leaf branches manually. When true, the entire subtree is deleted atomically.
    /// Enable only if your UI explicitly surfaces this as a deliberate "delete subtree" action.
    /// Default: false.
    /// </summary>
    public bool AllowRecursiveBranchDelete { get; set; } = false;
}
