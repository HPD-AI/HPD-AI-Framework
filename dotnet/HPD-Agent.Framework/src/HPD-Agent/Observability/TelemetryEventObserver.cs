using System.Diagnostics.Metrics;

namespace HPD.Agent;

/// <summary>
/// Observes agent events and emits OpenTelemetry metrics.
/// Metrics provide aggregated statistics for dashboards and alerting.
/// Note: Distributed tracing is handled separately via ActivitySource in Agent.
/// </summary>
public class TelemetryEventObserver : IDisposable
{
    private readonly Meter _meter;

    // Counters
    private readonly Counter<int> _iterations;
    private readonly Counter<int> _decisions;
    private readonly Counter<int> _circuitBreakerTriggers;
    private readonly Counter<int> _permissionChecks;
    private readonly Counter<int> _containerExpansions;
    private readonly Counter<int> _retryAttempts;
    private readonly Counter<int> _documentProcessing;
    private readonly Counter<int> _nestedAgentCalls;
    private readonly Counter<int> _completions;
    private readonly Counter<int> _stateSnapshots;
    private readonly Counter<int> _parallelToolExecutions;
    private readonly Counter<int> _permissionDenials;

    // Histograms
    private readonly Histogram<double> _iterationDuration;
    private readonly Histogram<int> _parallelBatchSize;
    private readonly Histogram<double> _semaphoreWaitDuration;
    private readonly Histogram<double> _documentProcessingDuration;
    private readonly Histogram<double> _completionDuration;
    private readonly Histogram<double> _messageTurnDuration;
    private readonly Histogram<int> _stateMessageCountHistogram;
    private readonly Histogram<int> _turnHistoryCountHistogram;
    private readonly Histogram<int> _deltaMessageCountHistogram;
    private readonly Histogram<double> _permissionCheckDuration;
    private readonly Histogram<int> _containerMemberCountHistogram;
    private readonly Histogram<double> _retryDelayHistogram;
    private readonly Histogram<int> _compactionMessagesRemovedHistogram;
    private readonly Histogram<int> _nestingDepthHistogram;
    private readonly Histogram<long> _usageInputTokensHistogram;
    private readonly Histogram<long> _usageOutputTokensHistogram;
    private readonly Histogram<long> _usageTotalTokensHistogram;
    private readonly Histogram<long> _usageCachedInputTokensHistogram;
    private readonly Histogram<long> _usageReasoningTokensHistogram;
    private readonly Histogram<long> _usageInputAudioTokensHistogram;
    private readonly Histogram<long> _usageInputTextTokensHistogram;
    private readonly Histogram<long> _usageOutputAudioTokensHistogram;
    private readonly Histogram<long> _usageOutputTextTokensHistogram;

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
            description: "Number of ToolHarness/skill container expansions");

        _retryAttempts = _meter.CreateCounter<int>(
            "agent.retry_attempts",
            description: "Number of function retry attempts");

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

        _compactionMessagesRemovedHistogram = _meter.CreateHistogram<int>(
            "agent.compaction.messages_removed",
            description: "Distribution of messages removed by compaction");

        _nestingDepthHistogram = _meter.CreateHistogram<int>(
            "agent.nesting.depth",
            description: "Distribution of agent nesting depths");

        _usageInputTokensHistogram = _meter.CreateHistogram<long>(
            "agent.usage.input_tokens",
            unit: "tokens",
            description: "Input tokens used across a completed agent message turn");

        _usageOutputTokensHistogram = _meter.CreateHistogram<long>(
            "agent.usage.output_tokens",
            unit: "tokens",
            description: "Output tokens used across a completed agent message turn");

        _usageTotalTokensHistogram = _meter.CreateHistogram<long>(
            "agent.usage.total_tokens",
            unit: "tokens",
            description: "Total tokens used across a completed agent message turn");

        _usageCachedInputTokensHistogram = _meter.CreateHistogram<long>(
            "agent.usage.cached_input_tokens",
            unit: "tokens",
            description: "Cached input tokens used across a completed agent message turn");

        _usageReasoningTokensHistogram = _meter.CreateHistogram<long>(
            "agent.usage.reasoning_tokens",
            unit: "tokens",
            description: "Reasoning tokens used across a completed agent message turn");

        _usageInputAudioTokensHistogram = _meter.CreateHistogram<long>(
            "agent.usage.input_audio_tokens",
            unit: "tokens",
            description: "Audio input tokens used across a completed agent message turn");

        _usageInputTextTokensHistogram = _meter.CreateHistogram<long>(
            "agent.usage.input_text_tokens",
            unit: "tokens",
            description: "Text input tokens used across a completed agent message turn");

        _usageOutputAudioTokensHistogram = _meter.CreateHistogram<long>(
            "agent.usage.output_audio_tokens",
            unit: "tokens",
            description: "Audio output tokens used across a completed agent message turn");

        _usageOutputTextTokensHistogram = _meter.CreateHistogram<long>(
            "agent.usage.output_text_tokens",
            unit: "tokens",
            description: "Text output tokens used across a completed agent message turn");
    }

    public ValueTask HandleAsync(AgentEvent evt)
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
                    new KeyValuePair<string, object?>("container.type", e.ContainerType.ToString()),
                    new KeyValuePair<string, object?>("unlocked.count", e.UnlockedFunctions.Count));

                _containerMemberCountHistogram.Record(e.UnlockedFunctions.Count,
                    new KeyValuePair<string, object?>("container.name", e.ContainerName),
                    new KeyValuePair<string, object?>("container.type", e.ContainerType.ToString()));
                break;

            case FunctionRetryEvent e:
                _retryAttempts.Add(1,
                    new KeyValuePair<string, object?>("function.name", e.FunctionName),
                    new KeyValuePair<string, object?>("attempt", e.Attempt));

                _retryDelayHistogram.Record(e.Delay.TotalMilliseconds,
                    new KeyValuePair<string, object?>("function.name", e.FunctionName));
                break;

            case ModelCallRetryEvent e:
                _retryAttempts.Add(1,
                    new KeyValuePair<string, object?>("operation.kind", "model"),
                    new KeyValuePair<string, object?>("attempt", e.Attempt));

                _retryDelayHistogram.Record(e.Delay.TotalMilliseconds,
                    new KeyValuePair<string, object?>("operation.kind", "model"));
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

            case CompactionEvent e:
                if (e.MessagesRemoved.HasValue)
                {
                    _compactionMessagesRemovedHistogram.Record(e.MessagesRemoved.Value,
                        new KeyValuePair<string, object?>("agent.name", e.AgentName),
                        new KeyValuePair<string, object?>("status", e.Status.ToString()));
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
                    new KeyValuePair<string, object?>("agent.id", e.AgentId),
                    new KeyValuePair<string, object?>("agent.name", e.AgentName));
                RecordTurnUsage(e);
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

        return ValueTask.CompletedTask;
    }

    private void RecordTurnUsage(MessageTurnFinishedEvent evt)
    {
        foreach (var operation in evt.Usage.Operations)
        {
            var usage = operation.Usage;
            if (usage is null)
                continue;

            var tags = new KeyValuePair<string, object?>[]
            {
                new("agent.id", evt.AgentId),
                new("agent.name", evt.AgentName),
                new("provider.family", operation.Family.ToString()),
                new("provider.key", operation.ProviderKey),
                new("model.id", operation.ModelId),
                new("provider.operation.kind", operation.OperationKind.ToString()),
                new("provider.operation.outcome", operation.Outcome.ToString())
            };

            RecordIfPresent(_usageInputTokensHistogram, usage.InputTokenCount, tags);
            RecordIfPresent(_usageOutputTokensHistogram, usage.OutputTokenCount, tags);
            RecordIfPresent(_usageCachedInputTokensHistogram, usage.CachedInputTokenCount, tags);
            RecordIfPresent(_usageReasoningTokensHistogram, usage.ReasoningTokenCount, tags);
            RecordIfPresent(_usageInputAudioTokensHistogram, usage.InputAudioTokenCount, tags);
            RecordIfPresent(_usageInputTextTokensHistogram, usage.InputTextTokenCount, tags);
            RecordIfPresent(_usageOutputAudioTokensHistogram, usage.OutputAudioTokenCount, tags);
            RecordIfPresent(_usageOutputTextTokensHistogram, usage.OutputTextTokenCount, tags);

            var totalTokens = usage.TotalTokenCount;
            if (totalTokens is null && (usage.InputTokenCount.HasValue || usage.OutputTokenCount.HasValue))
                totalTokens = (usage.InputTokenCount ?? 0) + (usage.OutputTokenCount ?? 0);

            RecordIfPresent(_usageTotalTokensHistogram, totalTokens, tags);
        }
    }

    private static void RecordIfPresent(
        Histogram<long> histogram,
        long? value,
        KeyValuePair<string, object?>[] tags)
    {
        if (value.HasValue)
            histogram.Record(value.Value, tags);
    }

    public void Dispose()
    {
        _meter?.Dispose();
    }
}
