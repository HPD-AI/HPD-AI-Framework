using HPDAgent.Graph.Abstractions.Checkpointing;
using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Storage;
using HPDAgent.Graph.Hosting.Data;

namespace HPDAgent.Graph.Hosting.Lifecycle;

public sealed class ExecutionManager : IWorkflowExecutionStateSink
{
    private readonly IWorkflowExecutionStore _executionStore;
    private readonly IWorkflowLogStore? _logStore;
    private readonly IGraphCheckpointStore? _checkpointStore;
    private readonly IGraphDefinitionStore? _graphStore;
    private readonly IWorkflowResumeRunner _resumeRunner;
    private readonly TimeProvider _timeProvider;

    public ExecutionManager(
        IWorkflowExecutionStore executionStore,
        IWorkflowLogStore? logStore = null,
        IGraphCheckpointStore? checkpointStore = null,
        IGraphDefinitionStore? graphStore = null,
        IWorkflowResumeRunner? resumeRunner = null,
        TimeProvider? timeProvider = null)
    {
        _executionStore = executionStore ?? throw new ArgumentNullException(nameof(executionStore));
        _logStore = logStore;
        _checkpointStore = checkpointStore;
        _graphStore = graphStore;
        _resumeRunner = resumeRunner ?? new NoOpWorkflowResumeRunner();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<WorkflowStatusDto?> GetStatusAsync(
        string graphId,
        string executionId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);

        var execution = await _executionStore.LoadAsync(graphId, executionId, ct).ConfigureAwait(false);
        return execution is null ? null : WorkflowDtoMapper.ToStatusDto(execution);
    }

    public async IAsyncEnumerable<GraphLogEntryDto> StreamLogsAsync(
        string graphId,
        string executionId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);

        if (_logStore is null)
        {
            yield break;
        }

