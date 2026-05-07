using System.Text.Json;
using HPDAgent.Graph.Connectors.Abstractions.Serialization;
using HPDAgent.Graph.Connectors.Abstractions.Sources;

namespace HPDAgent.Graph.Connectors.Core.Sources;

public sealed class JsonWorkflowSourceStore : IWorkflowSourceStore
{
    private readonly string _sourcesDirectory;
    private readonly string _statesDirectory;

    public JsonWorkflowSourceStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _sourcesDirectory = Path.Combine(rootDirectory, "connector-sources");
        _statesDirectory = Path.Combine(rootDirectory, "connector-source-states");
    }

    public async Task SaveAsync(WorkflowSource source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.SourceId);
        ct.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_sourcesDirectory);
        await WriteJsonAsync(
            GetSourcePath(source.SourceId),
            source,
            ConnectorAbstractionsJsonSerializerContext.Default.WorkflowSource,
            ct).ConfigureAwait(false);
    }

    public async Task<WorkflowSource?> LoadAsync(string sourceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ct.ThrowIfCancellationRequested();

        var path = GetSourcePath(sourceId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(
            stream,
            ConnectorAbstractionsJsonSerializerContext.Default.WorkflowSource,
            ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WorkflowSource>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!Directory.Exists(_sourcesDirectory))
        {
            return Array.Empty<WorkflowSource>();
        }

        var sources = new List<WorkflowSource>();
        foreach (var path in Directory.EnumerateFiles(_sourcesDirectory, "*.source.json").OrderBy(static path => path, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            await using var stream = File.OpenRead(path);
            var source = await JsonSerializer.DeserializeAsync(
                stream,
                ConnectorAbstractionsJsonSerializerContext.Default.WorkflowSource,
                ct).ConfigureAwait(false);

            if (source is not null)
            {
                sources.Add(source);
            }
        }

        return sources.OrderBy(static source => source.SourceId, StringComparer.Ordinal).ToList();
    }

    public async Task<IReadOnlyList<WorkflowSource>> ListByGraphAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);

        var sources = await ListAsync(ct).ConfigureAwait(false);
        return sources
            .Where(source => string.Equals(source.GraphId, graphId, StringComparison.Ordinal))
            .OrderBy(static source => source.SourceId, StringComparer.Ordinal)
            .ToList();
    }

    public Task DeleteAsync(string sourceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ct.ThrowIfCancellationRequested();

        DeleteIfExists(GetSourcePath(sourceId));
        DeleteIfExists(GetStatePath(sourceId));
        return Task.CompletedTask;
    }

    public async Task<WorkflowSourceState?> LoadStateAsync(string sourceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ct.ThrowIfCancellationRequested();

        var path = GetStatePath(sourceId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(
            stream,
            ConnectorAbstractionsJsonSerializerContext.Default.WorkflowSourceState,
            ct).ConfigureAwait(false);
    }

    public async Task SaveStateAsync(WorkflowSourceState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.SourceId);
        ct.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_statesDirectory);
        await WriteJsonAsync(
            GetStatePath(state.SourceId),
            state,
            ConnectorAbstractionsJsonSerializerContext.Default.WorkflowSourceState,
            ct).ConfigureAwait(false);
    }

    private string GetSourcePath(string sourceId) =>
        Path.Combine(_sourcesDirectory, $"{EncodeFileName(sourceId)}.source.json");

    private string GetStatePath(string sourceId) =>
        Path.Combine(_statesDirectory, $"{EncodeFileName(sourceId)}.state.json");

    private static async Task WriteJsonAsync<T>(
        string path,
        T value,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken ct)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, value, jsonTypeInfo, ct).ConfigureAwait(false);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string EncodeFileName(string value) => Uri.EscapeDataString(value);
}
