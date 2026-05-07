using System.Collections.Concurrent;
using System.Text.Json;
using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Abstractions.Checkpointing;
using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.Invocation;
using HPDAgent.Graph.Abstractions.Registry;
using HPDAgent.Graph.Abstractions.Storage;
using HPDAgent.Graph.Core.Config;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using HPDAgent.Graph.Hosting.Data;
using HPD.Events;

namespace HPDAgent.Graph.Hosting.Lifecycle;

public interface IWorkflowExecutionRunner
{
    Task<WorkflowExecutionDto> StartAsync(
        string graphId,
        ExecuteWorkflowRequest request,
        CancellationToken ct = default);

    Task<WorkflowExecutionDto?> RunAsync(
        string graphId,
        string executionId,
        CancellationToken ct = default);

    Task<int> RunQueuedAsync(CancellationToken ct = default);

    Task<int> RequeueInterruptedAsync(CancellationToken ct = default);
}

public sealed class InProcessWorkflowExecutionRunner : IWorkflowExecutionRunner
{
    private static readonly TimeSpan DispatchDelay = TimeSpan.Zero;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
    private const int MaxDispatchAttempts = 3;

    private readonly IServiceProvider _serviceProvider;
    private readonly IGraphDefinitionStore _graphStore;
    private readonly IWorkflowExecutionStore _executionStore;
    private readonly IWorkflowLogStore? _logStore;
    private readonly IGraphCheckpointStore? _checkpointStore;
    private readonly IGraphHandlerRegistry? _handlerRegistry;
    private readonly IEventCoordinator? _eventCoordinator;
    private readonly IArtifactRegistry? _artifactRegistry;
    private readonly IGraphRegistry? _graphRegistry;
    private readonly GraphManager _graphManager;
    private readonly ExecutionManager _executionManager;
    private readonly TimeProvider _timeProvider;
    private readonly string _workerId;
    private readonly int _maxConcurrency;
    private readonly SemaphoreSlim _concurrency;
    private readonly GraphConfigCompiler _compiler = new();
    private readonly ConcurrentDictionary<string, byte> _running = new(StringComparer.Ordinal);

