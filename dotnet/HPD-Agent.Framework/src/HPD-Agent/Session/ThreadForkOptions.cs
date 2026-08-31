using System.Text.Json.Serialization;

namespace HPD.Agent;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(InheritThreadForkCompaction), "inherit")]
[JsonDerivedType(typeof(DisableThreadForkCompaction), "disabled")]
[JsonDerivedType(typeof(ApplyThreadForkCompaction), "enabled")]
public abstract record ThreadForkCompaction;

public sealed record InheritThreadForkCompaction : ThreadForkCompaction;

public sealed record DisableThreadForkCompaction : ThreadForkCompaction;

public sealed record ApplyThreadForkCompaction(CompactionSpecification Compaction)
    : ThreadForkCompaction;

/// <summary>Controls how a parent fork projects its direct durable subagents.</summary>
public enum SubAgentForkPolicy
{
    /// <summary>Retains local identifiers as explanatory tombstones.</summary>
    Detach,
    /// <summary>Grants the target parent control of the original child routes.</summary>
    Share,
    /// <summary>Eagerly forks each direct child and remaps the same local identifiers.</summary>
    ForkDirectChildren
}

/// <summary>Defines direct-child and descendant behavior for one parent fork.</summary>
public sealed record SubAgentForkOptions
{
    /// <summary>Gets the direct-child policy. Isolation through detach is the default.</summary>
    public SubAgentForkPolicy Policy { get; init; } = SubAgentForkPolicy.Detach;

    /// <summary>Gets the bounded policy applied to children owned by copied children.</summary>
    public SubAgentForkPolicy DescendantPolicy { get; init; } = SubAgentForkPolicy.Detach;
}

public sealed record ThreadForkOptions
{
    public Dictionary<string, object>? Metadata { get; init; }

    public ThreadForkCompaction Compaction { get; init; } = new InheritThreadForkCompaction();

    /// <summary>Gets the explicit subagent topology override for this fork.</summary>
    public SubAgentForkOptions? SubAgents { get; init; }

    /// <summary>Gets the idempotent operation identifier, when supplied by a trusted caller.</summary>
    public string? OperationId { get; init; }

    public static ThreadForkOptions Default { get; } = new();

    public static ThreadForkOptions FromMetadata(Dictionary<string, object>? metadata) =>
        metadata is null
            ? Default
            : new ThreadForkOptions { Metadata = metadata };
}

/// <summary>Identifies the durable lifecycle of a multi-journal fork topology.</summary>
public enum ThreadForkOperationStatus
{
    Prepared,
    ChildrenPreparing,
    ParentPreparing,
    ReadyToCommit,
    Committed,
    Aborted,
    ReconciliationRequired
}

/// <summary>Reports one direct-child outcome produced by a parent fork.</summary>
public sealed record SubAgentForkChildOutcome(
    string LocalId,
    SubAgentForkPolicy Policy,
    ThreadKey? Source,
    ThreadKey? Target,
    SubAgentChildAvailability Availability);

/// <summary>Authoritative result returned by every public thread fork.</summary>
public sealed record ThreadForkResult
{
    /// <summary>Gets the durable topology operation identifier.</summary>
    public required string OperationId { get; init; }
    /// <summary>Gets the source thread.</summary>
    public required ThreadKey Source { get; init; }
    /// <summary>Gets the committed target thread.</summary>
    public required ThreadKey Target { get; init; }
    /// <summary>Gets the exact source boundary.</summary>
    public required ThreadJournalCursor SourceBoundary { get; init; }
    /// <summary>Gets the effective direct-child policy.</summary>
    public required SubAgentForkPolicy SubAgentPolicy { get; init; }
    /// <summary>Gets the final operation status.</summary>
    public required ThreadForkOperationStatus Status { get; init; }
    /// <summary>Gets deterministic direct-child outcomes.</summary>
    public required IReadOnlyList<SubAgentForkChildOutcome> Children { get; init; }
}
