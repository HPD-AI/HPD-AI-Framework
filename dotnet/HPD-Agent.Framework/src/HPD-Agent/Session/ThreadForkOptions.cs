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

public sealed record ThreadForkOptions
{
    public Dictionary<string, object>? Metadata { get; init; }

    public ThreadForkCompaction Compaction { get; init; } = new InheritThreadForkCompaction();

    public static ThreadForkOptions Default { get; } = new();

    public static ThreadForkOptions FromMetadata(Dictionary<string, object>? metadata) =>
        metadata is null
            ? Default
            : new ThreadForkOptions { Metadata = metadata };
}