    public InProcessWorkflowExecutionRunner(
        IServiceProvider serviceProvider,
        IGraphDefinitionStore graphStore,
        IWorkflowExecutionStore executionStore,
        GraphManager graphManager,
        ExecutionManager executionManager,
        IWorkflowLogStore? logStore = null,
        IGraphCheckpointStore? checkpointStore = null,
        IGraphHandlerRegistry? handlerRegistry = null,
        IEventCoordinator? eventCoordinator = null,
        TimeProvider? timeProvider = null,
        int? maxConcurrency = null,
        string? workerId = null,
        IArtifactRegistry? artifactRegistry = null,
        IGraphRegistry? graphRegistry = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _graphStore = graphStore ?? throw new ArgumentNullException(nameof(graphStore));
        _executionStore = executionStore ?? throw new ArgumentNullException(nameof(executionStore));
        _graphManager = graphManager ?? throw new ArgumentNullException(nameof(graphManager));
        _executionManager = executionManager ?? throw new ArgumentNullException(nameof(executionManager));
        _logStore = logStore;
        _checkpointStore = checkpointStore;
        _handlerRegistry = handlerRegistry;
        _eventCoordinator = eventCoordinator;
        _artifactRegistry = artifactRegistry;
        _graphRegistry = graphRegistry;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _maxConcurrency = Math.Max(1, maxConcurrency ?? Environment.ProcessorCount);
        _concurrency = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
        _workerId = string.IsNullOrWhiteSpace(workerId)
            ? $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():n}"
            : workerId;
    }

    public async Task<WorkflowExecutionDto> StartAsync(
        string graphId,
        ExecuteWorkflowRequest request,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentNullException.ThrowIfNull(request);

        var execution = await _graphManager.CreateExecutionAsync(graphId, request, ct).ConfigureAwait(false);

        if (!request.StartImmediately)
        {
            return execution;
        }

        if (request.Mode == WorkflowExecutionMode.Foreground)
        {
            return await RunAsync(graphId, execution.ExecutionId, ct).ConfigureAwait(false) ?? execution;
        }

        _ = Task.Run(() => DispatchAsync(graphId, execution.ExecutionId, CancellationToken.None));
        return execution;
    }

    public async Task<WorkflowExecutionDto?> RunAsync(
        string graphId,
        string executionId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);

        var dispatchKey = $"{graphId}/{executionId}";
        if (!_running.TryAdd(dispatchKey, 0))
        {
            var alreadyRunning = await _executionStore.LoadAsync(graphId, executionId, ct).ConfigureAwait(false);
            return alreadyRunning is null ? null : WorkflowDtoMapper.ToExecutionDto(alreadyRunning);
        }

        await _concurrency.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var execution = await _executionStore.TryClaimAsync(
                graphId,
                executionId,
                _workerId,
                _timeProvider.GetUtcNow(),
                LeaseDuration,
                ct).ConfigureAwait(false);

            if (execution is null)
            {
                var unclaimed = await _executionStore.LoadAsync(graphId, executionId, ct).ConfigureAwait(false);
                return unclaimed is null ? null : WorkflowDtoMapper.ToExecutionDto(unclaimed);
            }

            if (execution.Status is WorkflowExecutionStatus.Completed or
                WorkflowExecutionStatus.Failed or
                WorkflowExecutionStatus.Cancelled)
            {
                return WorkflowDtoMapper.ToExecutionDto(execution);
            }

            using var timeoutCts = CreateTimeoutCancellationSource(execution, ct);
            var runToken = timeoutCts?.Token ?? ct;
            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(runToken);
            var heartbeatTask = HeartbeatAsync(graphId, executionId, heartbeatCts.Token);

            GraphContext? context = null;

            try
            {
                var graph = await _graphStore.LoadAsync(graphId, runToken).ConfigureAwait(false);
                if (graph is null)
                {
                    await StopHeartbeatAsync(heartbeatCts, heartbeatTask).ConfigureAwait(false);
                    var failedMissingGraph = await _executionManager.MarkFailedAsync(
                        graphId,
                        executionId,
                        nodeId: null,
                        $"Graph definition '{graphId}' was not found.",
                        CancellationToken.None).ConfigureAwait(false);

                    return WorkflowDtoMapper.ToExecutionDto(failedMissingGraph);
                }

                var runtimeGraph = _compiler.Compile(graph.Config);
                context = new GraphContext(execution.ExecutionId, runtimeGraph, _serviceProvider, enableSharedData: true)
                {
                    EventCoordinator = _eventCoordinator
                };
                SeedInput(context, execution.Input);

                var orchestrator = new GraphOrchestrator<GraphContext>(
                    _serviceProvider,
                    checkpointStore: _checkpointStore,
                    artifactRegistry: _artifactRegistry,
                    graphRegistry: _graphRegistry,
                    handlerRegistry: _handlerRegistry);

                await orchestrator.ExecuteAsync(context, runToken).ConfigureAwait(false);
                await StopHeartbeatAsync(heartbeatCts, heartbeatTask).ConfigureAwait(false);
                await FlushLogsAsync(graphId, executionId, context.LogEntries, CancellationToken.None).ConfigureAwait(false);

                var latest = await _executionStore.LoadAsync(graphId, executionId, CancellationToken.None)
                    .ConfigureAwait(false);
                if (latest?.Status is WorkflowExecutionStatus.Suspended or WorkflowExecutionStatus.Polling)
                {
                    return WorkflowDtoMapper.ToExecutionDto(latest);
                }

                var completed = await _executionManager.MarkCompletedAsync(
                    graphId,
                    executionId,
                    CancellationToken.None).ConfigureAwait(false);

                return WorkflowDtoMapper.ToExecutionDto(completed);
            }
            catch (GraphSuspendedException)
            {
                await StopHeartbeatAsync(heartbeatCts, heartbeatTask).ConfigureAwait(false);
                var suspended = await _executionStore.LoadAsync(graphId, executionId, CancellationToken.None)
                    .ConfigureAwait(false);
                return suspended is null ? null : WorkflowDtoMapper.ToExecutionDto(suspended);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await StopHeartbeatAsync(heartbeatCts, heartbeatTask).ConfigureAwait(false);
                throw;
            }
            catch (OperationCanceledException ex)
            {
                await StopHeartbeatAsync(heartbeatCts, heartbeatTask).ConfigureAwait(false);
                var cancelled = await _executionManager.MarkCancelledAsync(
                    graphId,
                    executionId,
                    ex.Message,
                    CancellationToken.None).ConfigureAwait(false);

                return WorkflowDtoMapper.ToExecutionDto(cancelled);
            }
            catch (Exception ex)
            {
                await StopHeartbeatAsync(heartbeatCts, heartbeatTask).ConfigureAwait(false);

                if (context is null)
                {
                    var dispatchFailed = await MarkDispatchFailureAsync(
                        execution,
                        ex.Message,
                        CancellationToken.None).ConfigureAwait(false);

                    return WorkflowDtoMapper.ToExecutionDto(dispatchFailed);
                }

                await FlushLogsAsync(graphId, executionId, context.LogEntries, CancellationToken.None).ConfigureAwait(false);

                var failed = await _executionManager.MarkFailedAsync(
                    graphId,
                    executionId,
                    context.CurrentNodeId,
                    ex.Message,
                    CancellationToken.None).ConfigureAwait(false);

                return WorkflowDtoMapper.ToExecutionDto(failed);
            }
        }
        finally
        {
            _running.TryRemove(dispatchKey, out _);
            _concurrency.Release();
        }
    }

    private async Task<WorkflowExecution> MarkDispatchFailureAsync(
        WorkflowExecution execution,
        string message,
        CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();
        if (execution.AttemptCount < MaxDispatchAttempts)
        {
            var retryDelay = TimeSpan.FromSeconds(Math.Pow(2, Math.Max(0, execution.AttemptCount - 1)));
            var retry = execution with
            {
                Status = WorkflowExecutionStatus.Created,
                CurrentNodeId = null,
                ClaimedBy = null,
                ClaimedAt = null,
                LeaseUntil = null,
                LastHeartbeatAt = null,
                ErrorMessage = message,
                NextAttemptAt = now + retryDelay
            };

            await _executionStore.SaveAsync(retry, ct).ConfigureAwait(false);
            await AppendLogAsync(
                execution.GraphId,
                execution.ExecutionId,
                $"Execution dispatch failed and will retry at {retry.NextAttemptAt:O}: {message}",
                LogLevel.Warning,
                ct).ConfigureAwait(false);
            return retry;
        }

        return await _executionManager.MarkFailedAsync(
            execution.GraphId,
            execution.ExecutionId,
            nodeId: null,
            message,
            ct).ConfigureAwait(false);
    }

    public async Task<int> RunQueuedAsync(CancellationToken ct = default)
    {
        var started = 0;
        var graphs = await _graphStore.ListAsync(ct).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();

        foreach (var graph in graphs)
        {
            ct.ThrowIfCancellationRequested();

            if (started >= Math.Max(0, _maxConcurrency - _running.Count))
            {
                break;
            }

            var executions = await _executionStore.ListAsync(graph.GraphId, ct).ConfigureAwait(false);
            if (executions.Any(execution =>
                    execution.Status is WorkflowExecutionStatus.Suspended or WorkflowExecutionStatus.Polling ||
                    (execution.Status == WorkflowExecutionStatus.Running &&
                     (execution.LeaseUntil is null || execution.LeaseUntil > now))))
            {
                continue;
            }

            var next = executions
                .Where(execution =>
                    execution.Status == WorkflowExecutionStatus.Created &&
                    (execution.NextAttemptAt is null || execution.NextAttemptAt <= now))
                .OrderBy(static execution => execution.CreatedAt)
                .FirstOrDefault();

            if (next is null)
            {
                continue;
            }

            _ = Task.Run(() => DispatchAsync(graph.GraphId, next.ExecutionId, CancellationToken.None));
            started++;
        }

        return started;
    }

    public async Task<int> RequeueInterruptedAsync(CancellationToken ct = default)
    {
        var requeued = 0;
        var graphs = await _graphStore.ListAsync(ct).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();

        foreach (var graph in graphs)
        {
            ct.ThrowIfCancellationRequested();

            var executions = await _executionStore.ListAsync(graph.GraphId, ct).ConfigureAwait(false);
            foreach (var execution in executions.Where(execution =>
                         execution.Status == WorkflowExecutionStatus.Running &&
                         (execution.LeaseUntil is null || execution.LeaseUntil <= now)))
            {
                var requeuedExecution = execution with
                {
                    Status = WorkflowExecutionStatus.Created,
                    CurrentNodeId = null,
                    StartedAt = null,
                    ClaimedBy = null,
                    ClaimedAt = null,
                    LeaseUntil = null,
                    LastHeartbeatAt = null,
                    NextAttemptAt = null
                };

                await _executionStore.SaveAsync(requeuedExecution, ct).ConfigureAwait(false);
                await AppendLogAsync(
                    execution.GraphId,
                    execution.ExecutionId,
                    "Execution was requeued after host startup recovery.",
                    LogLevel.Warning,
                    ct).ConfigureAwait(false);
                requeued++;
            }
        }

        return requeued;
    }

    private async Task DispatchAsync(string graphId, string executionId, CancellationToken ct)
    {
        if (DispatchDelay > TimeSpan.Zero)
        {
            await Task.Delay(DispatchDelay, ct).ConfigureAwait(false);
        }

        try
        {
            await RunAsync(graphId, executionId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task HeartbeatAsync(string graphId, string executionId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(HeartbeatInterval, ct).ConfigureAwait(false);
                await _executionStore.RenewLeaseAsync(
                    graphId,
                    executionId,
                    _workerId,
                    _timeProvider.GetUtcNow(),
                    LeaseDuration,
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static async Task StopHeartbeatAsync(
        CancellationTokenSource heartbeatCts,
        Task heartbeatTask)
    {
        await heartbeatCts.CancelAsync().ConfigureAwait(false);
        try
        {
            await heartbeatTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private CancellationTokenSource? CreateTimeoutCancellationSource(
        WorkflowExecution execution,
        CancellationToken ct)
    {
        var timeout = execution.DeadlineAt is null
            ? execution.Timeout
            : execution.DeadlineAt.Value - _timeProvider.GetUtcNow();

        if (timeout is null)
        {
            return null;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout.Value <= TimeSpan.Zero ? TimeSpan.Zero : timeout.Value);
        return cts;
    }

    private static void SeedInput(GraphContext context, JsonElement? input)
    {
        if (input is null)
        {
            return;
        }

        var value = ConvertJson(input.Value);
        context.Channels["input:workflow"].Set(value);
        context.SharedData?["input"] = value;

        if (input.Value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in input.Value.EnumerateObject())
        {
            var propertyValue = ConvertJson(property.Value);
            context.Channels[$"input:{property.Name}"].Set(propertyValue);
            context.SharedData?[property.Name] = propertyValue;
        }
    }

    private static object ConvertJson(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => ConvertJson(property.Value),
                    StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJson).ToList(),
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => element.Clone(),
            _ => element.Clone()
        };
    }

    private async Task FlushLogsAsync(
        string graphId,
        string executionId,
        IReadOnlyList<GraphLogEntry> logs,
        CancellationToken ct)
    {
        if (_logStore is null)
        {
            return;
        }

        foreach (var log in logs)
        {
            await _logStore.AppendAsync(new WorkflowLogEntry
            {
                GraphId = graphId,
                ExecutionId = executionId,
                Timestamp = log.Timestamp,
                Source = log.Source,
                Level = log.Level,
                Message = log.Message,
                NodeId = log.NodeId,
                Exception = log.Exception?.ToString()
            }, ct).ConfigureAwait(false);
        }
    }

    private Task AppendLogAsync(
        string graphId,
        string executionId,
        string message,
        LogLevel level,
        CancellationToken ct)
    {
        return _logStore?.AppendAsync(new WorkflowLogEntry
        {
            GraphId = graphId,
            ExecutionId = executionId,
            Timestamp = _timeProvider.GetUtcNow(),
            Source = nameof(InProcessWorkflowExecutionRunner),
            Level = level,
            Message = message
        }, ct) ?? Task.CompletedTask;
    }
}
