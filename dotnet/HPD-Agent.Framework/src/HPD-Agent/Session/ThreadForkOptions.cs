using System.Text.Json.Serialization;

namespace HPD.Agent;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(InheritThreadForkCompaction), "inherit")]
[JsonDerivedType(typeof(DisableThreadForkCompaction), "disabled")]
[JsonDerivedType(typeof(ApplyThreadForkCompaction), "enabled")]
/// <summary>Defines compaction behavior applied while constructing a fork target.</summary>
public abstract record ThreadForkCompaction;

/// <summary>Uses the source agent's configured fork-compaction behavior.</summary>
public sealed record InheritThreadForkCompaction : ThreadForkCompaction;

/// <summary>Disables target compaction for this fork.</summary>
public sealed record DisableThreadForkCompaction : ThreadForkCompaction;

/// <summary>Applies an explicit compaction specification to the target.</summary>
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

/// <summary>Controls one idempotent thread-fork operation.</summary>
public sealed record ThreadForkOptions
{
    /// <summary>Gets optional metadata merged into the target thread.</summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>Gets target-history compaction behavior.</summary>
    public ThreadForkCompaction Compaction { get; init; } = new InheritThreadForkCompaction();

    /// <summary>Gets the explicit subagent topology override for this fork.</summary>
    public SubAgentForkOptions? SubAgents { get; init; }

    /// <summary>Gets the idempotent operation identifier, when supplied by a trusted caller.</summary>
    public string? OperationId { get; init; }

    /// <summary>Gets the immutable default fork options.</summary>
    public static ThreadForkOptions Default { get; } = new();

    /// <summary>Creates fork options containing only the supplied target metadata.</summary>
    /// <param name="metadata">Optional target metadata.</param>
    /// <returns>Default options when metadata is absent; otherwise a new options value.</returns>
    public static ThreadForkOptions FromMetadata(Dictionary<string, object>? metadata) =>
        metadata is null
            ? Default
            : new ThreadForkOptions { Metadata = metadata };
}

/// <summary>Identifies the durable lifecycle of a multi-journal fork topology.</summary>
public enum ThreadForkOperationStatus
{
    /// <summary>The immutable request and source boundary are durable.</summary>
    Prepared,
    /// <summary>Direct and descendant child targets are being staged.</summary>
    ChildrenPreparing,
    /// <summary>The parent target is ready to be staged.</summary>
    ParentPreparing,
    /// <summary>All topology members are prepared for the visibility commit.</summary>
    ReadyToCommit,
    /// <summary>The fork topology is visible and authoritative.</summary>
    Committed,
    /// <summary>The operation failed before visibility and will not be resumed automatically.</summary>
    Aborted,
    /// <summary>The topology committed but requires lineage or metadata reconciliation.</summary>
    ReconciliationRequired
}

/// <summary>Reports one direct-child outcome produced by a parent fork.</summary>
public sealed record SubAgentForkChildOutcome(
    string LocalId,
    SubAgentForkPolicy Policy,
    ThreadKey? Source,
    ThreadKey? Target,
    SubAgentChildAvailability Availability,
    string? TargetSeedFingerprint = null,
    ThreadJournalCursor? SourceBoundary = null);

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
