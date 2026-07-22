using System.Collections.Immutable;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// An immutable, validated capability and visibility graph.
/// </summary>
internal sealed class CapabilityGraph
{
    private CapabilityGraph(
        ImmutableDictionary<CapabilityId, CapabilityNode> nodes,
        ImmutableDictionary<string, CapabilityId> modelNames)
    {
        Nodes = nodes;
        ModelNames = modelNames;
    }

    /// <summary>Gets nodes indexed by stable capability identifier.</summary>
    public ImmutableDictionary<CapabilityId, CapabilityNode> Nodes { get; }

    /// <summary>Gets stable identifiers indexed by model-facing function name.</summary>
    public ImmutableDictionary<string, CapabilityId> ModelNames { get; }

    /// <summary>
    /// Builds and validates an immutable graph from materialized capabilities.
    /// </summary>
    /// <param name="nodes">The complete capability-node collection.</param>
    /// <returns>The validated immutable graph.</returns>
    /// <exception cref="CapabilityGraphValidationException">The graph is structurally invalid.</exception>
    public static CapabilityGraph Create(IEnumerable<CapabilityNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var byId = ImmutableDictionary.CreateBuilder<CapabilityId, CapabilityNode>();
        var byName = ImmutableDictionary.CreateBuilder<string, CapabilityId>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            ArgumentNullException.ThrowIfNull(node);
            ValidateNodeShape(node);

            if (!byId.TryAdd(node.Id, node))
                throw new CapabilityGraphValidationException($"Duplicate capability ID '{node.Id}'.");

            if (!byName.TryAdd(node.Function.Name, node.Id))
                throw new CapabilityGraphValidationException(
                    $"Duplicate model-facing capability name '{node.Function.Name}'.");
        }

        foreach (var node in byId.Values)
        {
            foreach (var parentId in node.ParentContainerIds)
            {
                if (!byId.ContainsKey(parentId))
                    throw new CapabilityGraphValidationException(
                        $"Capability '{node.Id}' references missing parent '{parentId}'.");
            }

            foreach (var childId in node.Children)
            {
                if (!byId.ContainsKey(childId))
                    throw new CapabilityGraphValidationException(
                        $"Capability '{node.Id}' reveals missing child '{childId}'.");
            }
        }

        RejectCycles(byId.ToImmutable());
        return new CapabilityGraph(byId.ToImmutable(), byName.ToImmutable());
    }

    /// <summary>Builds a graph from the single typed metadata entry on native functions.</summary>
    public static CapabilityGraph CreateFromFunctions(IEnumerable<AIFunction> functions)
    {
        ArgumentNullException.ThrowIfNull(functions);
        return Create(functions.Select(function =>
        {
            if (function.AdditionalProperties?.TryGetValue(
                    HPDCapabilityMetadata.AdditionalPropertiesKey,
                    out var value) != true ||
                value is not HPDCapabilityMetadata metadata)
            {
                throw new CapabilityGraphValidationException(
                    $"Function '{function.Name}' does not contain typed HPD capability metadata.");
            }

            return new CapabilityNode
            {
                Id = metadata.Id,
                Function = function,
                Kind = metadata.Kind,
                ParentContainerIds = metadata.ParentContainerIds,
                Children = metadata.Reveals
            };
        }));
    }

    /// <summary>
    /// Returns whether a capability is visible for the supplied active containers.
    /// </summary>
    /// <param name="node">The capability to evaluate.</param>
    /// <param name="activeContainers">The active container identifiers.</param>
    /// <returns><see langword="true"/> when the capability should be exposed.</returns>
    public static bool IsVisible(
        CapabilityNode node,
        IReadOnlySet<CapabilityId> activeContainers)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(activeContainers);

        if (activeContainers.Contains(node.Id) && node.IsActivationContainer)
            return false;

        return node.ParentContainerIds.IsDefaultOrEmpty ||
               node.ParentContainerIds.Any(activeContainers.Contains);
    }

    private static void ValidateNodeShape(CapabilityNode node)
    {
        if (string.IsNullOrWhiteSpace(node.Id.Value))
            throw new CapabilityGraphValidationException("Capability IDs cannot be blank.");
        if (string.IsNullOrWhiteSpace(node.Function.Name))
            throw new CapabilityGraphValidationException($"Capability '{node.Id}' has a blank model name.");
        if (node.ParentContainerIds.Contains(node.Id))
            throw new CapabilityGraphValidationException($"Capability '{node.Id}' cannot parent itself.");
        if (node.Children.Contains(node.Id))
            throw new CapabilityGraphValidationException($"Capability '{node.Id}' cannot reveal itself.");
    }

    private static void RejectCycles(ImmutableDictionary<CapabilityId, CapabilityNode> nodes)
    {
        var visiting = new HashSet<CapabilityId>();
        var visited = new HashSet<CapabilityId>();

        foreach (var id in nodes.Keys)
            Visit(id, nodes, visiting, visited);
    }

    private static void Visit(
        CapabilityId id,
        ImmutableDictionary<CapabilityId, CapabilityNode> nodes,
        HashSet<CapabilityId> visiting,
        HashSet<CapabilityId> visited)
    {
        if (visited.Contains(id))
            return;
        if (!visiting.Add(id))
            throw new CapabilityGraphValidationException($"Capability graph contains a cycle at '{id}'.");

        foreach (var child in nodes[id].Children)
            Visit(child, nodes, visiting, visited);

        visiting.Remove(id);
        visited.Add(id);
    }
}

/// <summary>
/// A materialized native function and its immutable visibility relationships.
/// </summary>
internal sealed record CapabilityNode
{
    /// <summary>Gets the stable capability identifier.</summary>
    public required CapabilityId Id { get; init; }

    /// <summary>Gets the native model-callable function.</summary>
    public required AIFunction Function { get; init; }

    /// <summary>Gets the capability classification.</summary>
    public required HPDCapabilityKind Kind { get; init; }

    /// <summary>Gets alternative parent containers that can reveal this node.</summary>
    public ImmutableArray<CapabilityId> ParentContainerIds { get; init; } = [];

    /// <summary>Gets capabilities directly revealed by this node.</summary>
    public ImmutableArray<CapabilityId> Children { get; init; } = [];

    /// <summary>Gets whether invoking this node activates a visibility container.</summary>
    public bool IsActivationContainer =>
        Kind is HPDCapabilityKind.SkillActivation or HPDCapabilityKind.ToolHarnessActivation;
}

/// <summary>
/// Reports a deterministic capability-graph construction failure.
/// </summary>
internal sealed class CapabilityGraphValidationException : InvalidOperationException
{
    /// <summary>Creates a validation exception.</summary>
    /// <param name="message">The validation failure.</param>
    public CapabilityGraphValidationException(string message)
        : base(message)
    {
    }
}
