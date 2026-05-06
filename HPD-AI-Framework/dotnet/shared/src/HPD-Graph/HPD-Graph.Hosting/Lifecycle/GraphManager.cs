using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.Storage;
using HPDAgent.Graph.Hosting.Data;

namespace HPDAgent.Graph.Hosting.Lifecycle;

public sealed class GraphManager
{
    private readonly IGraphDefinitionStore _graphStore;
    private readonly IWorkflowExecutionStore _executionStore;
    private readonly IWorkflowLogStore? _logStore;
    private readonly TimeProvider _timeProvider;

    public GraphManager(
        IGraphDefinitionStore graphStore,
        IWorkflowExecutionStore executionStore,
        IWorkflowLogStore? logStore = null,
        TimeProvider? timeProvider = null)
    {
        _graphStore = graphStore ?? throw new ArgumentNullException(nameof(graphStore));
        _executionStore = executionStore ?? throw new ArgumentNullException(nameof(executionStore));
        _logStore = logStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<StoredGraph> CreateDefinitionAsync(GraphConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.GraphId);

        var now = _timeProvider.GetUtcNow();
        var stored = new StoredGraph
        {
            GraphId = config.GraphId,
            Name = config.Name,
            GraphVersion = config.GraphVersion,
            Config = config,
            CreatedAt = now,
            UpdatedAt = now,
            Description = config.Description,
            Metadata = config.Metadata
        };

        await _graphStore.SaveAsync(stored, ct).ConfigureAwait(false);
        return stored;
    }

    public Task<StoredGraph?> GetDefinitionAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        return _graphStore.LoadAsync(graphId, ct);
    }

    public Task<IReadOnlyList<StoredGraphSummary>> ListDefinitionsAsync(CancellationToken ct = default)
    {
        return _graphStore.ListAsync(ct);
    }

    public async Task<StoredGraph> UpdateDefinitionAsync(
        string graphId,
        GraphConfig config,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentNullException.ThrowIfNull(config);

        var existing = await _graphStore.LoadAsync(graphId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Graph definition '{graphId}' was not found.");

        var updatedConfig = config with { GraphId = graphId };
        var updated = existing with
        {
            Name = updatedConfig.Name,
            GraphVersion = updatedConfig.GraphVersion,
            Config = updatedConfig,
            UpdatedAt = _timeProvider.GetUtcNow(),
            Description = updatedConfig.Description,
            Metadata = updatedConfig.Metadata
        };

        await _graphStore.SaveAsync(updated, ct).ConfigureAwait(false);
        return updated;
    }

    public Task DeleteDefinitionAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        return _graphStore.DeleteAsync(graphId, ct);
    }

    public async Task<WorkflowExecutionDto> CreateExecutionAsync(
        string graphId,
        ExecuteWorkflowRequest request,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentNullException.ThrowIfNull(request);

        _ = await _graphStore.LoadAsync(graphId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Graph definition '{graphId}' was not found.");

        var now = _timeProvider.GetUtcNow();
        var execution = new WorkflowExecution
        {
            GraphId = graphId,
            ExecutionId = string.IsNullOrWhiteSpace(request.ExecutionId)
                ? Guid.NewGuid().ToString("n")
                : request.ExecutionId!,
            Status = request.StartImmediately
                ? WorkflowExecutionStatus.Running
                : WorkflowExecutionStatus.Created,
            CreatedAt = now,
            StartedAt = request.StartImmediately ? now : null,
            Input = request.Input,
            Timeout = request.Timeout,
            DeadlineAt = request.Timeout.HasValue
                ? now + request.Timeout.Value
                : null,
            TriggeredBy = request.TriggeredBy
        };

        await _executionStore.SaveAsync(execution, ct).ConfigureAwait(false);
        await AppendLogAsync(
            execution.GraphId,
            execution.ExecutionId,
            request.StartImmediately ? "Execution started." : "Execution created.",
            ct).ConfigureAwait(false);

        return WorkflowDtoMapper.ToExecutionDto(execution);
    }

    private Task AppendLogAsync(string graphId, string executionId, string message, CancellationToken ct)
    {
        return _logStore?.AppendAsync(new WorkflowLogEntry
        {
            GraphId = graphId,
            ExecutionId = executionId,
            Timestamp = _timeProvider.GetUtcNow(),
            Source = nameof(GraphManager),
            Level = LogLevel.Information,
            Message = message
        }, ct) ?? Task.CompletedTask;
    }
}
