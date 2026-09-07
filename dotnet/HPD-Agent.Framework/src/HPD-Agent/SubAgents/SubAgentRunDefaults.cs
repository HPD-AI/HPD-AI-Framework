using System.Text.Json;
using HPD.Agent.Serialization;

namespace HPD.Agent;

/// <summary>Explicit portable application defaults for every further descendant, independent of model propagation.</summary>
public sealed record SubAgentRunDefaults
{
    public bool AllowUserQuestions { get; init; }
    public AgentContextRunConfig? Context { get; init; }
    public CollapsingRunPolicy? Collapsing { get; init; }
    public CompactionRunPolicy? Compaction { get; init; }
    public CompactionSpecification? HandoffCompaction { get; init; }
    public AgentSecurityRunConfig Security { get; init; } = new();

    internal SubAgentRunDefaults Snapshot()
    {
        SubAgentCompactionConfiguration.Validate(HandoffCompaction);
        SubAgentCompactionConfiguration.Validate(Compaction?.Automatic?.Compaction);
        if (Context?.ToolInstances is { Count: > 0 })
            throw new InvalidOperationException("subagent_defaults_not_portable");
        return JsonSerializer.Deserialize(JsonSerializer.Serialize(this,
            AgentEventJsonContext.Default.SubAgentRunDefaults), AgentEventJsonContext.Default.SubAgentRunDefaults)!;
    }

    internal SubAgentRunConfig CreateRun()
    {
        var copy = Snapshot();
        return new SubAgentRunConfig
        {
            AllowUserQuestions = copy.AllowUserQuestions,
            Context = copy.Context, Collapsing = copy.Collapsing,
            Compaction = copy.Compaction, HandoffCompaction = copy.HandoffCompaction, Security = copy.Security,
            DescendantDefaults = copy
        };
    }
}
