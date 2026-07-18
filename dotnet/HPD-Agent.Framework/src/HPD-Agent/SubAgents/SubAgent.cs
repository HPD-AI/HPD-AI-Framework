namespace HPD.Agent;

/// <summary>
/// Identifies how a durable subagent definition obtains its configuration.
/// </summary>
public abstract record SubAgentConfigurationSource;

/// <summary>
/// Uses the configuration supplied by the ToolHarness declaration.
/// </summary>
/// <param name="Config">The complete child agent configuration.</param>
public sealed record SuppliedAgentConfiguration(AgentConfig Config) : SubAgentConfigurationSource;

/// <summary>
/// Creates the child definition from an immutable serializable snapshot of the parent configuration.
/// </summary>
public sealed record ParentAgentConfiguration : SubAgentConfigurationSource;

/// <summary>
/// Resolves an existing definition from the configured <see cref="IAgentStore"/>.
/// </summary>
public sealed record StoredAgentConfiguration : SubAgentConfigurationSource;

/// <summary>
/// Represents a callable sub-agent - another agent that can be invoked as a tool/function.
/// </summary>
public sealed class SubAgent
{
    /// <summary>
    /// Sub-agent name (REQUIRED - becomes AIFunction name shown to parent agent).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Description shown in tool list (REQUIRED - becomes AIFunction description).
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Stable identity used to store, reconstruct, and route the child agent.
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// Configuration source for the durable child definition.
    /// </summary>
    public required SubAgentConfigurationSource Configuration { get; init; }

    /// <summary>
    /// Session and thread routing policy for sub-agent execution.
    /// </summary>
    public SubAgentExecutionPolicy ExecutionPolicy { get; init; } = SubAgentExecutionPolicy.Default;

    /// <summary>
    /// Defines whether this subagent runs synchronously, in the background, or lets the model choose per call.
    /// </summary>
    public AgentInvocationModePolicy InvocationModePolicy { get; init; } =
        AgentInvocationModePolicy.SynchronousOnly;

    /// <summary>
    /// Rule used when this subagent is invoked as runtime-owned background work.
    /// </summary>
    public BackgroundTaskNotificationRule BackgroundNotification { get; init; } =
        new BackgroundTaskNotificationRule.OnFinalStateRule(Completed: true, Faulted: true);

    /// <summary>
    /// ToolHarness types to register with the sub-agent.
    /// </summary>
    public Type[] ToolHarnessTypes { get; init; } = Array.Empty<Type>();

