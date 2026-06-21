using System.Text.Json;
using HPD.Graph.Abstractions.Serialization;
using HPD.Graph.Abstractions.Storage;

namespace HPD.Graph.Core.Storage;

/// <summary>
/// File-backed workflow log store using newline-delimited JSON per execution.
/// </summary>
public sealed class JsonWorkflowLogStore : IWorkflowLogStore
{
    private static readonly GraphConfigJsonSerializerContext CompactJsonContext = new(new JsonSerializerOptions(
        GraphConfigJsonSerializerContext.Default.Options)
    {
        WriteIndented = false
    });

    private readonly string _logsDirectory;

    public JsonWorkflowLogStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _logsDirectory = Path.Combine(rootDirectory, "logs");
    }

    public async Task AppendAsync(WorkflowLogEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.GraphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.ExecutionId);
        ct.ThrowIfCancellationRequested();

        var graphDirectory = GetGraphDirectory(entry.GraphId);
        Directory.CreateDirectory(graphDirectory);

        var line = JsonSerializer.Serialize(
            entry,
            CompactJsonContext.WorkflowLogEntry);

        await File.AppendAllTextAsync(GetLogPath(entry.GraphId, entry.ExecutionId), line + Environment.NewLine, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WorkflowLogEntry>> ListAsync(
        string graphId,
        string executionId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ct.ThrowIfCancellationRequested();

        var path = GetLogPath(graphId, executionId);
        if (!File.Exists(path))
        {
            return Array.Empty<WorkflowLogEntry>();
        }

        var logs = new List<WorkflowLogEntry>();
        foreach (var line in await File.ReadAllLinesAsync(path, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var entry = JsonSerializer.Deserialize(
                line,
                CompactJsonContext.WorkflowLogEntry);

            if (entry is not null)
            {
                logs.Add(entry);
            }
        }

        return logs.OrderBy(log => log.Timestamp).ToList();
    }

    public async IAsyncEnumerable<WorkflowLogEntry> StreamAsync(
        string graphId,
        string executionId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var logs = await ListAsync(graphId, executionId, ct).ConfigureAwait(false);
        foreach (var log in logs)
        {
            ct.ThrowIfCancellationRequested();
            yield return log;
        }
    }

    private string GetGraphDirectory(string graphId) =>
        Path.Combine(_logsDirectory, EncodeFileName(graphId));

    private string GetLogPath(string graphId, string executionId) =>
        Path.Combine(GetGraphDirectory(graphId), $"{EncodeFileName(executionId)}.logs.ndjson");

    private static string EncodeFileName(string value) => Uri.EscapeDataString(value);
}
