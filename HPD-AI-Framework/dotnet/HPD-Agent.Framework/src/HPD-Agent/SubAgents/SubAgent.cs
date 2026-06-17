using HPD.Agent;

namespace HPD.Agent;

/// <summary>
/// Represents a callable sub-agent - another agent that can be invoked as a tool/function.
/// </summary>
public class SubAgent
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
    /// Where the child agent definition comes from.
    /// </summary>
    public SubAgentSourceKind SourceKind { get; init; }

    /// <summary>
    /// Inline agent configuration. Set only when <see cref="SourceKind"/> is <see cref="SubAgentSourceKind.InlineConfig"/>.
    /// </summary>
    public AgentConfig? AgentConfig { get; init; }

    /// <summary>
    /// Stored agent id. Set only when <see cref="SourceKind"/> is <see cref="SubAgentSourceKind.StoredAgent"/>.
    /// </summary>
    public string? AgentId { get; init; }

    /// <summary>
    /// Session and thread routing policy for sub-agent execution.
    /// </summary>
    public SubAgentExecutionPolicy ExecutionPolicy { get; init; } = SubAgentExecutionPolicy.Default;

    /// <summary>
    /// ToolHarness types to register with the sub-agent.
    /// </summary>
    public Type[] ToolHarnessTypes { get; init; } = Array.Empty<Type>();

    /// <summary>
    /// Optional thread metadata defaults applied to subagent-created threads.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    public static SubAgent FromConfig(
        string name,
        string description,
        AgentConfig agentConfig,
        SubAgentExecutionPolicy? executionPolicy = null,
        params Type[] toolharnessTypes)
        => FromConfig(name, description, agentConfig, executionPolicy, metadata: null, toolharnessTypes);

    public static SubAgent FromConfig(
        string name,
        string description,
        AgentConfig agentConfig,
        SubAgentExecutionPolicy? executionPolicy,
        Dictionary<string, object>? metadata,
        params Type[] toolharnessTypes)
    {
        ValidateNameAndDescription(name, description);
        ArgumentNullException.ThrowIfNull(agentConfig);

        var policy = executionPolicy ?? SubAgentExecutionPolicy.Default;
        policy.Validate();

        return new SubAgent
        {
            Name = name,
            Description = description,
            SourceKind = SubAgentSourceKind.InlineConfig,
            AgentConfig = agentConfig,
            AgentId = null,
            ExecutionPolicy = policy,
            ToolHarnessTypes = toolharnessTypes ?? Array.Empty<Type>(),
            Metadata = metadata
        };
    }

    public static SubAgent FromAgentId(
        string name,
        string description,
        string agentId,
        SubAgentExecutionPolicy? executionPolicy = null,
        params Type[] toolharnessTypes)
        => FromAgentId(name, description, agentId, executionPolicy, metadata: null, toolharnessTypes);

    public static SubAgent FromAgentId(
        string name,
        string description,
        string agentId,
        SubAgentExecutionPolicy? executionPolicy,
        Dictionary<string, object>? metadata,
        params Type[] toolharnessTypes)
    {
        ValidateNameAndDescription(name, description);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var policy = executionPolicy ?? SubAgentExecutionPolicy.Default;
        policy.Validate();

        return new SubAgent
        {
            Name = name,
            Description = description,
            SourceKind = SubAgentSourceKind.StoredAgent,
            AgentConfig = null,
            AgentId = agentId,
            ExecutionPolicy = policy,
            ToolHarnessTypes = toolharnessTypes ?? Array.Empty<Type>(),
            Metadata = metadata
        };
    }

    private static void ValidateNameAndDescription(string name, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
    }

}

public enum SubAgentSourceKind
{
    InlineConfig,
    StoredAgent
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
    ExistingThread,
    ParentThread
}

public enum SubAgentThreadCompaction
{
    Inherit,
    Enabled,
    Disabled,
    PreferCache
}

public sealed record SubAgentExecutionPolicy(
    SubAgentSessionPolicy SessionPolicy,
    SubAgentThreadPolicy ThreadPolicy,
    string? SharedSessionId = null,
    string? ExistingThreadId = null,
    string? ThreadNamePrefix = null,
    SubAgentThreadCompaction ThreadCompaction = SubAgentThreadCompaction.Inherit)
{
    public static SubAgentExecutionPolicy Default { get; } =
        new(SubAgentSessionPolicy.ParentSession, SubAgentThreadPolicy.ForkFromParentThread);
}

public static class SubAgentExecutionPolicies
{
    public static SubAgentExecutionPolicy ParentSessionForkedThread(
        SubAgentThreadCompaction threadCompaction = SubAgentThreadCompaction.Inherit) =>
        new(
            SubAgentSessionPolicy.ParentSession,
            SubAgentThreadPolicy.ForkFromParentThread,
            ThreadCompaction: threadCompaction);

    public static SubAgentExecutionPolicy ParentSessionFreshThread() =>
        new(SubAgentSessionPolicy.ParentSession, SubAgentThreadPolicy.FreshThread);

    public static SubAgentExecutionPolicy ParentThread() =>
        new(SubAgentSessionPolicy.ParentSession, SubAgentThreadPolicy.ParentThread);

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

        if (policy.ThreadPolicy == SubAgentThreadPolicy.ParentThread &&
            policy.SessionPolicy != SubAgentSessionPolicy.ParentSession)
        {
            throw new ArgumentException("ParentThread requires SessionPolicy to be ParentSession.");
        }

        if (policy.ThreadCompaction != SubAgentThreadCompaction.Inherit &&
            policy.ThreadPolicy != SubAgentThreadPolicy.ForkFromParentThread)
        {
            throw new ArgumentException("ThreadCompaction can only be set when ThreadPolicy is ForkFromParentThread.");
        }
    }

}