    /// <summary>
    /// Optional thread metadata defaults applied to subagent-created threads.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    public static SubAgent FromConfig(
        string agentId,
        string name,
        string description,
        AgentConfig agentConfig,
        SubAgentExecutionPolicy? executionPolicy = null,
        params Type[] toolharnessTypes)
        => FromConfig(
            agentId,
            name,
            description,
            agentConfig,
            executionPolicy,
            metadata: null,
            invocationModePolicy: AgentInvocationModePolicy.SynchronousOnly,
            backgroundNotification: null,
            toolharnessTypes);

    public static SubAgent FromConfig(
        string agentId,
        string name,
        string description,
        AgentConfig agentConfig,
        SubAgentExecutionPolicy? executionPolicy,
        Dictionary<string, object>? metadata,
        params Type[] toolharnessTypes)
        => FromConfig(
            agentId,
            name,
            description,
            agentConfig,
            executionPolicy,
            metadata,
            AgentInvocationModePolicy.SynchronousOnly,
            backgroundNotification: null,
            toolharnessTypes);

    /// <summary>
    /// Creates an inline-config subagent definition.
    /// </summary>
    /// <param name="agentId">The stable stored identity of the child agent.</param>
    /// <param name="name">The model-facing subagent tool name.</param>
    /// <param name="description">The model-facing subagent tool description.</param>
    /// <param name="agentConfig">The child agent configuration.</param>
    /// <param name="executionPolicy">The child session and thread routing policy.</param>
    /// <param name="metadata">Optional metadata applied to subagent-created threads.</param>
    /// <param name="invocationModePolicy">The allowed synchronous/background invocation policy.</param>
    /// <param name="backgroundNotification">The notification rule used for background invocations.</param>
    /// <param name="toolharnessTypes">Tool harness types registered on the child agent.</param>
    /// <returns>The subagent definition.</returns>
    public static SubAgent FromConfig(
        string agentId,
        string name,
        string description,
        AgentConfig agentConfig,
        SubAgentExecutionPolicy? executionPolicy,
        Dictionary<string, object>? metadata,
        AgentInvocationModePolicy invocationModePolicy,
        BackgroundTaskNotificationRule? backgroundNotification,
        params Type[] toolharnessTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ValidateNameAndDescription(name, description);
        ArgumentNullException.ThrowIfNull(agentConfig);

        var policy = executionPolicy ?? SubAgentExecutionPolicy.Default;
        policy.Validate();

        return new SubAgent
        {
            AgentId = agentId,
            Name = name,
            Description = description,
            Configuration = new SuppliedAgentConfiguration(agentConfig),
            ExecutionPolicy = policy,
            InvocationModePolicy = invocationModePolicy,
            BackgroundNotification = backgroundNotification
                ?? new BackgroundTaskNotificationRule.OnFinalStateRule(Completed: true, Faulted: true),
            ToolHarnessTypes = toolharnessTypes ?? Array.Empty<Type>(),
            Metadata = metadata
        };
    }

    public static SubAgent FromAgentId(
        string agentId,
        string name,
        string description,
        SubAgentExecutionPolicy? executionPolicy = null,
        params Type[] toolharnessTypes)
        => FromAgentId(
            agentId,
            name,
            description,
            executionPolicy,
            metadata: null,
            invocationModePolicy: AgentInvocationModePolicy.SynchronousOnly,
            backgroundNotification: null,
            toolharnessTypes);

    /// <summary>
    /// Creates a stored-agent subagent definition.
    /// </summary>
    /// <param name="agentId">The stored child agent id.</param>
    /// <param name="name">The model-facing subagent tool name.</param>
    /// <param name="description">The model-facing subagent tool description.</param>
    /// <param name="executionPolicy">The child session and thread routing policy.</param>
    /// <param name="metadata">Optional metadata applied to subagent-created threads.</param>
    /// <param name="invocationModePolicy">The allowed synchronous/background invocation policy.</param>
    /// <param name="backgroundNotification">The notification rule used for background invocations.</param>
    /// <param name="toolharnessTypes">Tool harness types registered on the child agent.</param>
    /// <returns>The subagent definition.</returns>
    public static SubAgent FromAgentId(
        string agentId,
        string name,
        string description,
        SubAgentExecutionPolicy? executionPolicy,
        Dictionary<string, object>? metadata,
        params Type[] toolharnessTypes)
        => FromAgentId(
            agentId,
            name,
            description,
            executionPolicy,
            metadata,
            AgentInvocationModePolicy.SynchronousOnly,
            backgroundNotification: null,
            toolharnessTypes);

    public static SubAgent FromAgentId(
        string agentId,
        string name,
        string description,
        SubAgentExecutionPolicy? executionPolicy,
        Dictionary<string, object>? metadata,
        AgentInvocationModePolicy invocationModePolicy,
        BackgroundTaskNotificationRule? backgroundNotification,
        params Type[] toolharnessTypes)
    {
        ValidateNameAndDescription(name, description);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var policy = executionPolicy ?? SubAgentExecutionPolicy.Default;
        policy.Validate();

        return new SubAgent
        {
            AgentId = agentId,
            Name = name,
            Description = description,
            Configuration = new StoredAgentConfiguration(),
            ExecutionPolicy = policy,
            InvocationModePolicy = invocationModePolicy,
            BackgroundNotification = backgroundNotification
                ?? new BackgroundTaskNotificationRule.OnFinalStateRule(Completed: true, Faulted: true),
            ToolHarnessTypes = toolharnessTypes ?? Array.Empty<Type>(),
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a durable subagent definition from a snapshot of the effective parent configuration.
    /// </summary>
    public static SubAgent FromParent(
        string agentId,
        string name,
        string description,
        SubAgentExecutionPolicy? executionPolicy = null,
        Dictionary<string, object>? metadata = null,
        AgentInvocationModePolicy invocationModePolicy = AgentInvocationModePolicy.SynchronousOnly,
        BackgroundTaskNotificationRule? backgroundNotification = null,
        params Type[] toolharnessTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ValidateNameAndDescription(name, description);
        var policy = executionPolicy ?? SubAgentExecutionPolicy.Default;
        policy.Validate();

        return new SubAgent
        {
            AgentId = agentId,
            Name = name,
            Description = description,
            Configuration = new ParentAgentConfiguration(),
            ExecutionPolicy = policy,
            InvocationModePolicy = invocationModePolicy,
            BackgroundNotification = backgroundNotification
                ?? new BackgroundTaskNotificationRule.OnFinalStateRule(Completed: true, Faulted: true),
            ToolHarnessTypes = toolharnessTypes ?? [],
            Metadata = metadata
        };
    }

    private static void ValidateNameAndDescription(string name, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
    }

}

public enum SubAgentSessionPolicy
{
    ParentSession,
    NewSession,
    SharedSession
}

public enum SubAgentThreadPolicy
{
    ForkFromParentThread,
    FreshThread,
    ExistingThread
}

public sealed record SubAgentExecutionPolicy(
    SubAgentSessionPolicy SessionPolicy,
    SubAgentThreadPolicy ThreadPolicy,
    string? SharedSessionId = null,
    string? ExistingThreadId = null,
    string? ThreadNamePrefix = null,
    ThreadForkCompaction? ThreadCompaction = null)
{
    public static SubAgentExecutionPolicy Default { get; } =
        new(SubAgentSessionPolicy.ParentSession, SubAgentThreadPolicy.ForkFromParentThread);
}

public static class SubAgentExecutionPolicies
{
    public static SubAgentExecutionPolicy ParentSessionForkedThread(
        ThreadForkCompaction? compaction = null) =>
        new(
            SubAgentSessionPolicy.ParentSession,
            SubAgentThreadPolicy.ForkFromParentThread,
            ThreadCompaction: compaction);

    public static SubAgentExecutionPolicy ParentSessionFreshThread() =>
        new(SubAgentSessionPolicy.ParentSession, SubAgentThreadPolicy.FreshThread);

    public static SubAgentExecutionPolicy NewSession() =>
        new(SubAgentSessionPolicy.NewSession, SubAgentThreadPolicy.FreshThread);

    public static SubAgentExecutionPolicy SharedSessionFreshThread(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return new(
            SubAgentSessionPolicy.SharedSession,
            SubAgentThreadPolicy.FreshThread,
            SharedSessionId: sessionId);
    }

    public static SubAgentExecutionPolicy SharedSessionExistingThread(string sessionId, string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        return new(
            SubAgentSessionPolicy.SharedSession,
            SubAgentThreadPolicy.ExistingThread,
            SharedSessionId: sessionId,
            ExistingThreadId: threadId);
    }

    public static SubAgentExecutionPolicy ExistingThread(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        return new(SubAgentSessionPolicy.ParentSession, SubAgentThreadPolicy.ExistingThread, ExistingThreadId: threadId);
    }
}

internal static class SubAgentExecutionPolicyExtensions
{
    public static void Validate(this SubAgentExecutionPolicy policy)
    {
        if (policy.SessionPolicy == SubAgentSessionPolicy.SharedSession)
        {
            if (string.IsNullOrWhiteSpace(policy.SharedSessionId))
                throw new ArgumentException("SharedSessionId is required when SessionPolicy is SharedSession.");
        }
        else if (!string.IsNullOrWhiteSpace(policy.SharedSessionId))
        {
            throw new ArgumentException("SharedSessionId is only valid when SessionPolicy is SharedSession.");
        }

        if (policy.ThreadPolicy == SubAgentThreadPolicy.ExistingThread)
        {
            if (string.IsNullOrWhiteSpace(policy.ExistingThreadId))
                throw new ArgumentException("ExistingThreadId is required when ThreadPolicy is ExistingThread.");
        }
        else if (!string.IsNullOrWhiteSpace(policy.ExistingThreadId))
        {
            throw new ArgumentException("ExistingThreadId is only valid when ThreadPolicy is ExistingThread.");
        }

        if (policy.ThreadPolicy == SubAgentThreadPolicy.ForkFromParentThread &&
            policy.SessionPolicy != SubAgentSessionPolicy.ParentSession)
        {
            throw new ArgumentException("ForkFromParentThread requires SessionPolicy to be ParentSession.");
        }

        if (policy.ThreadCompaction != null &&
            policy.ThreadPolicy != SubAgentThreadPolicy.ForkFromParentThread)
        {
            throw new ArgumentException("ThreadCompaction can only be set when ThreadPolicy is ForkFromParentThread.");
        }
    }

}