        await foreach (var entry in _logStore.StreamAsync(graphId, executionId, ct).ConfigureAwait(false))
        {
            yield return WorkflowDtoMapper.ToLogDto(entry);
        }
    }

    public async Task CancelAsync(string graphId, string executionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);

        var execution = await _executionStore.LoadAsync(graphId, executionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Execution '{executionId}' for graph '{graphId}' was not found.");

        if (execution.Status is WorkflowExecutionStatus.Completed or WorkflowExecutionStatus.Failed or WorkflowExecutionStatus.Cancelled)
        {
            return;
        }

        var cancelled = execution with
        {
            Status = WorkflowExecutionStatus.Cancelled,
            CompletedAt = _timeProvider.GetUtcNow()
        };

        await _executionStore.SaveAsync(cancelled, ct).ConfigureAwait(false);
        await AppendLogAsync(graphId, executionId, "Execution cancelled.", LogLevel.Warning, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SuspendedNodeDto>> GetSuspendedNodesAsync(
        string graphId,
        string executionId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);

        var execution = await _executionStore.LoadAsync(graphId, executionId, ct).ConfigureAwait(false);
        if (execution is not null && execution.Suspensions.Count > 0)
        {
            return execution.Suspensions
                .Select(suspension => ToSuspendedNodeDto(execution, suspension))
                .ToList();
        }

        if (execution is null ||
            execution.Status is not (WorkflowExecutionStatus.Suspended or WorkflowExecutionStatus.Polling) ||
            string.IsNullOrWhiteSpace(execution.SuspendedNodeId) ||
            string.IsNullOrWhiteSpace(execution.SuspendToken))
        {
            var checkpoint = await LoadSuspensionCheckpointAsync(graphId, executionId, ct).ConfigureAwait(false);
            return checkpoint is null
                ? Array.Empty<SuspendedNodeDto>()
                : new[] { ToSuspendedNodeDto(checkpoint) };
        }

        return new[]
        {
            new SuspendedNodeDto
            {
                GraphId = execution.GraphId,
                ExecutionId = execution.ExecutionId,
                NodeId = execution.SuspendedNodeId,
                SuspendToken = execution.SuspendToken,
                Reason = execution.SuspendReason,
                Message = execution.SuspensionMessage,
                SuspendedAt = execution.SuspendedAt,
                RetryAfter = execution.RetryAfter,
                MaxWaitTime = execution.MaxWaitTime,
                MaxRetries = execution.MaxRetries,
                Status = execution.Status
            }
        };
    }

    public async Task<PollingStatusDto?> GetPollingStatusAsync(
        string graphId,
        string suspendToken,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(suspendToken);

        var execution = (await _executionStore.ListAsync(graphId, ct).ConfigureAwait(false))
            .FirstOrDefault(candidate =>
                candidate.Status == WorkflowExecutionStatus.Polling &&
                (string.Equals(candidate.SuspendToken, suspendToken, StringComparison.Ordinal) ||
                 candidate.Suspensions.Any(suspension =>
                     string.Equals(suspension.SuspendToken, suspendToken, StringComparison.Ordinal) &&
                     suspension.Reason is SuspendReason.PollingCondition or SuspendReason.ResourceWait)));

        var indexedSuspension = execution?.Suspensions.FirstOrDefault(suspension =>
            string.Equals(suspension.SuspendToken, suspendToken, StringComparison.Ordinal));

        if (indexedSuspension is not null)
        {
            return ToPollingStatusDto(execution!, indexedSuspension);
        }

        if (execution is null || string.IsNullOrWhiteSpace(execution.SuspendedNodeId))
        {
            var checkpoint = await FindSuspensionCheckpointAsync(graphId, suspendToken, ct).ConfigureAwait(false);
            return checkpoint is null ? null : ToPollingStatusDto(checkpoint);
        }

        var now = _timeProvider.GetUtcNow();
        var startedAt = execution.PollingStartedAt ?? execution.SuspendedAt ?? execution.StartedAt ?? execution.CreatedAt;
        var retryAfter = execution.RetryAfter ?? TimeSpan.Zero;
        var maxWaitTime = execution.MaxWaitTime ?? TimeSpan.Zero;

        return new PollingStatusDto
        {
            GraphId = execution.GraphId,
            ExecutionId = execution.ExecutionId,
            SuspendToken = suspendToken,
            NodeId = execution.SuspendedNodeId,
            Status = execution.Status,
            AttemptNumber = execution.PollingAttemptNumber ?? 0,
            RetryAfter = retryAfter,
            MaxWaitTime = maxWaitTime,
            ElapsedTime = now - startedAt,
            NextRetryAt = execution.NextRetryAt
        };
    }

    public async Task<ResumeSuspensionResultDto> ResumeSuspendedNodeAsync(
        string graphId,
        string suspendToken,
        ResumeSuspensionRequest request,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(suspendToken);
        ArgumentNullException.ThrowIfNull(request);

        GraphCheckpoint? resumeCheckpoint = null;
        var execution = (await _executionStore.ListAsync(graphId, ct).ConfigureAwait(false))
            .FirstOrDefault(candidate =>
                string.Equals(candidate.SuspendToken, suspendToken, StringComparison.Ordinal) ||
                candidate.Suspensions.Any(suspension =>
                    string.Equals(suspension.SuspendToken, suspendToken, StringComparison.Ordinal)));
        var matchedSuspension = execution?.Suspensions.FirstOrDefault(suspension =>
            string.Equals(suspension.SuspendToken, suspendToken, StringComparison.Ordinal));

        if (execution is null)
        {
            var checkpoint = await FindSuspensionCheckpointAsync(graphId, suspendToken, ct).ConfigureAwait(false);
            if (checkpoint is null)
            {
                return new ResumeSuspensionResultDto
                {
                    GraphId = graphId,
                    ExecutionId = string.Empty,
                    SuspendToken = suspendToken,
                    Status = ResumeSuspensionStatus.NotFound,
                    Message = $"Suspension token '{suspendToken}' was not found for graph '{graphId}'."
                };
            }

            resumeCheckpoint = checkpoint;
            execution = await _executionStore.LoadAsync(graphId, checkpoint.ExecutionId, ct).ConfigureAwait(false)
                ?? new WorkflowExecution
                {
                    GraphId = graphId,
                    ExecutionId = checkpoint.ExecutionId,
                    Status = WorkflowExecutionStatus.Suspended,
                    CreatedAt = checkpoint.CreatedAt,
                    StartedAt = checkpoint.CreatedAt,
                    SuspendedNodeId = checkpoint.Metadata?.SuspendedNodeId,
                    SuspendToken = checkpoint.Metadata?.SuspendToken,
                    SuspendReason = GetSuspendReason(checkpoint),
                    SuspensionMessage = GetStringMetadata(checkpoint, "message"),
                    SuspendedAt = checkpoint.CreatedAt
                };

            if (string.IsNullOrWhiteSpace(execution.SuspendToken))
            {
                execution = execution with
                {
                    Status = GetSuspendReason(checkpoint) is SuspendReason.PollingCondition or SuspendReason.ResourceWait
                        ? WorkflowExecutionStatus.Polling
                        : WorkflowExecutionStatus.Suspended,
                    SuspendedNodeId = checkpoint.Metadata?.SuspendedNodeId,
                    SuspendToken = checkpoint.Metadata?.SuspendToken,
                    SuspendReason = GetSuspendReason(checkpoint),
                    SuspensionMessage = GetStringMetadata(checkpoint, "message"),
                    SuspendedAt = checkpoint.CreatedAt
                };
            }
        }
        else
        {
            resumeCheckpoint = await FindSuspensionCheckpointAsync(graphId, suspendToken, ct).ConfigureAwait(false);
            if (resumeCheckpoint is null)
            {
                resumeCheckpoint = await LoadSuspensionCheckpointAsync(graphId, execution.ExecutionId, ct).ConfigureAwait(false);
            }
        }

        if (execution.Status is not (WorkflowExecutionStatus.Suspended or WorkflowExecutionStatus.Polling))
        {
            return new ResumeSuspensionResultDto
            {
                GraphId = execution.GraphId,
                ExecutionId = execution.ExecutionId,
                NodeId = matchedSuspension?.NodeId ?? execution.SuspendedNodeId,
                SuspendToken = suspendToken,
                Status = ResumeSuspensionStatus.AlreadyCompleted,
                Message = $"Execution '{execution.ExecutionId}' is not currently suspended."
            };
        }

        var nodeId = matchedSuspension?.NodeId ?? execution.SuspendedNodeId;
        var remainingSuspensions = execution.Suspensions
            .Where(suspension => !string.Equals(suspension.SuspendToken, suspendToken, StringComparison.Ordinal))
            .ToList();
        var nextSuspension = remainingSuspensions.FirstOrDefault();
        var resumed = execution with
        {
            Status = ToExecutionStatus(nextSuspension) ?? WorkflowExecutionStatus.Running,
            CurrentNodeId = nodeId,
            SuspendedNodeId = nextSuspension?.NodeId,
            SuspendToken = nextSuspension?.SuspendToken,
            SuspendReason = nextSuspension?.Reason,
            SuspensionMessage = nextSuspension?.Message,
            SuspendedAt = nextSuspension?.SuspendedAt,
            RetryAfter = nextSuspension?.RetryAfter,
            MaxWaitTime = nextSuspension?.MaxWaitTime,
            MaxRetries = nextSuspension?.MaxRetries,
            PollingAttemptNumber = nextSuspension?.PollingAttemptNumber,
            PollingStartedAt = nextSuspension?.PollingStartedAt,
            NextRetryAt = nextSuspension?.NextRetryAt,
            Suspensions = remainingSuspensions
        };

        var graph = _graphStore is null
            ? null
            : await _graphStore.LoadAsync(graphId, ct).ConfigureAwait(false);

        var runnerResult = await _resumeRunner.ResumeAsync(new WorkflowResumeRunnerRequest
        {
            Execution = resumed,
            Graph = graph,
            Checkpoint = resumeCheckpoint,
            ResumeValue = request.ResumeValue
        }, ct).ConfigureAwait(false);

        var message = runnerResult.Message ?? "Suspension resume accepted.";
        var resultExecution = resumed;

        switch (runnerResult.Status)
        {
            case ResumeSuspensionStatus.Accepted:
                await _executionStore.SaveAsync(resumed, ct).ConfigureAwait(false);
                await AppendLogAsync(
                    graphId,
                    resumed.ExecutionId,
                    $"Suspension '{suspendToken}' resumed.",
                    LogLevel.Information,
                    ct).ConfigureAwait(false);
                break;

            case ResumeSuspensionStatus.Failed:
                resultExecution = resumed with
                {
                    Status = WorkflowExecutionStatus.Failed,
                    CompletedAt = _timeProvider.GetUtcNow(),
                    ErrorMessage = message
                };
                await _executionStore.SaveAsync(resultExecution, ct).ConfigureAwait(false);
                await AppendLogAsync(
                    graphId,
                    resumed.ExecutionId,
                    $"Suspension '{suspendToken}' resume failed: {message}",
                    LogLevel.Error,
                    ct).ConfigureAwait(false);
                break;

            case ResumeSuspensionStatus.Rejected:
                resultExecution = execution with { ErrorMessage = message };
                await _executionStore.SaveAsync(resultExecution, ct).ConfigureAwait(false);
                await AppendLogAsync(
                    graphId,
                    execution.ExecutionId,
                    $"Suspension '{suspendToken}' resume rejected: {message}",
                    LogLevel.Warning,
                    ct).ConfigureAwait(false);
                break;
        }

        return new ResumeSuspensionResultDto
        {
            GraphId = resultExecution.GraphId,
            ExecutionId = resultExecution.ExecutionId,
            NodeId = nodeId,
            SuspendToken = suspendToken,
            Status = runnerResult.Status,
            Message = message
        };
    }

    private async Task<GraphCheckpoint?> LoadSuspensionCheckpointAsync(
        string graphId,
        string executionId,
        CancellationToken ct)
    {
        if (_checkpointStore is null)
        {
            return null;
        }

        var checkpoint = await _checkpointStore.LoadLatestCheckpointAsync(executionId, ct).ConfigureAwait(false);
        return IsPendingSuspensionCheckpoint(checkpoint, graphId) ? checkpoint : null;
    }

    private async Task<GraphCheckpoint?> FindSuspensionCheckpointAsync(
        string graphId,
        string suspendToken,
        CancellationToken ct)
    {
        if (_checkpointStore is null)
        {
            return null;
        }

        var executions = await _executionStore.ListAsync(graphId, ct).ConfigureAwait(false);
        foreach (var execution in executions)
        {
            var checkpoint = await _checkpointStore.LoadLatestCheckpointAsync(execution.ExecutionId, ct).ConfigureAwait(false);
            if (IsPendingSuspensionCheckpoint(checkpoint, graphId) &&
                string.Equals(checkpoint!.Metadata?.SuspendToken, suspendToken, StringComparison.Ordinal))
            {
                return checkpoint;
            }
        }

        return null;
    }

    private static bool IsPendingSuspensionCheckpoint(GraphCheckpoint? checkpoint, string graphId)
    {
        return checkpoint is
        {
            Metadata.Trigger: CheckpointTrigger.Suspension,
            Metadata.SuspensionOutcome: null or SuspensionOutcome.Pending
        } &&
        string.Equals(checkpoint.GraphId, graphId, StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(checkpoint.Metadata.SuspendedNodeId) &&
        !string.IsNullOrWhiteSpace(checkpoint.Metadata.SuspendToken);
    }

    private static SuspendedNodeDto ToSuspendedNodeDto(GraphCheckpoint checkpoint)
    {
        var reason = GetSuspendReason(checkpoint);
        return new SuspendedNodeDto
        {
            GraphId = checkpoint.GraphId,
            ExecutionId = checkpoint.ExecutionId,
            NodeId = checkpoint.Metadata!.SuspendedNodeId!,
            SuspendToken = checkpoint.Metadata.SuspendToken!,
            Reason = reason,
            Message = GetStringMetadata(checkpoint, "message"),
            SuspendedAt = checkpoint.CreatedAt,
            RetryAfter = GetTimeSpanMetadata(checkpoint, "retryAfter"),
            MaxWaitTime = GetTimeSpanMetadata(checkpoint, "maxWaitTime"),
            MaxRetries = GetIntMetadata(checkpoint, "maxRetries"),
            Status = reason is SuspendReason.PollingCondition or SuspendReason.ResourceWait
                ? WorkflowExecutionStatus.Polling
                : WorkflowExecutionStatus.Suspended
        };
    }

    private static SuspendedNodeDto ToSuspendedNodeDto(WorkflowExecution execution, WorkflowSuspension suspension)
    {
        return new SuspendedNodeDto
        {
            GraphId = execution.GraphId,
            ExecutionId = execution.ExecutionId,
            NodeId = suspension.NodeId,
            SuspendToken = suspension.SuspendToken,
            Reason = suspension.Reason,
            Message = suspension.Message,
            SuspendedAt = suspension.SuspendedAt,
            RetryAfter = suspension.RetryAfter,
            MaxWaitTime = suspension.MaxWaitTime,
            MaxRetries = suspension.MaxRetries,
            Status = ToExecutionStatus(suspension) ?? WorkflowExecutionStatus.Suspended
        };
    }

    private PollingStatusDto ToPollingStatusDto(GraphCheckpoint checkpoint)
    {
        var retryAfter = GetTimeSpanMetadata(checkpoint, "retryAfter") ?? TimeSpan.Zero;
        var maxWaitTime = GetTimeSpanMetadata(checkpoint, "maxWaitTime") ?? TimeSpan.Zero;
        var startedAt = GetDateTimeOffsetMetadata(checkpoint, "pollingStartedAt") ?? checkpoint.CreatedAt;

        return new PollingStatusDto
        {
            GraphId = checkpoint.GraphId,
            ExecutionId = checkpoint.ExecutionId,
            NodeId = checkpoint.Metadata!.SuspendedNodeId!,
            SuspendToken = checkpoint.Metadata.SuspendToken!,
            Status = WorkflowExecutionStatus.Polling,
            AttemptNumber = GetIntMetadata(checkpoint, "pollingAttemptNumber") ?? 0,
            RetryAfter = retryAfter,
            MaxWaitTime = maxWaitTime,
            ElapsedTime = _timeProvider.GetUtcNow() - startedAt,
            NextRetryAt = GetDateTimeOffsetMetadata(checkpoint, "nextRetryAt")
        };
    }

    private PollingStatusDto ToPollingStatusDto(WorkflowExecution execution, WorkflowSuspension suspension)
    {
        var now = _timeProvider.GetUtcNow();
        var startedAt = suspension.PollingStartedAt ?? suspension.SuspendedAt;

        return new PollingStatusDto
        {
            GraphId = execution.GraphId,
            ExecutionId = execution.ExecutionId,
            NodeId = suspension.NodeId,
            SuspendToken = suspension.SuspendToken,
            Status = WorkflowExecutionStatus.Polling,
            AttemptNumber = suspension.PollingAttemptNumber ?? 0,
            RetryAfter = suspension.RetryAfter ?? TimeSpan.Zero,
            MaxWaitTime = suspension.MaxWaitTime ?? TimeSpan.Zero,
            ElapsedTime = now - startedAt,
            NextRetryAt = suspension.NextRetryAt
        };
    }

    private static WorkflowExecutionStatus? ToExecutionStatus(WorkflowSuspension? suspension)
    {
        return suspension?.Reason is null
            ? null
            : suspension.Reason is SuspendReason.PollingCondition or SuspendReason.ResourceWait
                ? WorkflowExecutionStatus.Polling
                : WorkflowExecutionStatus.Suspended;
    }

    private static SuspendReason? GetSuspendReason(GraphCheckpoint checkpoint)
    {
        var value = GetStringMetadata(checkpoint, "reason") ?? GetStringMetadata(checkpoint, "suspendReason");
        return Enum.TryParse<SuspendReason>(value, ignoreCase: true, out var reason)
            ? reason
            : null;
    }

    private static string? GetStringMetadata(GraphCheckpoint checkpoint, string key)
    {
        return checkpoint.Metadata?.CustomMetadata?.TryGetValue(key, out var value) == true
            ? value?.ToString()
            : null;
    }

    private static int? GetIntMetadata(GraphCheckpoint checkpoint, string key)
    {
        if (checkpoint.Metadata?.CustomMetadata?.TryGetValue(key, out var value) != true || value is null)
        {
            return null;
        }

        return value switch
        {
            int typed => typed,
            long typed => checked((int)typed),
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => null
        };
    }

    private static TimeSpan? GetTimeSpanMetadata(GraphCheckpoint checkpoint, string key)
    {
        if (checkpoint.Metadata?.CustomMetadata?.TryGetValue(key, out var value) != true || value is null)
        {
            return null;
        }

        return value switch
        {
            TimeSpan typed => typed,
            string text when TimeSpan.TryParse(text, out var parsed) => parsed,
            _ => null
        };
    }

    private static DateTimeOffset? GetDateTimeOffsetMetadata(GraphCheckpoint checkpoint, string key)
    {
        if (checkpoint.Metadata?.CustomMetadata?.TryGetValue(key, out var value) != true || value is null)
        {
            return null;
        }

        return value switch
        {
            DateTimeOffset typed => typed,
            DateTime typed => typed,
            string text when DateTimeOffset.TryParse(text, out var parsed) => parsed,
            _ => null
        };
    }

    public async Task<WorkflowExecution> MarkSuspendedAsync(
        string graphId,
        string executionId,
        string nodeId,
        string suspendToken,
        SuspendReason reason,
        string? message = null,
        TimeSpan? retryAfter = null,
        TimeSpan? maxWaitTime = null,
        int? maxRetries = null,
        int? pollingAttemptNumber = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(suspendToken);

        var execution = await _executionStore.LoadAsync(graphId, executionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Execution '{executionId}' for graph '{graphId}' was not found.");

        var now = _timeProvider.GetUtcNow();
        var status = reason is SuspendReason.PollingCondition or SuspendReason.ResourceWait
            ? WorkflowExecutionStatus.Polling
            : WorkflowExecutionStatus.Suspended;
        var existingSuspension = execution.Suspensions.FirstOrDefault(suspension =>
            string.Equals(suspension.SuspendToken, suspendToken, StringComparison.Ordinal));
        var isSamePollingSuspension =
            status == WorkflowExecutionStatus.Polling &&
            ((execution.Status == WorkflowExecutionStatus.Polling &&
              string.Equals(execution.SuspendedNodeId, nodeId, StringComparison.Ordinal) &&
              string.Equals(execution.SuspendToken, suspendToken, StringComparison.Ordinal)) ||
             (existingSuspension is not null &&
              existingSuspension.Reason is SuspendReason.PollingCondition or SuspendReason.ResourceWait));
        var effectiveRetryAfter = retryAfter ?? (isSamePollingSuspension ? existingSuspension?.RetryAfter ?? execution.RetryAfter : null);
        var effectiveMaxWaitTime = maxWaitTime ?? (isSamePollingSuspension ? existingSuspension?.MaxWaitTime ?? execution.MaxWaitTime : null);
        var effectiveMaxRetries = maxRetries ?? (isSamePollingSuspension ? existingSuspension?.MaxRetries ?? execution.MaxRetries : null);
        var effectivePollingAttempt = pollingAttemptNumber ?? (isSamePollingSuspension ? existingSuspension?.PollingAttemptNumber ?? execution.PollingAttemptNumber : null);
        var effectiveSuspendedAt = isSamePollingSuspension
            ? existingSuspension?.SuspendedAt ?? execution.SuspendedAt ?? now
            : now;
        DateTimeOffset? effectivePollingStartedAt = status == WorkflowExecutionStatus.Polling
            ? (isSamePollingSuspension
                ? existingSuspension?.PollingStartedAt ?? execution.PollingStartedAt ?? existingSuspension?.SuspendedAt ?? execution.SuspendedAt ?? now
                : now)
            : null;
        var suspension = new WorkflowSuspension
        {
            NodeId = nodeId,
            SuspendToken = suspendToken,
            Reason = reason,
            Message = message ?? (isSamePollingSuspension ? existingSuspension?.Message ?? execution.SuspensionMessage : null),
            SuspendedAt = effectiveSuspendedAt,
            RetryAfter = effectiveRetryAfter,
            MaxWaitTime = effectiveMaxWaitTime,
            MaxRetries = effectiveMaxRetries,
            PollingAttemptNumber = effectivePollingAttempt,
            PollingStartedAt = effectivePollingStartedAt,
            NextRetryAt = effectiveRetryAfter.HasValue ? now + effectiveRetryAfter.Value : null
        };
        var suspensions = execution.Suspensions
            .Where(existing => !string.Equals(existing.SuspendToken, suspendToken, StringComparison.Ordinal))
            .Append(suspension)
            .ToList();

        var suspended = execution with
        {
            Status = status,
            CurrentNodeId = nodeId,
            SuspendedNodeId = nodeId,
            SuspendToken = suspendToken,
            SuspendReason = reason,
            SuspensionMessage = suspension.Message,
            SuspendedAt = effectiveSuspendedAt,
            RetryAfter = effectiveRetryAfter,
            MaxWaitTime = effectiveMaxWaitTime,
            MaxRetries = effectiveMaxRetries,
            PollingAttemptNumber = effectivePollingAttempt,
            PollingStartedAt = effectivePollingStartedAt,
            NextRetryAt = suspension.NextRetryAt,
            Suspensions = suspensions
        };

        await _executionStore.SaveAsync(suspended, ct).ConfigureAwait(false);
        await AppendLogAsync(
            graphId,
            executionId,
            $"Execution suspended at node '{nodeId}' with token '{suspendToken}'.",
            LogLevel.Information,
            ct).ConfigureAwait(false);

        return suspended;
    }

    async Task IWorkflowSuspensionSink.MarkSuspendedAsync(
        string graphId,
        string executionId,
        string nodeId,
        string suspendToken,
        SuspendReason reason,
        string? message,
        TimeSpan? retryAfter,
        TimeSpan? maxWaitTime,
        int? maxRetries,
        int? pollingAttemptNumber,
        CancellationToken ct)
    {
        await MarkSuspendedAsync(
            graphId,
            executionId,
            nodeId,
            suspendToken,
            reason,
            message,
            retryAfter,
            maxWaitTime,
            maxRetries,
            pollingAttemptNumber,
            ct).ConfigureAwait(false);
    }

    public async Task<WorkflowExecution> MarkRunningAsync(
        string graphId,
        string executionId,
        string? currentNodeId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);

        var execution = await _executionStore.LoadAsync(graphId, executionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Execution '{executionId}' for graph '{graphId}' was not found.");

        var running = execution with
        {
            Status = WorkflowExecutionStatus.Running,
            CurrentNodeId = currentNodeId ?? execution.CurrentNodeId,
            SuspendedNodeId = null,
            SuspendToken = null,
            SuspendReason = null,
            SuspensionMessage = null,
            SuspendedAt = null,
            RetryAfter = null,
            MaxWaitTime = null,
            MaxRetries = null,
            PollingAttemptNumber = null,
            PollingStartedAt = null,
            NextRetryAt = null,
            Suspensions = Array.Empty<WorkflowSuspension>()
        };

        await _executionStore.SaveAsync(running, ct).ConfigureAwait(false);
        return running;
    }

    async Task IWorkflowExecutionStateSink.MarkRunningAsync(
        string graphId,
        string executionId,
        string? currentNodeId,
        CancellationToken ct)
    {
        await MarkRunningAsync(graphId, executionId, currentNodeId, ct).ConfigureAwait(false);
    }

    public async Task<WorkflowExecution> MarkFailedAsync(
        string graphId,
        string executionId,
        string? nodeId,
        string errorMessage,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        var execution = await _executionStore.LoadAsync(graphId, executionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Execution '{executionId}' for graph '{graphId}' was not found.");

        var failed = execution with
        {
            Status = WorkflowExecutionStatus.Failed,
            CurrentNodeId = nodeId ?? execution.CurrentNodeId,
            SuspendedNodeId = null,
            SuspendToken = null,
            SuspendReason = null,
            SuspensionMessage = null,
            SuspendedAt = null,
            RetryAfter = null,
            MaxWaitTime = null,
            MaxRetries = null,
            PollingAttemptNumber = null,
            PollingStartedAt = null,
            NextRetryAt = null,
            Suspensions = Array.Empty<WorkflowSuspension>(),
            CompletedAt = _timeProvider.GetUtcNow(),
            ErrorMessage = errorMessage
        };

        await _executionStore.SaveAsync(failed, ct).ConfigureAwait(false);
        await AppendLogAsync(
            graphId,
            executionId,
            $"Execution failed{(string.IsNullOrWhiteSpace(nodeId) ? "" : $" at node '{nodeId}'")}: {errorMessage}",
            LogLevel.Error,
            ct).ConfigureAwait(false);

        return failed;
    }

    async Task IWorkflowExecutionStateSink.MarkFailedAsync(
        string graphId,
        string executionId,
        string? nodeId,
        string errorMessage,
        CancellationToken ct)
    {
        await MarkFailedAsync(graphId, executionId, nodeId, errorMessage, ct).ConfigureAwait(false);
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
            Source = nameof(ExecutionManager),
            Level = level,
            Message = message
        }, ct) ?? Task.CompletedTask;
    }
}
