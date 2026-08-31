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

    public static ThreadForkOptions Default { get; } = new();

    public static ThreadForkOptions FromMetadata(Dictionary<string, object>? metadata) =>
        metadata is null
            ? Default
            : new ThreadForkOptions { Metadata = metadata };
}
