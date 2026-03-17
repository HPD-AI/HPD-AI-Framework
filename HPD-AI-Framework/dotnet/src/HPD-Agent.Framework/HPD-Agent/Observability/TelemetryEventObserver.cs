using System.Diagnostics.Metrics;

namespace HPD.Agent;

/// <summary>
/// Observes agent events and emits OpenTelemetry metrics.
/// Metrics provide aggregated statistics for dashboards and alerting.
/// Note: Distributed tracing is handled separately via ActivitySource in Agent.
/// </summary>
public class TelemetryEventObserver : IAgentEventObserver, IDisposable
{
    private readonly Meter _meter;

    // Counters
    private readonly Counter<int> _iterations;
    private readonly Counter<int> _decisions;
    private readonly Counter<int> _circuitBreakerTriggers;
    private readonly Counter<int> _permissionChecks;
    private readonly Counter<int> _containerExpansions;
    private readonly Counter<int> _retryAttempts;
    private readonly Counter<int> _reductionCacheHits;
    private readonly Counter<int> _reductionCacheMisses;
    private readonly Counter<int> _documentProcessing;
    private readonly Counter<int> _nestedAgentCalls;
    private readonly Counter<int> _completions;
    private readonly Counter<int> _stateSnapshots;
    private readonly Counter<int> _parallelToolExecutions;
    private readonly Counter<int> _permissionDenials;
    private readonly Counter<int> _checkpointErrors;
    private readonly Counter<int> _retryExhaustions;

    // Histograms
    private readonly Histogram<double> _iterationDuration;
    private readonly Histogram<int> _parallelBatchSize;
    private readonly Histogram<double> _semaphoreWaitDuration;
    private readonly Histogram<double> _checkpointDuration;
    private readonly Histogram<double> _checkpointRestoreDuration;
    private readonly Histogram<double> _documentProcessingDuration;
    private readonly Histogram<double> _completionDuration;
    private readonly Histogram<double> _messageTurnDuration;
    private readonly Histogram<int> _stateMessageCountHistogram;
    private readonly Histogram<int> _turnHistoryCountHistogram;
    private readonly Histogram<int> _deltaMessageCountHistogram;
    private readonly Histogram<double> _permissionCheckDuration;
    private readonly Histogram<int> _containerMemberCountHistogram;
    private readonly Histogram<double> _retryDelayHistogram;
    private readonly Histogram<int> _reductionTokenSavingsHistogram;
    private readonly Histogram<int> _nestingDepthHistogram;
    private readonly Histogram<int> _checkpointSizeHistogram;

