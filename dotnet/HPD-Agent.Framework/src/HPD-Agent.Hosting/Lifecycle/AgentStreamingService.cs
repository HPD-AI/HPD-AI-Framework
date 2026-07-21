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
        var lease = await GetAgentForExecutionAsync(agentId, sessionId, threadId, cancellationToken)
            .ConfigureAwait(false);
        if (lease.Status != AgentServiceStatus.Success)
            return new AgentServiceResult<InputSubmissionDto>(lease.Status, default, lease.ErrorCode, lease.ErrorMessage, lease.ErrorMessages);

        if (!_sessionManager.TryReserveThreadExecution(agentId, sessionId, threadId, out var execution))
        {
            return AgentServiceResult<InputSubmissionDto>.ConflictWith(
                "ThreadExecutionActive",
                $"Thread '{threadId}' in session '{sessionId}' already has an active execution.");
        }

        var agent = lease.Value!;
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
            _ = FinishExecutionAsync(execution, submission, publisher);
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
            throw;
        }

        return AgentServiceResult<InputSubmissionDto>.Success(
            new InputSubmissionDto(
                execution.ThreadExecutionId,
                execution.StartedAt));
    }

    private async Task FinishExecutionAsync(
        ThreadExecutionState execution,
        AgentRuntimeInputSubmission submission,
        IThreadEventPublisher publisher)
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
        }
        catch (Exception ex)
        {
            _logger?.LogCritical(
                ex,
                "Thread execution {ThreadExecutionId} could not commit its terminal lifecycle fact; ownership remains active.",
                execution.ThreadExecutionId);
        }
    }

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

    public async Task<AgentServiceResult<InterruptionSubmissionDto>> InterruptAsync(
        string agentId,
        string sessionId,
        string threadId,
        string? expectedThreadExecutionId,
        InterruptionRequestEvent interruption,
        CancellationToken cancellationToken = default)
    {
        var lease = await GetAgentForExecutionAsync(agentId, sessionId, threadId, cancellationToken)
            .ConfigureAwait(false);
        if (lease.Status != AgentServiceStatus.Success)
            return new AgentServiceResult<InterruptionSubmissionDto>(
                lease.Status,
                default,
                lease.ErrorCode,
                lease.ErrorMessage,
                lease.ErrorMessages);

        var activeExecution = _sessionManager.GetActiveThreadExecution(sessionId, threadId);
        if (activeExecution == null)
        {
            var events = await _sessionManager.GetThreadExecutionProjectionEventsAsync(sessionId, threadId, cancellationToken)
                .ConfigureAwait(false);
            var expectedRun = string.IsNullOrWhiteSpace(expectedThreadExecutionId)
                ? null
                : ThreadExecutionProjector.Project(agentId, sessionId, threadId, events ?? [])
                    .LastOrDefault(execution => execution.ThreadExecutionId == expectedThreadExecutionId);
            return AgentServiceResult<InterruptionSubmissionDto>.Success(new InterruptionSubmissionDto(
                expectedRun is null ? "no_active_execution" : "already_terminal"));
        }

        if (!string.IsNullOrWhiteSpace(expectedThreadExecutionId) &&
            !string.Equals(expectedThreadExecutionId, activeExecution.ThreadExecutionId, StringComparison.Ordinal))
        {
            return AgentServiceResult<InterruptionSubmissionDto>.Success(new InterruptionSubmissionDto(
                "active_execution_mismatch",
                ToExecutionDto(activeExecution)));
        }

        var scoped = interruption with
        {
            AgentId = agentId,
            SessionId = sessionId,
            ThreadId = threadId,
            ThreadExecutionId = activeExecution.ThreadExecutionId
        };

        await lease.Value!.RunAsync(scoped, cancellationToken).ConfigureAwait(false);
        return AgentServiceResult<InterruptionSubmissionDto>.Success(new InterruptionSubmissionDto(
            "accepted",
            ToExecutionDto(activeExecution)));
    }

    public AgentInputEvent ApplyRouteScope(
        AgentInputEvent input,
        string agentId,
        string sessionId,
        string threadId,
        string? threadExecutionId = null)
    {
        return input switch
        {
            UserMessagesInputEvent messages => messages with
            {
                ClientInputId = messages.ClientInputId,
                AgentId = agentId,
                SessionId = sessionId,
                ThreadId = threadId,
                ThreadExecutionId = threadExecutionId ?? messages.ThreadExecutionId,
                RunConfig = messages.RunConfig
            },
            CompactThreadInputEvent compact => compact with
            {
                ClientInputId = compact.ClientInputId,
                AgentId = agentId,
                SessionId = sessionId,
                ThreadId = threadId,
                ThreadExecutionId = threadExecutionId ?? compact.ThreadExecutionId,
                RunConfig = compact.RunConfig
            },
            InterruptionRequestEvent interruption => interruption with
            {
                AgentId = agentId,
                SessionId = sessionId,
                ThreadId = threadId,
                ThreadExecutionId = threadExecutionId ?? interruption.ThreadExecutionId,
                RunConfig = interruption.RunConfig
            },
            BackgroundTaskNotificationInputEvent notification => notification with
            {
                ClientInputId = notification.ClientInputId,
                AgentId = agentId,
                SessionId = sessionId,
                ThreadId = threadId,
                ThreadExecutionId = threadExecutionId ?? notification.ThreadExecutionId,
                RunConfig = notification.RunConfig
            },
            _ => input
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
