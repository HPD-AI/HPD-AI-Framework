using System.Collections.Concurrent;

namespace HPD.Graph.Abstractions.Execution;

/// <summary>Execution-owned facts shared by isolated scheduler contexts, not by workflow definitions.</summary>
public sealed class GraphExecutionLedger
{
    public ConcurrentDictionary<string, ErrorPropagationPolicy> TerminalPolicies { get; } = new();
    public ConcurrentDictionary<string, NodeAttemptRecord> Attempts { get; } = new();
    public ConcurrentDictionary<string, RecoveryExecution> Recoveries { get; } = new();
    public ConcurrentDictionary<string, SemaphoreSlim> RecoveryConcurrency { get; } = new();
}

public sealed record NodeAttemptRecord(string ExecutionId, string LogicalNodeId, string NodeId,
    int Attempt, bool IsRecovery, NodeExecutionResult Result);

public sealed class RecoveryExecution
{
    public required string ExecutionId { get; init; }
    public required string PrimaryNodeId { get; init; }
    public required string RecoveryNodeId { get; init; }
    public required NodeExecutionResult.Failure PrimaryFailure { get; init; }
    public string State { get; set; } = "started";
    public int Attempts { get; set; }
}

public sealed record NodeInvocation(string ExecutionId, string LogicalNodeId, string NodeId, int Attempt, bool IsRecovery);
