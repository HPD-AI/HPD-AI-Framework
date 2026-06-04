using HPD.Agent;

namespace HPD.Agent.Hosting.Configuration;

/// <summary>
/// Configuration options for hosting an HPD Agent.
/// Used by both AspNetCore and MAUI hosting platforms.
/// </summary>
public class HPDAgentConfig
{
    /// <summary>
    /// Workspace store used as the single persistence substrate for hosted sessions,
    /// branches, stored agents, framework documents, and content attachments.
    /// Defaults to <see cref="InMemoryWorkspaceStore"/>.
    /// </summary>
    public IWorkspaceStore? WorkspaceStore { get; set; }

    /// <summary>
    /// Path to a directory where the workspace is persisted as JSON.
    /// Prefer <see cref="UseJsonWorkspace"/> for fluent configuration.
    /// Ignored when <see cref="WorkspaceStore"/> is set explicitly.
    /// </summary>
    public string? WorkspaceStorePath { get; set; }

    /// <summary>
    /// Whether to automatically persist conversation history after each completed turn.
    /// Uses the configured workspace-backed session repository.
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
    /// Whether hosted agent builds should persist synthesized or updated definitions back
    /// to the configured workspace-backed agent repository.
    /// Default: true.
    /// </summary>
    public bool PersistAgentDefinitionsOnBuild { get; set; } = true;

    /// <summary>
    /// Callback to configure the AgentBuilder for each new session.
    /// Called after AgentConfig/AgentConfigPath are applied.
    /// Use this for runtime-only concerns (compiled type references, DI services).
    /// </summary>
    /// <remarks>
    /// The AgentBuilder is pre-configured with workspace-backed repositories and any
    /// AgentConfig/AgentConfigPath settings. Use this callback for agent behavior only:
    /// providers, tools, middleware, instructions.
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

public static class HPDAgentConfigWorkspaceExtensions
{
    public static HPDAgentConfig UseDefaultAgent(
        this HPDAgentConfig config,
        AgentConfig agentConfig)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.DefaultAgentConfig = agentConfig ?? throw new ArgumentNullException(nameof(agentConfig));
        config.DefaultAgentConfigPath = null;
        return config;
    }

    public static HPDAgentConfig UseDefaultAgent(
        this HPDAgentConfig config,
        string agentConfigPath)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentConfigPath);

        config.DefaultAgentConfig = null;
        config.DefaultAgentConfigPath = agentConfigPath;
        return config;
    }

    public static HPDAgentConfig UseWorkspaceStore(
        this HPDAgentConfig config,
        IWorkspaceStore workspaceStore)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.WorkspaceStore = workspaceStore ?? throw new ArgumentNullException(nameof(workspaceStore));
        return config;
    }

    public static HPDAgentConfig UseInMemoryWorkspace(this HPDAgentConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.WorkspaceStore = new InMemoryWorkspaceStore();
        config.WorkspaceStorePath = null;
        return config;
    }

    public static HPDAgentConfig UseJsonWorkspace(
        this HPDAgentConfig config,
        string workspacePath)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

        config.WorkspaceStore = new JsonWorkspaceStore(workspacePath);
        config.WorkspaceStorePath = workspacePath;
        return config;
    }
}
