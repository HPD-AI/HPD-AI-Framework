using HPD.Serialization;
using HPDAgent.Graph.Abstractions.Serialization;
using HPDAgent.Graph.Abstractions.Storage;

namespace HPDAgent.Graph.Core.Storage;

/// <summary>
/// File-backed graph definition store using one JSON file per graph.
/// </summary>
public sealed class JsonGraphDefinitionStore : IGraphDefinitionStore
{
    private readonly string _definitionsDirectory;
    private readonly HpdConfigFormat _storageFormat;

    public JsonGraphDefinitionStore(string rootDirectory, HpdConfigFormat storageFormat = HpdConfigFormat.Json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _definitionsDirectory = Path.Combine(rootDirectory, "definitions");
        _storageFormat = storageFormat;
    }

    public async Task<StoredGraph?> LoadAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ct.ThrowIfCancellationRequested();

        var path = GetGraphPath(graphId, _storageFormat);
        if (!File.Exists(path) && _storageFormat != HpdConfigFormat.Json)
        {
            path = GetGraphPath(graphId, HpdConfigFormat.Json);
        }

        if (!File.Exists(path) && _storageFormat != HpdConfigFormat.Yaml)
        {
            path = GetGraphPath(graphId, HpdConfigFormat.Yaml);
        }

        if (!File.Exists(path))
        {
            path = GetGraphYmlPath(graphId);
        }

        if (!File.Exists(path))
        {
            return null;
        }

        return await GraphConfigSerializer.ReadStoredGraphFileAsync(path, ct);
    }

    public async Task SaveAsync(StoredGraph graph, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(graph.GraphId);
        ct.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_definitionsDirectory);
        var path = GetGraphPath(graph.GraphId, _storageFormat);
        await WriteConfigAsync(path, graph, ct);
    }

    public Task DeleteAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ct.ThrowIfCancellationRequested();

        foreach (var path in GetGraphPaths(graphId))
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
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
        foreach (var path in EnumerateGraphFiles().OrderBy(static p => p, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            var graph = await GraphConfigSerializer.ReadStoredGraphFileAsync(path, ct);

            if (graph is not null)
            {
                summaries.Add(ToSummary(graph));
            }
        }

        return summaries;
    }

    private string GetGraphPath(string graphId, HpdConfigFormat format) =>
        Path.Combine(_definitionsDirectory, $"{EncodeFileName(graphId)}.graph.{GetExtension(format)}");

    private IEnumerable<string> GetGraphPaths(string graphId)
    {
        yield return GetGraphPath(graphId, HpdConfigFormat.Json);
        yield return GetGraphPath(graphId, HpdConfigFormat.Yaml);
        yield return GetGraphYmlPath(graphId);
    }

    private string GetGraphYmlPath(string graphId) =>
        Path.Combine(_definitionsDirectory, $"{EncodeFileName(graphId)}.graph.yml");

    private IEnumerable<string> EnumerateGraphFiles()
    {
        foreach (var path in Directory.EnumerateFiles(_definitionsDirectory, "*.graph.json"))
            yield return path;

        foreach (var path in Directory.EnumerateFiles(_definitionsDirectory, "*.graph.yaml"))
            yield return path;

        foreach (var path in Directory.EnumerateFiles(_definitionsDirectory, "*.graph.yml"))
            yield return path;
    }

    private static async Task WriteConfigAsync(string path, StoredGraph graph, CancellationToken ct)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        var format = HpdConfigSerializer.InferFormat(path);
        var text = GraphConfigSerializer.SerializeStoredGraph(graph, format);
        await File.WriteAllTextAsync(tempPath, text, ct);

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

    private static string GetExtension(HpdConfigFormat format)
        => format switch
        {
            HpdConfigFormat.Json => "json",
            HpdConfigFormat.Yaml => "yaml",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };
}
