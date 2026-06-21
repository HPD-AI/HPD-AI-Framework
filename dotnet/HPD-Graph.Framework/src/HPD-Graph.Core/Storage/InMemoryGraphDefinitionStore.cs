using HPD.Graph.Abstractions.Storage;

namespace HPD.Graph.Core.Storage;

/// <summary>
/// In-memory graph definition store for development and tests.
/// </summary>
public sealed class InMemoryGraphDefinitionStore : IGraphDefinitionStore
{
    private readonly Dictionary<string, StoredGraph> _graphs = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public Task<StoredGraph?> LoadAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            _graphs.TryGetValue(graphId, out var graph);
            return Task.FromResult(graph);
        }
    }

    public Task SaveAsync(StoredGraph graph, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(graph.GraphId);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            _graphs[graph.GraphId] = graph;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            _graphs.Remove(graphId);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StoredGraphSummary>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            var summaries = _graphs.Values
                .OrderBy(graph => graph.GraphId, StringComparer.Ordinal)
                .Select(ToSummary)
                .ToList();

            return Task.FromResult<IReadOnlyList<StoredGraphSummary>>(summaries);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _graphs.Clear();
        }
    }

    private static StoredGraphSummary ToSummary(StoredGraph graph) => new()
    {
        GraphId = graph.GraphId,
        Name = graph.Name,
        GraphVersion = graph.GraphVersion,
        CreatedAt = graph.CreatedAt,
        UpdatedAt = graph.UpdatedAt,
        Description = graph.Description
    };
}
