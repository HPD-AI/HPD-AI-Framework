using HPDAgent.Graph.Abstractions.Storage;

namespace HPDAgent.Graph.Core.Storage;

/// <summary>
/// In-memory workflow log store for development and tests.
/// </summary>
public sealed class InMemoryWorkflowLogStore : IWorkflowLogStore
{
    private readonly Dictionary<string, List<WorkflowLogEntry>> _logsByExecution = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public Task AppendAsync(WorkflowLogEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.GraphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.ExecutionId);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            var key = CreateKey(entry.GraphId, entry.ExecutionId);
            if (!_logsByExecution.TryGetValue(key, out var logs))
            {
                logs = [];
                _logsByExecution[key] = logs;
            }

            logs.Add(entry);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WorkflowLogEntry>> ListAsync(
        string graphId,
        string executionId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            var key = CreateKey(graphId, executionId);
            if (!_logsByExecution.TryGetValue(key, out var logs))
            {
                return Task.FromResult<IReadOnlyList<WorkflowLogEntry>>(Array.Empty<WorkflowLogEntry>());
            }

            return Task.FromResult<IReadOnlyList<WorkflowLogEntry>>(
                logs.OrderBy(log => log.Timestamp).ToList());
        }
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

    public void Clear()
    {
        lock (_lock)
        {
            _logsByExecution.Clear();
        }
    }

    private static string CreateKey(string graphId, string executionId) => $"{graphId}\n{executionId}";
}
