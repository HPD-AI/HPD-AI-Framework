using HPD.Agent;
using HPD.Agent.Packages;
using HPD.Agent.Providers;

namespace HPD.Agent.Hosting.Configuration;

/// <summary>
/// Configuration options for hosting an HPD Agent.
/// Used by both AspNetCore and MAUI hosting platforms.
/// </summary>
public class HPDAgentConfig
{
    public HPDAgentConfig()
    {
        PackageContributions = new HpdPackageContributionStores(
            AgentContributors,
            ProviderContributions);
    }

    /// <summary>
    /// The session store to use for this agent.
    /// Owns session lifecycle (list, create, delete) and is shared with the agent for thread persistence.
    /// Defaults to <see cref="InMemorySessionStore"/> if not set.
    /// Use <see cref="JsonSessionStore"/> for persistence across restarts.
    /// </summary>
    /// <remarks>
    /// The hosting layer owns the store, not the AgentBuilder. The store is created at startup
    /// so that session/thread endpoints work before any agent is built. When a stream request
    /// arrives, the same store is passed into the AgentBuilder automatically.
    /// </remarks>
    /// <summary>
    /// Path to a directory where sessions are persisted as JSON files.
    /// When set, a <see cref="JsonSessionStore"/> is created automatically.
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
    /// use the same storage instance. Set this property instead of adding content storage
    /// from an agent contributor.
    /// </remarks>
    public IContentStore? ContentStore { get; set; }

    /// <summary>
    /// Whether to automatically persist conversation history after each completed turn.
    /// Only meaningful when <see cref="SessionStore"/> is a durable store (e.g. <see cref="JsonSessionStore"/>).
    /// Default: false.
    /// </summary>
    public bool PersistAfterTurn { get; set; } = false;

    /// <summary>
    /// Serializable default agent definition.
    /// If set, seeds the AgentBuilder before agent contributors run.
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
    /// Ordered contributors applied to every hosted agent build after the stored/default
    /// agent definition and hosting-owned stores are applied.
    /// </summary>
    public AgentBuilderContributorStore AgentContributors { get; } = new();

    /// <summary>
    /// Provider contributions applied to every hosted agent build after globally discovered
    /// providers and before the agent is built.
    /// </summary>
    public ProviderContributionStore ProviderContributions { get; } = new();

    /// <summary>
    /// Backend package contribution stores used by package managers for this hosted agent.
    /// </summary>
    public HpdPackageContributionStores PackageContributions { get; }

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