    public TelemetryEventObserver(string sourceName = "HPD.Agent")
    {
        _meter = new Meter(sourceName);

        // Initialize counters
        _iterations = _meter.CreateCounter<int>(
            "agent.iterations",
            description: "Number of agent iterations executed");

        _decisions = _meter.CreateCounter<int>(
            "agent.decisions",
            description: "Number of agent decisions made");

        _circuitBreakerTriggers = _meter.CreateCounter<int>(
            "agent.circuit_breaker_triggers",
            description: "Number of times circuit breaker was triggered");

        _permissionChecks = _meter.CreateCounter<int>(
            "agent.permission_checks",
            description: "Number of permission checks performed");

        _containerExpansions = _meter.CreateCounter<int>(
            "agent.container_expansions",
            description: "Number of Toolkit/skill container expansions");

        _retryAttempts = _meter.CreateCounter<int>(
            "agent.retry_attempts",
            description: "Number of function retry attempts");

        _reductionCacheHits = _meter.CreateCounter<int>(
            "agent.reduction_cache.hits",
            description: "Number of history reduction cache hits");

        _reductionCacheMisses = _meter.CreateCounter<int>(
            "agent.reduction_cache.misses",
            description: "Number of history reduction cache misses");

        _documentProcessing = _meter.CreateCounter<int>(
            "agent.document_processing",
            description: "Number of documents processed");

        _nestedAgentCalls = _meter.CreateCounter<int>(
            "agent.nested_agent_calls",
            description: "Number of nested agent invocations");

        // Initialize histograms
        _iterationDuration = _meter.CreateHistogram<double>(
            "agent.iteration.duration",
            unit: "ms",
            description: "Duration of agent iterations");

        _checkpointDuration = _meter.CreateHistogram<double>(
            "agent.checkpoint.duration",
            unit: "ms",
            description: "Duration of checkpoint save operations");

        _checkpointRestoreDuration = _meter.CreateHistogram<double>(
            "agent.checkpoint_restore.duration",
            unit: "ms",
            description: "Duration of checkpoint restore operations");

        _documentProcessingDuration = _meter.CreateHistogram<double>(
            "agent.document_processing.duration",
            unit: "ms",
            description: "Duration of document processing operations");

        _completions = _meter.CreateCounter<int>(
            "agent.completions",
            description: "Number of successful agent completions");

        _stateSnapshots = _meter.CreateCounter<int>(
            "agent.state_snapshots",
            description: "Number of state snapshots captured");

        _parallelToolExecutions = _meter.CreateCounter<int>(
            "agent.parallel_tool_executions",
            description: "Number of parallel tool execution batches");

        _permissionDenials = _meter.CreateCounter<int>(
            "agent.permission_denials",
            description: "Number of permission denials");

        _checkpointErrors = _meter.CreateCounter<int>(
            "agent.checkpoint_errors",
            description: "Number of checkpoint save failures");

        _retryExhaustions = _meter.CreateCounter<int>(
            "agent.retry_exhaustions",
            description: "Number of retry exhaustion events");

        _completionDuration = _meter.CreateHistogram<double>(
            "agent.completion.duration",
            unit: "ms",
            description: "Duration from start to completion");

        _messageTurnDuration = _meter.CreateHistogram<double>(
            "agent.message_turn.duration",
            unit: "ms",
            description: "Duration of message turns");

        _parallelBatchSize = _meter.CreateHistogram<int>(
            "agent.parallel_batch_size",
            description: "Distribution of parallel tool batch sizes");

        _semaphoreWaitDuration = _meter.CreateHistogram<double>(
            "agent.semaphore_wait_duration",
            unit: "ms",
            description: "Time tools wait for semaphore slots (contention)");

        _stateMessageCountHistogram = _meter.CreateHistogram<int>(
            "agent.state.message_count",
            description: "Distribution of message counts in AgentLoopState");

        _turnHistoryCountHistogram = _meter.CreateHistogram<int>(
            "agent.turn_history.message_count",
            description: "Distribution of message counts in turn history");

        _deltaMessageCountHistogram = _meter.CreateHistogram<int>(
            "agent.delta_sending.message_count",
            description: "Distribution of message counts sent in delta mode");

        _permissionCheckDuration = _meter.CreateHistogram<double>(
            "agent.permission.check_duration",
            unit: "ms",
            description: "Permission check duration in milliseconds");

        _containerMemberCountHistogram = _meter.CreateHistogram<int>(
            "agent.container.member_count",
            description: "Distribution of container member counts");

        _retryDelayHistogram = _meter.CreateHistogram<double>(
            "agent.retry.delay",
            unit: "ms",
            description: "Retry delay durations");

        _reductionTokenSavingsHistogram = _meter.CreateHistogram<int>(
            "agent.reduction.token_savings",
            description: "Distribution of token savings from reduction");

        _nestingDepthHistogram = _meter.CreateHistogram<int>(
            "agent.nesting.depth",
            description: "Distribution of agent nesting depths");

        _checkpointSizeHistogram = _meter.CreateHistogram<int>(
            "agent.checkpoint.size",
            unit: "bytes",
            description: "Distribution of checkpoint sizes");
    }

