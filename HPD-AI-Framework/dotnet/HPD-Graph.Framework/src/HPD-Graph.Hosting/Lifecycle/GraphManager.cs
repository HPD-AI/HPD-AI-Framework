using HPD.Graph.Abstractions.Config;
using HPD.Graph.Abstractions.Context;
using HPD.Graph.Abstractions.Registry;
using HPD.Graph.Abstractions.Storage;
using HPD.Graph.Core.Config;
using HPD.Graph.Hosting.Data;

namespace HPD.Graph.Hosting.Lifecycle;

public sealed class GraphManager
{
    private readonly IGraphDefinitionStore _graphStore;
    private readonly IWorkflowExecutionStore _executionStore;
    private readonly IWorkflowLogStore? _logStore;
    private readonly IGraphRegistry? _graphRegistry;
    private readonly TimeProvider _timeProvider;
    private readonly GraphConfigCompiler _compiler = new();

    public GraphManager(
        IGraphDefinitionStore graphStore,
        IWorkflowExecutionStore executionStore,
        IWorkflowLogStore? logStore = null,
        TimeProvider? timeProvider = null,
        IGraphRegistry? graphRegistry = null)
    {
        _graphStore = graphStore ?? throw new ArgumentNullException(nameof(graphStore));
        _executionStore = executionStore ?? throw new ArgumentNullException(nameof(executionStore));
        _logStore = logStore;
        _graphRegistry = graphRegistry;
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
        SyncGraphRegistry(config.GraphId, config);
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
        SyncGraphRegistry(graphId, updatedConfig);
        return updated;
    }

    public async Task DeleteDefinitionAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        await _graphStore.DeleteAsync(graphId, ct).ConfigureAwait(false);
        _graphRegistry?.UnregisterGraph(graphId);
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

    private void SyncGraphRegistry(string graphId, GraphConfig config)
    {
        if (_graphRegistry is null)
        {
            return;
        }

        if (_graphRegistry.ContainsGraph(graphId))
        {
            _graphRegistry.UnregisterGraph(graphId);
        }

        _graphRegistry.RegisterGraph(graphId, _compiler.Compile(config));
    }
}
