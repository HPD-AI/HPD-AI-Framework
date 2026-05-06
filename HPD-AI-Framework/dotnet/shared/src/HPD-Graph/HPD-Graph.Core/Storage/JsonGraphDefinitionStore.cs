using System.Text.Json;
using HPDAgent.Graph.Abstractions.Serialization;
using HPDAgent.Graph.Abstractions.Storage;

namespace HPDAgent.Graph.Core.Storage;

/// <summary>
/// File-backed graph definition store using one JSON file per graph.
/// </summary>
public sealed class JsonGraphDefinitionStore : IGraphDefinitionStore
{
    private readonly string _definitionsDirectory;

    public JsonGraphDefinitionStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _definitionsDirectory = Path.Combine(rootDirectory, "definitions");
    }

    public async Task<StoredGraph?> LoadAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ct.ThrowIfCancellationRequested();

        var path = GetGraphPath(graphId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(
            stream,
            GraphConfigJsonSerializerContext.Default.StoredGraph,
            ct);
    }

    public async Task SaveAsync(StoredGraph graph, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(graph.GraphId);
        ct.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_definitionsDirectory);
        var path = GetGraphPath(graph.GraphId);
        await WriteJsonAsync(path, graph, ct);
    }

    public Task DeleteAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ct.ThrowIfCancellationRequested();

        var path = GetGraphPath(graphId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<StoredGraphSummary>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!Directory.Exists(_definitionsDirectory))
        {
            return Array.Empty<StoredGraphSummary>();
        }

        var summaries = new List<StoredGraphSummary>();
        foreach (var path in Directory.EnumerateFiles(_definitionsDirectory, "*.graph.json").OrderBy(static p => p, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            await using var stream = File.OpenRead(path);
            var graph = await JsonSerializer.DeserializeAsync(
                stream,
                GraphConfigJsonSerializerContext.Default.StoredGraph,
                ct);

            if (graph is not null)
            {
                summaries.Add(ToSummary(graph));
            }
        }

        return summaries;
    }

    private string GetGraphPath(string graphId) =>
        Path.Combine(_definitionsDirectory, $"{EncodeFileName(graphId)}.graph.json");

    private static async Task WriteJsonAsync(string path, StoredGraph graph, CancellationToken ct)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                graph,
                GraphConfigJsonSerializerContext.Default.StoredGraph,
                ct);
        }

        File.Move(tempPath, path, overwrite: true);
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

    private static string EncodeFileName(string value) => Uri.EscapeDataString(value);
}
