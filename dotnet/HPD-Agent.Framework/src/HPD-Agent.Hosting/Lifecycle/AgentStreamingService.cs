using HPD.Agent;
using HPD.Agent.Hosting.Data;
using Microsoft.Extensions.Logging;

namespace HPD.Agent.Hosting.Lifecycle;

public sealed class AgentStreamingService : IAgentStreamingService
{
    private readonly SessionManager _sessionManager;
    private readonly AgentManager _agentManager;
    private readonly ILogger<AgentStreamingService>? _logger;

    public AgentStreamingService(
        SessionManager sessionManager,
        AgentManager agentManager,
        ILogger<AgentStreamingService>? logger = null)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _agentManager = agentManager ?? throw new ArgumentNullException(nameof(agentManager));
        _logger = logger;
    }

    public async Task<AgentServiceResult<ThreadEventObservationLease>> ObserveThreadEventsAsync(
        string agentId,
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        if (await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult<ThreadEventObservationLease>.NotFound;

        var key = new ThreadKey(sessionId, threadId);
        if (await _sessionManager.Store.GetThreadAsync(key, cancellationToken).ConfigureAwait(false) == null)
            return AgentServiceResult<ThreadEventObservationLease>.NotFound;

        var liveEvents = _agentManager.CreateRuntimeEventInbox(
            agentId,
            sessionId,
            threadId,
            HPD.Events.EventInboxOptions.Deterministic());
        return AgentServiceResult<ThreadEventObservationLease>.Success(
            new ThreadEventObservationLease(_sessionManager.Store, key, liveEvents));
    }

    public async Task<AgentServiceResult<InputSubmissionDto>> SubmitInputAsync(
        string agentId,
        string sessionId,
        string threadId,
        AgentInputEvent input,
        CancellationToken cancellationToken = default)
    {
        var registration = AgentInputDispatcher.GetBuiltInRegistration(input.GetType());
        if (registration.Delivery == AgentInputDelivery.ActiveControl)
            return await SubmitActiveControlAsync(agentId, sessionId, threadId, input, cancellationToken)
                .ConfigureAwait(false);

        var lease = await GetAgentForExecutionAsync(agentId, sessionId, threadId, cancellationToken)
            .ConfigureAwait(false);
        if (lease.Status != AgentServiceStatus.Success)
            return new AgentServiceResult<InputSubmissionDto>(lease.Status, default, lease.ErrorCode, lease.ErrorMessage, lease.ErrorMessages);

        var agent = lease.Value!;
        var runtimePin = _agentManager.PinRuntime(agentId, sessionId, threadId);
        if (!_sessionManager.TryReserveThreadExecution(agentId, sessionId, threadId, out var execution))
        {
            runtimePin.Dispose();
            return AgentServiceResult<InputSubmissionDto>.ConflictWith(
                "ThreadExecutionActive",
                $"Thread '{threadId}' in session '{sessionId}' already has an active execution.");
        }

        input = ApplyRouteScope(input, agentId, sessionId, threadId, execution.ThreadExecutionId);
        var publisher = new ThreadEventPublisher(_sessionManager.Store, agent.EventCoordinator);
        var startCommitted = false;

        try
        {
            await agent.StartAsync(input.RunConfig, CancellationToken.None).ConfigureAwait(false);
            await publisher.CommitAndPublishAsync(
                new ThreadKey(sessionId, threadId),
                new ThreadExecutionStartedEvent(execution.ThreadExecutionId, agentId, execution.StartedAt)
                {
                    SessionId = sessionId,
                    ThreadId = threadId
                },
                cancellationToken).ConfigureAwait(false);
            startCommitted = true;

            if (!_sessionManager.ActivateThreadExecution(sessionId, threadId, execution.ThreadExecutionId))
                throw new InvalidOperationException($"Thread execution '{execution.ThreadExecutionId}' lost its reserved ownership before activation.");

            var submission = await agent.EnqueueAsync(input, CancellationToken.None).ConfigureAwait(false);
            _ = FinishExecutionAsync(execution, submission, publisher, runtimePin);
        }
        catch (Exception ex)
        {
            if (startCommitted)
            {
                await CommitTerminalAsync(
                    execution,
                    publisher,
                    cancelled: ex is OperationCanceledException,
                    error: ex,
                    CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                _sessionManager.ReleaseThreadExecution(sessionId, threadId, execution.ThreadExecutionId);
            }
            runtimePin.Dispose();
            throw;
        }

        return AgentServiceResult<InputSubmissionDto>.Success(
            new InputSubmissionDto(
                "queued",
                execution.ThreadExecutionId,
                execution.StartedAt));
    }

    private async Task<AgentServiceResult<InputSubmissionDto>> SubmitActiveControlAsync(
        string routeAgentId,
        string sessionId,
        string threadId,
        AgentInputEvent input,
        CancellationToken cancellationToken)
    {
        var activeExecution = _sessionManager.GetActiveThreadExecution(sessionId, threadId);
        if (activeExecution is null)
        {
            return AgentServiceResult<InputSubmissionDto>.Success(new InputSubmissionDto(
                "no_active_execution",
                input.ThreadExecutionId));
        }

        if (!string.Equals(routeAgentId, activeExecution.AgentId, StringComparison.Ordinal))
        {
            return AgentServiceResult<InputSubmissionDto>.ConflictWith(
                "ActiveExecutionAgentMismatch",
                $"Active execution '{activeExecution.ThreadExecutionId}' belongs to agent '{activeExecution.AgentId}'.");
        }

        if (string.IsNullOrWhiteSpace(input.ThreadExecutionId))
        {
            return AgentServiceResult<InputSubmissionDto>.Validation(
                "ThreadExecutionIdRequired",
                "Active-control input requires ThreadExecutionId.");
        }

        if (!string.Equals(input.ThreadExecutionId, activeExecution.ThreadExecutionId, StringComparison.Ordinal))
        {
            return AgentServiceResult<InputSubmissionDto>.Success(new InputSubmissionDto(
                "active_execution_mismatch",
                input.ThreadExecutionId,
                ActiveExecution: ToExecutionDto(activeExecution)));
        }

        var agent = _agentManager.GetRuntimeAgent(
            activeExecution.AgentId,
            activeExecution.SessionId,
            activeExecution.ThreadId);
        if (agent is null)
        {
            return AgentServiceResult<InputSubmissionDto>.ConflictWith(
                "ActiveRuntimeUnavailable",
                $"Active execution '{activeExecution.ThreadExecutionId}' has no cached runtime.");
        }

        var scoped = ApplyRouteScope(input, routeAgentId, sessionId, threadId);
        var result = await agent.RunAsync(scoped, cancellationToken).ConfigureAwait(false);
        return AgentServiceResult<InputSubmissionDto>.Success(new InputSubmissionDto(
            ToWireDisposition(result.Disposition),
            result.ThreadExecutionId ?? activeExecution.ThreadExecutionId,
            ActiveExecution: ToExecutionDto(activeExecution)));
    }

    private async Task FinishExecutionAsync(
        ThreadExecutionState execution,
        AgentRuntimeInputSubmission submission,
        IThreadEventPublisher publisher,
        IDisposable runtimePin)
    {
        try
        {
            var outcome = await submission.Completion.ConfigureAwait(false);
            await CommitTerminalAsync(
                execution,
                publisher,
                outcome.Cancelled,
                outcome.Error,
                CancellationToken.None).ConfigureAwait(false);
            runtimePin.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.LogCritical(
                ex,
                "Thread execution {ThreadExecutionId} could not commit its terminal lifecycle fact; ownership remains active.",
                execution.ThreadExecutionId);
        }
    }

    private static string ToWireDisposition(AgentInputDisposition disposition) => disposition switch
    {
        AgentInputDisposition.Completed => "completed",
        AgentInputDisposition.Queued => "queued",
        AgentInputDisposition.Accepted => "accepted",
        AgentInputDisposition.NoActiveExecution => "no_active_execution",
        AgentInputDisposition.ActiveExecutionMismatch => "active_execution_mismatch",
        AgentInputDisposition.ActiveInputNotSteerable => "active_input_not_steerable",
        AgentInputDisposition.ExecutionFinishing => "execution_finishing",
        _ => throw new ArgumentOutOfRangeException(nameof(disposition), disposition, null)
    };

    private async Task CommitTerminalAsync(
        ThreadExecutionState execution,
        IThreadEventPublisher publisher,
        bool cancelled,
        Exception? error,
        CancellationToken cancellationToken)
    {
        await publisher.CommitAndPublishAsync(
            new ThreadKey(execution.SessionId, execution.ThreadId),
            new ThreadExecutionFinishedEvent(
                execution.ThreadExecutionId,
                execution.AgentId,
                cancelled
                    ? ThreadExecutionOutcome.Cancelled
                    : error is not null ? ThreadExecutionOutcome.Failed : ThreadExecutionOutcome.Succeeded,
                DateTimeOffset.UtcNow,
                cancelled || error is null
                    ? null
                    : new ThreadExecutionError(error.GetType().Name, error.Message))
            {
                SessionId = execution.SessionId,
                ThreadId = execution.ThreadId
            },
            cancellationToken).ConfigureAwait(false);

        if (!_sessionManager.ReleaseThreadExecution(execution.SessionId, execution.ThreadId, execution.ThreadExecutionId))
        {
            throw new InvalidOperationException(
                $"Thread execution '{execution.ThreadExecutionId}' committed its terminal fact but no longer owned its runtime slot.");
        }
    }

    public async Task<AgentServiceResult<ThreadRuntimeStateDto>> GetThreadStateAsync(
        string agentId,
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var head = await _sessionManager.Store.GetThreadEventHeadAsync(
            new ThreadKey(sessionId, threadId), cancellationToken).ConfigureAwait(false);
        if (head is null)
            return AgentServiceResult<ThreadRuntimeStateDto>.NotFound;

        var activeState = _sessionManager.GetActiveThreadExecution(sessionId, threadId);
        ThreadExecutionDto? activeExecution = activeState is not null && activeState.AgentId == agentId
            ? ToExecutionDto(activeState)
            : null;

        var pendingRequests = _agentManager
            .GetRuntimeAgent(agentId, sessionId, threadId)?
            .EventCoordinator
            .GetPendingRequests()
            .Where(item => item.Request is AgentEvent)
            .Select(item => new PendingAgentRequestDto(
                (AgentEvent)item.Request,
                item.Session.CreatedAt))
            .ToArray() ?? [];

        return AgentServiceResult<ThreadRuntimeStateDto>.Success(new ThreadRuntimeStateDto(
            head.Cursor,
            activeExecution,
            pendingRequests));
    }

    public async Task<AgentServiceResult<ThreadContextUsage>> EstimateContextUsageAsync(
        string agentId,
        string sessionId,
        string threadId,
        AgentRunConfig? runConfig,
        CancellationToken cancellationToken = default)
    {
        if (await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult<ThreadContextUsage>.NotFound;

        var thread = await _sessionManager.Store.ProjectThreadAsync(sessionId, threadId, ThreadProjectionPurpose.ThreadHistory, cancellationToken)
            .ConfigureAwait(false);
        if (thread == null)
            return AgentServiceResult<ThreadContextUsage>.NotFound;

        var estimator = new ThreadContextUsageEstimator();
        var usage = await estimator.EstimateAsync(thread, runConfig ?? new AgentRunConfig(), cancellationToken)
            .ConfigureAwait(false);
        return AgentServiceResult<ThreadContextUsage>.Success(usage);
    }

    public AgentInputEvent ApplyRouteScope(
        AgentInputEvent input,
        string agentId,
        string sessionId,
        string threadId,
        string? threadExecutionId = null)
    {
        return input with
        {
            AgentId = agentId,
            SessionId = sessionId,
            ThreadId = threadId,
            ThreadExecutionId = threadExecutionId ?? input.ThreadExecutionId
        };
    }

    private static ThreadExecutionDto ToExecutionDto(ThreadExecutionState execution) => new(
        execution.ThreadExecutionId,
        execution.AgentId,
        execution.SessionId,
        execution.ThreadId,
        ThreadExecutionStatus.Active,
        execution.StartedAt,
        null,
        null,
        null,
        [],
        []);

    private async Task<AgentServiceResult<Agent>> GetAgentForExecutionAsync(
        string agentId,
        string sessionId,
        string threadId,
        CancellationToken cancellationToken)
    {
        if (await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken).ConfigureAwait(false) == null)
            return AgentServiceResult<Agent>.NotFound;
        if (await _sessionManager.Store.GetThreadAsync(new ThreadKey(sessionId, threadId), cancellationToken)
                .ConfigureAwait(false) == null)
            return AgentServiceResult<Agent>.NotFound;
        var agent = await _agentManager.GetOrBuildAgentRuntimeAsync(agentId, sessionId, threadId, cancellationToken)
            .ConfigureAwait(false);
        return AgentServiceResult<Agent>.Success(agent);
    }

}
