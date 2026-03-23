using Microsoft.Extensions.Logging;

namespace HPD.Agent;

/// <summary>
/// Observes agent events and emits structured logs via ILogger.
/// Replaces AgentLoggingService with event-driven approach.
/// </summary>
public class LoggingEventObserver : IAgentEventObserver
{
    private readonly ILogger<LoggingEventObserver> _logger;
    private readonly bool _enableSensitiveData;

    public LoggingEventObserver(ILogger<LoggingEventObserver> logger, bool enableSensitiveData = false)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _enableSensitiveData = enableSensitiveData;
    }

    public Task OnEventAsync(AgentEvent evt, CancellationToken ct = default)
    {
        switch (evt)
        {
            // Collapsing
            case CollapsedToolsVisibleEvent e:
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace(
                        "Agent '{AgentName}' iteration {Iteration}: Collapsed tools sent to LLM (count={Count}): [{Tools}]",
                        e.AgentName, e.Iteration, e.TotalToolCount,
                        string.Join(", ", e.VisibleToolNames));
                }
                break;

            // Container expansion
            case ContainerExpandedEvent e:
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace(
                        "Container '{Container}' ({Type}) expanded at iteration {Iteration}: unlocked {Count} functions: [{Functions}]",
                        e.ContainerName, e.Type, e.Iteration, e.UnlockedFunctions.Count,
                        string.Join(", ", e.UnlockedFunctions));
                }
                break;

            // Permission checks
            case PermissionCheckEvent e:
                if (!e.IsApproved)
                {
                    _logger.LogWarning(
                        "Agent '{AgentName}' iteration {Iteration}: Permission DENIED for function '{Function}': {Reason}",
                        e.AgentName, e.Iteration, e.FunctionName, e.DenialReason);
                }
                else if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace(
                        "Agent '{AgentName}' iteration {Iteration}: Permission APPROVED for function '{Function}'",
                        e.AgentName, e.Iteration, e.FunctionName);
                }
                break;

            // Circuit breaker
            case CircuitBreakerTriggeredEvent e:
                _logger.LogWarning(
                    "Agent '{AgentName}' iteration {Iteration}: Circuit breaker triggered for '{Function}' ({Count} consecutive calls)",
                    e.AgentName, e.Iteration, e.FunctionName, e.ConsecutiveCount);
                break;

            // Iteration tracking
            case IterationStartEvent e:
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Agent '{AgentName}' iteration {Iteration}/{MaxIterations} started: " +
                        "Messages={Messages}, History={History}, TurnHistory={TurnHistory}, CompletedFunctions={Functions}",
                        e.AgentName, e.Iteration, e.MaxIterations,
                        e.CurrentMessageCount, e.HistoryMessageCount, e.TurnHistoryMessageCount,
                        e.CompletedFunctionsCount);
                }
                break;

            // Turn boundaries
            case MessageTurnStartedEvent e:
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Agent '{AgentName}' message turn started: {TurnId}",
                        e.AgentName, e.MessageTurnId);
                }
                break;

            case MessageTurnFinishedEvent e:
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Agent '{AgentName}' message turn ended: {TurnId} (Duration: {Duration}ms)",
                        e.AgentName, e.MessageTurnId, e.Duration.TotalMilliseconds);
                }
                break;

            // Agent turn boundaries (skip - already logged by Microsoft's LoggingChatClient)
            case AgentTurnStartedEvent:
            case AgentTurnFinishedEvent:
                // Already logged by Microsoft's LoggingChatClient at API level
                // Skip to avoid duplication
                break;

            // History reduction cache
            case HistoryReductionCacheEvent e:
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    if (e.IsHit)
                    {
                        _logger.LogDebug(
                            "Agent '{AgentName}' history reduction cache HIT: Reusing reduction from {CreatedAt}, " +
                            "summarized {SummarizedCount} messages, current count: {CurrentCount}",
                            e.AgentName, e.ReductionCreatedAt, e.SummarizedUpToIndex, e.CurrentMessageCount);
                    }
                    else
                    {
                        _logger.LogDebug(
                            "Agent '{AgentName}' history reduction cache MISS: Current count: {CurrentCount}",
                            e.AgentName, e.CurrentMessageCount);
                    }
                }
                break;

            // Checkpoint operations (consolidated)
            case CheckpointEvent e:
                switch (e.Operation)
                {
                    case CheckpointOperation.Saved:
                        if (e.Success == true)
                        {
                            _logger.LogInformation(
                                "Checkpoint saved for thread '{SessionId}' at iteration {Iteration} in {Duration}ms",
                                e.SessionId, e.Iteration, e.Duration?.TotalMilliseconds ?? 0);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Checkpoint save failed for thread '{SessionId}' at iteration {Iteration}: {Error}",
                                e.SessionId, e.Iteration, e.ErrorMessage);
                        }
                        break;

                    case CheckpointOperation.Restored:
                        _logger.LogInformation(
                            "Checkpoint restored for thread '{SessionId}' from iteration {Iteration} ({MessageCount} messages) in {Duration}ms",
                            e.SessionId, e.Iteration, e.MessageCount, e.Duration?.TotalMilliseconds ?? 0);
                        break;

                    case CheckpointOperation.Cleared:
                        if (_logger.IsEnabled(LogLevel.Debug))
                        {
                            _logger.LogDebug(
                                "Uncommitted turn cleared for thread '{SessionId}' at iteration {Iteration}",
                                e.SessionId, e.Iteration);
                        }
                        break;
                }
                break;

            // Retry events (consolidated)
            case InternalRetryEvent e:
                switch (e.Status)
                {
                    case RetryStatus.Attempting:
                        _logger.LogWarning(
                            "Agent '{AgentName}' retrying function '{Function}' (attempt {Attempt}/{MaxRetries}): {Error}",
                            e.AgentName, e.FunctionName, e.AttemptNumber, e.MaxRetries, e.ErrorMessage);
                        break;

                    case RetryStatus.Exhausted:
                        _logger.LogError(
                            "Agent '{AgentName}' retry exhausted for function '{Function}' after {Attempts} attempts: {Error}",
                            e.AgentName, e.FunctionName, e.AttemptNumber, e.ErrorMessage);
                        break;
                }
                break;

            // Parallel tool execution
            case InternalParallelToolExecutionEvent e:
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Agent '{AgentName}' iteration {Iteration}: Executed {Total} tools " +
                        "(batch={Batch}, approved={Approved}, denied={Denied}) [{Duration}ms]",
                        e.AgentName, e.Iteration, e.ToolCount, e.ParallelBatchSize,
                        e.ApprovedCount, e.DeniedCount, e.Duration.TotalMilliseconds);

                    if (e.SemaphoreWaitDuration.HasValue && e.SemaphoreWaitDuration.Value.TotalMilliseconds > 100)
                    {
                        _logger.LogDebug(
                            "Agent '{AgentName}' semaphore contention detected: waited {Wait}ms for slots",
                            e.AgentName, e.SemaphoreWaitDuration.Value.TotalMilliseconds);
                    }
                }
                break;

            // Agent decisions
            case AgentDecisionEvent e:
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Agent '{AgentName}' iteration {Iteration}: Decision={Decision}, " +
                        "State(Failures={Failures}, Functions={Functions})",
                        e.AgentName, e.Iteration, e.DecisionType,
                        e.ConsecutiveFailures, e.CompletedFunctionsCount);
                }
                break;

            // Collapsing state (from ToolCollapsingMiddleware)
            case CollapsingStateEvent e:
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Agent '{AgentName}' iteration {Iteration}: Collapsing(ExpandedToolkits={Toolkits}, ExpandedSkills={Skills})",
                        e.AgentName, e.Iteration, e.ExpandedToolkitsCount, e.ExpandedSkillsCount);
                }
                break;

            // Document processing
            case DocumentProcessedEvent e:
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        "Agent '{AgentName}' processed document '{Path}' ({SizeMB:F2} MB) in {Duration}ms",
                        e.AgentName, e.DocumentPath, e.SizeBytes / 1024.0 / 1024.0, e.Duration.TotalMilliseconds);
                }
                break;

            // Message preparation
            case InternalMessagePreparedEvent e:
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace(
                        "Agent '{AgentName}' iteration {Iteration}: Message preparation complete ({MessageCount} messages)",
                        e.AgentName, e.Iteration, e.FinalMessageCount);
                }
                break;

            // Delta sending activation
            case DeltaSendingActivatedEvent e:
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Agent '{AgentName}': Delta sending activated ({MessageCount} messages sent)",
                        e.AgentName, e.MessageCountSent);
                }
                break;

            // Plan mode activation
            case PlanModeActivatedEvent e:
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Agent '{AgentName}': Plan mode activated",
                        e.AgentName);
                }
                break;

            // Nested agent invocation
            case NestedAgentInvokedEvent e:
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        "Agent '{OrchestratorName}' invoked nested agent '{ChildName}'",
                        e.OrchestratorName, e.ChildAgentName);
                }
                break;

            // Bidirectional event processing
            case BidirectionalEventProcessedEvent e:
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace(
                        "Agent '{AgentName}' processed bidirectional event '{EventType}' (RequiresResponse: {RequiresResponse})",
                        e.AgentName, e.EventType, e.RequiresResponse);
                }
                break;

            // Completion
            case AgentCompletionEvent e:
                _logger.LogInformation(
                    "Agent '{AgentName}' completed after {Iterations} iterations in {Duration}ms",
                    e.AgentName, e.TotalIterations, e.Duration.TotalMilliseconds);
                break;

            // Iteration messages
            case IterationMessagesEvent e:
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace(
                        "Agent '{AgentName}' iteration {Iteration}: {MessageCount} messages in conversation",
                        e.AgentName, e.Iteration, e.MessageCount);
                }
                break;


            // State snapshot
            case StateSnapshotEvent e:
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace(
                        "Agent '{AgentName}' state snapshot at iteration {Iteration}: " +
                        "Terminated={Terminated}, Reason={Reason}, Errors={Errors}, Functions={Functions}",
                        e.AgentName, e.CurrentIteration, e.IsTerminated, e.TerminationReason,
                        e.ConsecutiveErrorCount, e.CompletedFunctions.Count);
                }
                break;


            // Errors
            case MessageTurnErrorEvent e:
                _logger.LogError(e.Exception, "Agent error: {Message}", e.Message);
                break;
        }

        return Task.CompletedTask;
    }
}