    public Task OnEventAsync(AgentEvent evt, CancellationToken ct = default)
    {
        switch (evt)
        {
            // Iteration tracking
            case IterationStartEvent e:
                _iterations.Add(1,
                    new KeyValuePair<string, object?>("agent.name", e.AgentName),
                    new KeyValuePair<string, object?>("iteration", e.Iteration));

                _stateMessageCountHistogram.Record(e.CurrentMessageCount,
                    new KeyValuePair<string, object?>("agent.name", e.AgentName),
                    new KeyValuePair<string, object?>("iteration", e.Iteration));

                _turnHistoryCountHistogram.Record(e.TurnHistoryMessageCount,
                    new KeyValuePair<string, object?>("agent.name", e.AgentName),
                    new KeyValuePair<string, object?>("iteration", e.Iteration));
                break;

            // Decisions
            case AgentDecisionEvent e:
                _decisions.Add(1,
                    new KeyValuePair<string, object?>("agent.name", e.AgentName),
                    new KeyValuePair<string, object?>("decision.type", e.DecisionType));
                break;

            // Circuit breaker
            case CircuitBreakerTriggeredEvent e:
                _circuitBreakerTriggers.Add(1,
                    new KeyValuePair<string, object?>("agent.name", e.AgentName),
                    new KeyValuePair<string, object?>("function.name", e.FunctionName),
                    new KeyValuePair<string, object?>("consecutive.count", e.ConsecutiveCount));
                break;

            // Permission checks
            case PermissionCheckEvent e:
                _permissionChecks.Add(1,
                    new KeyValuePair<string, object?>("agent.name", e.AgentName),
                    new KeyValuePair<string, object?>("function.name", e.FunctionName),
                    new KeyValuePair<string, object?>("approved", e.IsApproved));

                _permissionCheckDuration.Record(e.Duration.TotalMilliseconds,
                    new KeyValuePair<string, object?>("agent.name", e.AgentName),
                    new KeyValuePair<string, object?>("function.name", e.FunctionName),
                    new KeyValuePair<string, object?>("approved", e.IsApproved));

                if (!e.IsApproved)
                {
                    _permissionDenials.Add(1,
                        new KeyValuePair<string, object?>("agent.name", e.AgentName),
                        new KeyValuePair<string, object?>("function.name", e.FunctionName),
                        new KeyValuePair<string, object?>("reason", e.DenialReason ?? "unknown"));
                }
                break;

            // Container expansions
            case ContainerExpandedEvent e:
                _containerExpansions.Add(1,
                    new KeyValuePair<string, object?>("container.name", e.ContainerName),
                    new KeyValuePair<string, object?>("container.type", e.Type.ToString()),
                    new KeyValuePair<string, object?>("unlocked.count", e.UnlockedFunctions.Count));

                _containerMemberCountHistogram.Record(e.UnlockedFunctions.Count,
                    new KeyValuePair<string, object?>("container.name", e.ContainerName),
                    new KeyValuePair<string, object?>("container.type", e.Type.ToString()));
                break;

            // Retries (consolidated)
            case InternalRetryEvent e:
                switch (e.Status)
                {
                    case RetryStatus.Attempting:
                        _retryAttempts.Add(1,
                            new KeyValuePair<string, object?>("agent.name", e.AgentName),
                            new KeyValuePair<string, object?>("function.name", e.FunctionName),
                            new KeyValuePair<string, object?>("attempt", e.AttemptNumber));

                        if (e.RetryDelay.HasValue)
                        {
                            _retryDelayHistogram.Record(e.RetryDelay.Value.TotalMilliseconds,
                                new KeyValuePair<string, object?>("agent.name", e.AgentName),
                                new KeyValuePair<string, object?>("function.name", e.FunctionName));
                        }
                        break;

                    case RetryStatus.Exhausted:
                        _retryExhaustions.Add(1,
                            new KeyValuePair<string, object?>("agent.name", e.AgentName),
                            new KeyValuePair<string, object?>("function.name", e.FunctionName),
                            new KeyValuePair<string, object?>("total.attempts", e.AttemptNumber),
                            new KeyValuePair<string, object?>("error", e.ErrorMessage ?? "unknown"));
                        break;
                }
                break;

            // Parallel tool execution
            case InternalParallelToolExecutionEvent e:
                _parallelToolExecutions.Add(1,
                    new KeyValuePair<string, object?>("agent.name", e.AgentName),
                    new KeyValuePair<string, object?>("iteration", e.Iteration),
                    new KeyValuePair<string, object?>("is.parallel", e.IsParallel));

                _parallelBatchSize.Record(e.ParallelBatchSize,
                    new KeyValuePair<string, object?>("agent.name", e.AgentName),
                    new KeyValuePair<string, object?>("tool.count", e.ToolCount));

                if (e.SemaphoreWaitDuration.HasValue)
                {
                    _semaphoreWaitDuration.Record(e.SemaphoreWaitDuration.Value.TotalMilliseconds,
                        new KeyValuePair<string, object?>("agent.name", e.AgentName));
                }
                break;

            // Cache tracking
            case HistoryReductionCacheEvent e:
                if (e.IsHit)
                {
                    _reductionCacheHits.Add(1,
                        new KeyValuePair<string, object?>("agent.name", e.AgentName));
                }
                else
                {
                    _reductionCacheMisses.Add(1,
                        new KeyValuePair<string, object?>("agent.name", e.AgentName));
                }

                if (e.TokenSavings.HasValue)
                {
                    _reductionTokenSavingsHistogram.Record(e.TokenSavings.Value,
                        new KeyValuePair<string, object?>("agent.name", e.AgentName),
                        new KeyValuePair<string, object?>("is.hit", e.IsHit));
                }
                break;

            // Checkpoint operations (consolidated)
            case CheckpointEvent e:
                switch (e.Operation)
                {
                    case CheckpointOperation.Saved:
                        if (e.Duration.HasValue)
                        {
                            _checkpointDuration.Record(e.Duration.Value.TotalMilliseconds,
                                new KeyValuePair<string, object?>("thread.id", e.SessionId),
                                new KeyValuePair<string, object?>("success", e.Success ?? false));
                        }

                        if (e.SizeBytes.HasValue)
                        {
                            _checkpointSizeHistogram.Record(e.SizeBytes.Value,
                                new KeyValuePair<string, object?>("thread.id", e.SessionId),
                                new KeyValuePair<string, object?>("success", e.Success ?? false));
                        }

                        if (e.Success == false)
                        {
                            _checkpointErrors.Add(1,
                                new KeyValuePair<string, object?>("thread.id", e.SessionId),
                                new KeyValuePair<string, object?>("iteration", e.Iteration ?? 0),
                                new KeyValuePair<string, object?>("error", e.ErrorMessage ?? "unknown"));
                        }
                        break;

                    case CheckpointOperation.Restored:
                        if (e.Duration.HasValue)
                        {
                            _checkpointRestoreDuration.Record(e.Duration.Value.TotalMilliseconds,
                                new KeyValuePair<string, object?>("thread.id", e.SessionId),
                                new KeyValuePair<string, object?>("from.iteration", e.Iteration ?? 0),
                                new KeyValuePair<string, object?>("message.count", e.MessageCount ?? 0));
                        }
                        break;

                    case CheckpointOperation.Cleared:
                        // Uncommitted turn deleted after successful turn completion
                        break;
                }
                break;

            // Document processing
            case DocumentProcessedEvent e:
                _documentProcessing.Add(1,
                    new KeyValuePair<string, object?>("agent.name", e.AgentName));
                _documentProcessingDuration.Record(e.Duration.TotalMilliseconds,
                    new KeyValuePair<string, object?>("agent.name", e.AgentName),
                    new KeyValuePair<string, object?>("size.bytes", e.SizeBytes));
                break;

            // Nested agent calls
            case NestedAgentInvokedEvent e:
                _nestedAgentCalls.Add(1,
                    new KeyValuePair<string, object?>("orchestrator.name", e.OrchestratorName),
                    new KeyValuePair<string, object?>("child.name", e.ChildAgentName));

                _nestingDepthHistogram.Record(e.NestingDepth,
                    new KeyValuePair<string, object?>("orchestrator.name", e.OrchestratorName),
                    new KeyValuePair<string, object?>("child.name", e.ChildAgentName));
                break;

            // Completion
            case AgentCompletionEvent e:
                _completions.Add(1,
                    new KeyValuePair<string, object?>("agent.name", e.AgentName),
                    new KeyValuePair<string, object?>("iterations", e.TotalIterations));
                _completionDuration.Record(e.Duration.TotalMilliseconds,
                    new KeyValuePair<string, object?>("agent.name", e.AgentName));
                break;

            // Message turn tracking
            case MessageTurnFinishedEvent e:
                _messageTurnDuration.Record(e.Duration.TotalMilliseconds,
                    new KeyValuePair<string, object?>("agent.name", e.AgentName));
                break;

            // Delta sending activation
            case DeltaSendingActivatedEvent e:
                _deltaMessageCountHistogram.Record(e.MessageCountSent,
                    new KeyValuePair<string, object?>("agent.name", e.AgentName));
                break;

            // State snapshots
            case StateSnapshotEvent e:
                _stateSnapshots.Add(1,
                    new KeyValuePair<string, object?>("agent.name", e.AgentName),
                    new KeyValuePair<string, object?>("iteration", e.CurrentIteration),
                    new KeyValuePair<string, object?>("terminated", e.IsTerminated));
                break;

        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _meter?.Dispose();
    }
}
