using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>Identifies one source revision participating in an effective turn snapshot.</summary>
/// <param name="SourceId">The stable source identifier.</param>
/// <param name="Revision">The source-local revision.</param>
public sealed record AgentCapabilitySourceRevision(string SourceId, long Revision);

/// <summary>Describes the immutable capability identity pinned for one complete turn.</summary>
public sealed record AgentTurnCapabilityIdentity
{
    /// <summary>Gets the leased agent catalog epoch.</summary>
    public required long AgentEpoch { get; init; }
    /// <summary>Gets the deterministic revision of the per-turn overlay.</summary>
    public required string OverlayRevision { get; init; }
    /// <summary>Gets the deterministic identity of the complete effective capability surface.</summary>
    public required string EffectiveSnapshotId { get; init; }
    /// <summary>Gets source revisions pinned by the agent snapshot.</summary>
    public required IReadOnlyList<AgentCapabilitySourceRevision> SourceRevisions { get; init; }
}

/// <summary>Composes one leased agent snapshot and all per-turn tools exactly once.</summary>
internal sealed record AgentTurnCapabilityOverlay
{
    internal required ImmutableArray<AITool> Tools { get; init; }
    internal required AgentTurnCapabilityIdentity Identity { get; init; }

    internal static AgentTurnCapabilityOverlay Compose(
        AgentCapabilitySnapshot? agentSnapshot,
        IEnumerable<AITool>? preparedTools,
        IEnumerable<AITool>? runtimeTools,
        IEnumerable<AITool>? additionalTools)
    {
        var entries = new List<(AITool Tool, string Source, string StableId, string SortName)>();
        Add(preparedTools, "turn.configured");
        Add(runtimeTools, "turn.runtime");
        Add(additionalTools, "turn.additional");

        var collisions = entries
            .GroupBy(static entry => entry.SortName, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(group => $"'{group.Key}' from {string.Join(", ", group.Select(static item => item.Source).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))}")
            .ToArray();
        if (collisions.Length > 0)
            throw new InvalidOperationException(
                $"Turn capability collision: {string.Join("; ", collisions)}. Register an explicit override policy or use unique model-facing names.");

        var ordered = entries
            .OrderBy(static entry => entry.SortName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Source, StringComparer.Ordinal)
            .ThenBy(static entry => entry.StableId, StringComparer.Ordinal)
            .ToArray();
        var overlayMaterial = string.Join("\n", ordered.Select(static entry =>
            $"{entry.Source}|{entry.StableId}|{entry.SortName}"));
        var overlayRevision = Digest(overlayMaterial);
        var epoch = agentSnapshot?.Epoch ?? -1;
        var sources = agentSnapshot?.Revisions.Values
            .OrderBy(static owner => owner.SourceId.Value, StringComparer.Ordinal)
            .Select(static owner => new AgentCapabilitySourceRevision(owner.SourceId.Value, owner.Revision.Value))
            .ToArray() ?? [];
        var sourceMaterial = string.Join("\n", sources.Select(static source =>
            $"{source.SourceId}|{source.Revision}"));

        return new AgentTurnCapabilityOverlay
        {
            Tools = ordered.Select(static entry => entry.Tool).ToImmutableArray(),
            Identity = new AgentTurnCapabilityIdentity
            {
                AgentEpoch = epoch,
                OverlayRevision = overlayRevision,
                EffectiveSnapshotId = Digest($"{epoch}\n{sourceMaterial}\n{overlayRevision}"),
                SourceRevisions = sources
            }
        };

        void Add(IEnumerable<AITool>? tools, string fallbackSource)
        {
            if (tools is null) return;
            var ordinal = 0;
            foreach (var tool in tools)
            {
                ArgumentNullException.ThrowIfNull(tool);
                if (tool is AIFunction function)
                {
                    var metadata = function.AdditionalProperties?.TryGetValue(
                        HPDCapabilityMetadata.AdditionalPropertiesKey, out var raw) == true
                        ? raw as HPDCapabilityMetadata
                        : null;
                    var source = metadata is not null && agentSnapshot?.Descriptors.TryGetValue(metadata.Id, out var descriptor) == true
                        ? $"agent:{descriptor.SourceId.Value}@{descriptor.SourceRevision.Value}"
                        : fallbackSource;
                    entries.Add((tool, source, metadata?.Id.Value ?? function.Name, function.Name));
                }
                else
                {
                    var name = tool.GetType().FullName ?? tool.GetType().Name;
                    entries.Add((tool, fallbackSource, $"{name}#{ordinal}", $"{fallbackSource}:{name}#{ordinal}"));
                }
                ordinal++;
            }
        }
    }

    private static string Digest(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
}
