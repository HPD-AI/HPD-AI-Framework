using System.Text.Json;
using HPDAgent.Graph.Abstractions.Serialization;
using HPDAgent.Graph.Abstractions.Storage;

namespace HPDAgent.Graph.Core.Storage;

/// <summary>
/// File-backed workflow execution status store using one JSON file per execution.
/// </summary>
public sealed class JsonWorkflowExecutionStore : IWorkflowExecutionStore
{
    private readonly string _executionsDirectory;

    public JsonWorkflowExecutionStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _executionsDirectory = Path.Combine(rootDirectory, "executions");
    }

    public async Task SaveAsync(WorkflowExecution execution, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentException.ThrowIfNullOrWhiteSpace(execution.GraphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(execution.ExecutionId);
        ct.ThrowIfCancellationRequested();

        var graphDirectory = GetGraphDirectory(execution.GraphId);
        Directory.CreateDirectory(graphDirectory);

        var path = GetExecutionPath(execution.GraphId, execution.ExecutionId);
        await WriteJsonAsync(path, execution, ct);
    }

    public async Task<WorkflowExecution?> LoadAsync(string graphId, string executionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ct.ThrowIfCancellationRequested();

        var path = GetExecutionPath(graphId, executionId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(
            stream,
            GraphConfigJsonSerializerContext.Default.WorkflowExecution,
            ct);
    }

    public async Task<IReadOnlyList<WorkflowExecution>> ListAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ct.ThrowIfCancellationRequested();

        var graphDirectory = GetGraphDirectory(graphId);
        if (!Directory.Exists(graphDirectory))
        {
            return Array.Empty<WorkflowExecution>();
        }

        var executions = new List<WorkflowExecution>();
        foreach (var path in Directory.EnumerateFiles(graphDirectory, "*.execution.json").OrderBy(static p => p, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            await using var stream = File.OpenRead(path);
            var execution = await JsonSerializer.DeserializeAsync(
                stream,
                GraphConfigJsonSerializerContext.Default.WorkflowExecution,
                ct);

            if (execution is not null)
            {
                executions.Add(execution);
            }
        }

        return executions
            .OrderBy(execution => execution.CreatedAt)
            .ThenBy(execution => execution.ExecutionId, StringComparer.Ordinal)
            .ToList();
    }

    private string GetGraphDirectory(string graphId) =>
        Path.Combine(_executionsDirectory, EncodeFileName(graphId));

    private string GetExecutionPath(string graphId, string executionId) =>
        Path.Combine(GetGraphDirectory(graphId), $"{EncodeFileName(executionId)}.execution.json");

    private static async Task WriteJsonAsync(string path, WorkflowExecution execution, CancellationToken ct)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                execution,
                GraphConfigJsonSerializerContext.Default.WorkflowExecution,
                ct);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private static string EncodeFileName(string value) => Uri.EscapeDataString(value);
}
