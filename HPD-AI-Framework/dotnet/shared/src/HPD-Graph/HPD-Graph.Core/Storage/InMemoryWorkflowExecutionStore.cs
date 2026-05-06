using HPDAgent.Graph.Abstractions.Storage;

namespace HPDAgent.Graph.Core.Storage;

/// <summary>
/// In-memory workflow execution store for development and tests.
/// </summary>
public sealed class InMemoryWorkflowExecutionStore : IWorkflowExecutionStore
{
    private readonly Dictionary<string, Dictionary<string, WorkflowExecution>> _executionsByGraphId =
        new(StringComparer.Ordinal);

    private readonly object _lock = new();

    public Task SaveAsync(WorkflowExecution execution, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentException.ThrowIfNullOrWhiteSpace(execution.GraphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(execution.ExecutionId);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (!_executionsByGraphId.TryGetValue(execution.GraphId, out var executions))
            {
                executions = new Dictionary<string, WorkflowExecution>(StringComparer.Ordinal);
                _executionsByGraphId[execution.GraphId] = executions;
            }

            executions[execution.ExecutionId] = execution;
        }

        return Task.CompletedTask;
    }

    public Task<WorkflowExecution?> LoadAsync(string graphId, string executionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (_executionsByGraphId.TryGetValue(graphId, out var executions) &&
                executions.TryGetValue(executionId, out var execution))
            {
                return Task.FromResult<WorkflowExecution?>(execution);
            }

            return Task.FromResult<WorkflowExecution?>(null);
        }
    }

    public Task<IReadOnlyList<WorkflowExecution>> ListAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (!_executionsByGraphId.TryGetValue(graphId, out var executions))
            {
                return Task.FromResult<IReadOnlyList<WorkflowExecution>>(Array.Empty<WorkflowExecution>());
            }

            var ordered = executions.Values
                .OrderBy(execution => execution.CreatedAt)
                .ThenBy(execution => execution.ExecutionId, StringComparer.Ordinal)
                .ToList();

            return Task.FromResult<IReadOnlyList<WorkflowExecution>>(ordered);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _executionsByGraphId.Clear();
        }
    }
}
