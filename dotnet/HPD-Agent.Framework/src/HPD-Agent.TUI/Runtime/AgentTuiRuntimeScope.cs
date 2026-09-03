namespace HPD.Agent.TUI.Runtime;

public sealed record AgentTuiRuntimeScope
{
    public AgentTuiRuntimeScope(
        string agentId,
        string sessionId,
        string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        AgentId = agentId;
        SessionId = sessionId;
        ThreadId = threadId;
    }

    public string AgentId { get; init; }

    public string SessionId { get; init; }

    public string ThreadId { get; init; }
}

/// <summary>Identifies how a TUI may submit work for the transcript it is displaying.</summary>
public abstract record AgentTuiExecutionTarget
{
    /// <summary>Gets the scope used for hydration, observation, usage, responses, and cancellation.</summary>
    public abstract AgentTuiRuntimeScope Scope { get; }
}

/// <summary>Submits directly to the displayed agent scope.</summary>
/// <param name="Scope">The directly addressable agent scope.</param>
public sealed record DirectAgentTuiExecutionTarget : AgentTuiExecutionTarget
{
    /// <summary>Creates a direct target.</summary>
    public DirectAgentTuiExecutionTarget(AgentTuiRuntimeScope scope)
        => Scope = scope ?? throw new ArgumentNullException(nameof(scope));

    /// <inheritdoc />
    public override AgentTuiRuntimeScope Scope { get; }
}

/// <summary>Displays a child scope but submits through its durable controller registry entry.</summary>
/// <param name="ChildScope">The displayed child scope.</param>
/// <param name="ControllerScope">The controller scope that owns the registry entry.</param>
/// <param name="LocalId">The controller-local child identifier.</param>
/// <param name="ClientSelection">Redacted admitted model identity for presentation.</param>
public sealed record ControlledSubAgentTuiExecutionTarget(
    AgentTuiRuntimeScope ChildScope,
    AgentTuiRuntimeScope ControllerScope,
    SubAgentLocalId LocalId,
    AgentTuiClientSelectionSummary? ClientSelection = null) : AgentTuiExecutionTarget
{
    /// <inheritdoc />
    public override AgentTuiRuntimeScope Scope => ChildScope;
}

/// <summary>Redacted model identity safe for controlled-child presentation.</summary>
public sealed record AgentTuiClientSelectionSummary(string? ProviderKey, string? ModelName);
