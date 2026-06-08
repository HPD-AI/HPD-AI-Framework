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
    /// Session and branch routing policy for sub-agent execution.
    /// </summary>
    public SubAgentExecutionPolicy ExecutionPolicy { get; init; } = SubAgentExecutionPolicy.Default;

    /// <summary>
    /// ToolHarness types to register with the sub-agent.
    /// </summary>
    public Type[] ToolHarnessTypes { get; init; } = Array.Empty<Type>();

    /// <summary>
    /// Optional branch metadata defaults applied to subagent-created branches.
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

public enum SubAgentBranchPolicy
{
    ForkFromParentBranch,
    FreshBranch,
    ExistingBranch,
    ParentBranch
}

public enum SubAgentBranchCompaction
{
    Inherit,
    Enabled,
    Disabled,
    PreferCache
}

public sealed record SubAgentExecutionPolicy(
    SubAgentSessionPolicy SessionPolicy,
    SubAgentBranchPolicy BranchPolicy,
    string? SharedSessionId = null,
    string? ExistingBranchId = null,
    string? BranchNamePrefix = null,
    SubAgentBranchCompaction BranchCompaction = SubAgentBranchCompaction.Inherit)
{
    public static SubAgentExecutionPolicy Default { get; } =
        new(SubAgentSessionPolicy.ParentSession, SubAgentBranchPolicy.ForkFromParentBranch);
}

public static class SubAgentExecutionPolicies
{
    public static SubAgentExecutionPolicy ParentSessionForkedBranch(
        SubAgentBranchCompaction branchCompaction = SubAgentBranchCompaction.Inherit) =>
        new(
            SubAgentSessionPolicy.ParentSession,
            SubAgentBranchPolicy.ForkFromParentBranch,
            BranchCompaction: branchCompaction);

    public static SubAgentExecutionPolicy ParentSessionFreshBranch() =>
        new(SubAgentSessionPolicy.ParentSession, SubAgentBranchPolicy.FreshBranch);

    public static SubAgentExecutionPolicy ParentBranch() =>
        new(SubAgentSessionPolicy.ParentSession, SubAgentBranchPolicy.ParentBranch);

    public static SubAgentExecutionPolicy NewSession() =>
        new(SubAgentSessionPolicy.NewSession, SubAgentBranchPolicy.FreshBranch);

    public static SubAgentExecutionPolicy SharedSessionFreshBranch(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return new(
            SubAgentSessionPolicy.SharedSession,
            SubAgentBranchPolicy.FreshBranch,
            SharedSessionId: sessionId);
    }

    public static SubAgentExecutionPolicy SharedSessionExistingBranch(string sessionId, string branchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        return new(
            SubAgentSessionPolicy.SharedSession,
            SubAgentBranchPolicy.ExistingBranch,
            SharedSessionId: sessionId,
            ExistingBranchId: branchId);
    }

    public static SubAgentExecutionPolicy ExistingBranch(string branchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        return new(SubAgentSessionPolicy.ParentSession, SubAgentBranchPolicy.ExistingBranch, ExistingBranchId: branchId);
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

        if (policy.BranchPolicy == SubAgentBranchPolicy.ExistingBranch)
        {
            if (string.IsNullOrWhiteSpace(policy.ExistingBranchId))
                throw new ArgumentException("ExistingBranchId is required when BranchPolicy is ExistingBranch.");
        }
        else if (!string.IsNullOrWhiteSpace(policy.ExistingBranchId))
        {
            throw new ArgumentException("ExistingBranchId is only valid when BranchPolicy is ExistingBranch.");
        }

        if (policy.BranchPolicy == SubAgentBranchPolicy.ForkFromParentBranch &&
            policy.SessionPolicy != SubAgentSessionPolicy.ParentSession)
        {
            throw new ArgumentException("ForkFromParentBranch requires SessionPolicy to be ParentSession.");
        }

        if (policy.BranchPolicy == SubAgentBranchPolicy.ParentBranch &&
            policy.SessionPolicy != SubAgentSessionPolicy.ParentSession)
        {
            throw new ArgumentException("ParentBranch requires SessionPolicy to be ParentSession.");
        }

        if (policy.BranchCompaction != SubAgentBranchCompaction.Inherit &&
            policy.BranchPolicy != SubAgentBranchPolicy.ForkFromParentBranch)
        {
            throw new ArgumentException("BranchCompaction can only be set when BranchPolicy is ForkFromParentBranch.");
        }
    }

}
