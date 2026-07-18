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

        if (!_sessionManager.TryReserveThreadRun(agentId, sessionId, threadId, out var run))
        {
            return AgentServiceResult<InputSubmissionDto>.ConflictWith(
                "ThreadRunActive",
                $"Thread '{threadId}' in session '{sessionId}' already has an active run.");
        }

        var agent = lease.Value!;
        input = ApplyRouteScope(input, agentId, sessionId, threadId, run.RuntimeRunId);
        var publisher = new ThreadEventPublisher(_sessionManager.Store, agent.EventCoordinator);
        var startCommitted = false;

        try
        {
            await agent.StartAsync(input.RunConfig, CancellationToken.None).ConfigureAwait(false);
            await publisher.CommitAndPublishAsync(
                new ThreadKey(sessionId, threadId),
                new ThreadRunStartedEvent(run.RuntimeRunId, agentId, run.StartedAt)
                {
                    SessionId = sessionId,
                    ThreadId = threadId
                },
                cancellationToken).ConfigureAwait(false);
            startCommitted = true;

            if (!_sessionManager.ActivateThreadRun(sessionId, threadId, run.RuntimeRunId))
                throw new InvalidOperationException($"Thread run '{run.RuntimeRunId}' lost its reserved ownership before activation.");

            var submission = await agent.EnqueueAsync(input, CancellationToken.None).ConfigureAwait(false);
            _ = CompleteRunAsync(run, submission, publisher);
        }
        catch (Exception ex)
        {
            if (startCommitted)
            {
                await CommitTerminalAsync(
                    run,
                    publisher,
                    cancelled: ex is OperationCanceledException,
                    error: ex,
                    CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                _sessionManager.CompleteThreadRun(sessionId, threadId, run.RuntimeRunId);
            }
            throw;
        }

        return AgentServiceResult<InputSubmissionDto>.Success(
            new InputSubmissionDto(
                run.RuntimeRunId,
                run.StartedAt));
    }

    private async Task CompleteRunAsync(
        ThreadRunState run,
        AgentRuntimeInputSubmission submission,
        IThreadEventPublisher publisher)
    {
        try
        {
            var outcome = await submission.Completion.ConfigureAwait(false);
            await CommitTerminalAsync(
                run,
                publisher,
                outcome.Cancelled,
                outcome.Error,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogCritical(
                ex,
                "Thread run {RuntimeRunId} could not commit its terminal lifecycle fact; ownership remains active.",
                run.RuntimeRunId);
        }
    }

    private async Task CommitTerminalAsync(
        ThreadRunState run,
        IThreadEventPublisher publisher,
        bool cancelled,
        Exception? error,
        CancellationToken cancellationToken)
    {
        await publisher.CommitAndPublishAsync(
            new ThreadKey(run.SessionId, run.ThreadId),
            new ThreadRunCompletedEvent(
                run.RuntimeRunId,
                run.AgentId,
                cancelled,
                error?.GetType().Name,
                error?.Message)
            {
                SessionId = run.SessionId,
                ThreadId = run.ThreadId
            },
            cancellationToken).ConfigureAwait(false);

        if (!_sessionManager.CompleteThreadRun(run.SessionId, run.ThreadId, run.RuntimeRunId))
        {
            throw new InvalidOperationException(
                $"Thread run '{run.RuntimeRunId}' committed completion but no longer owned its runtime slot.");
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

        var activeState = _sessionManager.GetActiveThreadRun(sessionId, threadId);
        ThreadRunDto? activeRun = activeState is not null && activeState.AgentId == agentId
            ? ToRunDto(activeState)
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
            activeRun,
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
        string? expectedRuntimeRunId,
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

        var activeRun = _sessionManager.GetActiveThreadRun(sessionId, threadId);
        if (activeRun == null)
        {
            var events = await _sessionManager.GetThreadRunProjectionEventsAsync(sessionId, threadId, cancellationToken)
                .ConfigureAwait(false);
            var expectedRun = string.IsNullOrWhiteSpace(expectedRuntimeRunId)
                ? null
                : ThreadRunProjector.Project(agentId, sessionId, threadId, events ?? [])
                    .LastOrDefault(run => run.RuntimeRunId == expectedRuntimeRunId);
            return AgentServiceResult<InterruptionSubmissionDto>.Success(new InterruptionSubmissionDto(
                expectedRun is null ? "no_active_run" : "already_terminal"));
        }

        if (!string.IsNullOrWhiteSpace(expectedRuntimeRunId) &&
            !string.Equals(expectedRuntimeRunId, activeRun.RuntimeRunId, StringComparison.Ordinal))
        {
            return AgentServiceResult<InterruptionSubmissionDto>.Success(new InterruptionSubmissionDto(
                "active_run_mismatch",
                ToRunDto(activeRun)));
        }

        var scoped = interruption with
        {
            AgentId = agentId,
            SessionId = sessionId,
            ThreadId = threadId,
            RuntimeRunId = activeRun.RuntimeRunId
        };

        await lease.Value!.RunAsync(scoped, cancellationToken).ConfigureAwait(false);
        return AgentServiceResult<InterruptionSubmissionDto>.Success(new InterruptionSubmissionDto(
            "accepted",
            ToRunDto(activeRun)));
    }

    public AgentInputEvent ApplyRouteScope(
        AgentInputEvent input,
        string agentId,
        string sessionId,
        string threadId,
        string? runtimeRunId = null)
    {
        return input switch
        {
            UserMessagesInputEvent messages => messages with
            {
                ClientInputId = messages.ClientInputId,
                AgentId = agentId,
                SessionId = sessionId,
                ThreadId = threadId,
                RuntimeRunId = runtimeRunId ?? messages.RuntimeRunId,
                RunConfig = messages.RunConfig
            },
            CompactThreadInputEvent compact => compact with
            {
                ClientInputId = compact.ClientInputId,
                AgentId = agentId,
                SessionId = sessionId,
                ThreadId = threadId,
                RuntimeRunId = runtimeRunId ?? compact.RuntimeRunId,
                RunConfig = compact.RunConfig
            },
            InterruptionRequestEvent interruption => interruption with
            {
                AgentId = agentId,
                SessionId = sessionId,
                ThreadId = threadId,
                RuntimeRunId = runtimeRunId ?? interruption.RuntimeRunId,
                RunConfig = interruption.RunConfig
            },
            BackgroundTaskNotificationInputEvent notification => notification with
            {
                ClientInputId = notification.ClientInputId,
                AgentId = agentId,
                SessionId = sessionId,
                ThreadId = threadId,
                RuntimeRunId = runtimeRunId ?? notification.RuntimeRunId,
                RunConfig = notification.RunConfig
            },
            _ => input
        };
    }

    private static ThreadRunDto ToRunDto(ThreadRunState run) => new(
        run.RuntimeRunId,
        run.AgentId,
        run.SessionId,
        run.ThreadId,
        ThreadRunStatus.Active,
        run.StartedAt,
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
