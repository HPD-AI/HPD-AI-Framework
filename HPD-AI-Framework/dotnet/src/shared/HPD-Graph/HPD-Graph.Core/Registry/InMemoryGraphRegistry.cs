using System.Collections.Concurrent;
using HPDAgent.Graph.Abstractions.Registry;

namespace HPDAgent.Graph.Core.Registry;

/// <summary>
/// Thread-safe in-memory implementation of IGraphRegistry.
/// Uses ConcurrentDictionary for thread-safe access without locks.
///
/// <code>
/// var registry = new InMemoryGraphRegistry();
/// registry.RegisterGraph("etl-pipeline", etlGraph);
/// registry.RegisterGraph("ml-pipeline", mlGraph);
///
/// var orchestrator = new GraphOrchestrator(..., graphRegistry: registry);
/// await orchestrator.MaterializeAsync("etl-pipeline", "users");
/// </code>
/// </summary>
public class InMemoryGraphRegistry : IGraphRegistry
{
    private readonly ConcurrentDictionary<string, Abstractions.Graph.Graph> _graphs = new();

    /// <inheritdoc/>
    public void RegisterGraph(string graphId, Abstractions.Graph.Graph graph)
    {
        if (string.IsNullOrWhiteSpace(graphId))
            throw new ArgumentException("Graph ID cannot be null or empty", nameof(graphId));

        if (graph == null)
            throw new ArgumentNullException(nameof(graph));

        if (!_graphs.TryAdd(graphId, graph))
            throw new ArgumentException($"Graph with ID '{graphId}' is already registered", nameof(graphId));
    }

    /// <inheritdoc/>
    public Abstractions.Graph.Graph? GetGraph(string graphId)
    {
        if (string.IsNullOrWhiteSpace(graphId))
            return null;

        return _graphs.TryGetValue(graphId, out var graph) ? graph : null;
    }

    /// <inheritdoc/>
    public bool ContainsGraph(string graphId)
    {
        if (string.IsNullOrWhiteSpace(graphId))
            return false;

        return _graphs.ContainsKey(graphId);
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetGraphIds()
    {
        return _graphs.Keys.ToList(); // Snapshot to avoid enumeration issues
    }

    /// <inheritdoc/>
    public bool UnregisterGraph(string graphId)
    {
        if (string.IsNullOrWhiteSpace(graphId))
            return false;

        return _graphs.TryRemove(graphId, out _);
    }

    /// <inheritdoc/>
    public int Count => _graphs.Count;

    /// <summary>
    /// Clear all registered graphs.
    /// Useful for testing or resetting the registry.
    /// </summary>
    public void Clear()
    {
        _graphs.Clear();
    }
}
