using HPD.Agent;
using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Lifecycle;

public sealed class AgentStreamingService : IAgentStreamingService
{
    private readonly SessionManager _sessionManager;
    private readonly AgentManager _agentManager;

    public AgentStreamingService(SessionManager sessionManager, AgentManager agentManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _agentManager = agentManager ?? throw new ArgumentNullException(nameof(agentManager));
    }

    public async Task<AgentServiceResult<AgentStreamLease>> GetAgentForThreadAsync(
        string agentId,
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        if (await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult<AgentStreamLease>.NotFound;

        if (await _sessionManager.Store.LoadThreadAsync(sessionId, threadId, cancellationToken) == null)
            return AgentServiceResult<AgentStreamLease>.NotFound;

        var agent = await _agentManager.GetOrBuildAgentRuntimeAsync(agentId, sessionId, threadId, cancellationToken);
        return AgentServiceResult<AgentStreamLease>.Success(new AgentStreamLease(agent));
    }

    public async Task<AgentServiceResult<InputSubmissionDto>> SubmitInputAsync(
        string agentId,
        string sessionId,
        string threadId,
        AgentInputEvent input,
        CancellationToken cancellationToken = default)
    {
        var lease = await GetAgentForThreadAsync(agentId, sessionId, threadId, cancellationToken)
            .ConfigureAwait(false);
        if (lease.Status != AgentServiceStatus.Success)
            return new AgentServiceResult<InputSubmissionDto>(lease.Status, default, lease.ErrorCode, lease.ErrorMessage, lease.ErrorMessages);

        if (!_sessionManager.TryStartThreadRun(agentId, sessionId, threadId, out var run))
        {
            return AgentServiceResult<InputSubmissionDto>.ConflictWith(
                "ThreadRunActive",
                $"Thread '{threadId}' in session '{sessionId}' already has an active run.");
        }

        var agent = lease.Value!.Agent;
        input = ApplyRouteScope(input, agentId, sessionId, threadId, run.RuntimeRunId);

        IDisposable? completionSubscription = null;
        completionSubscription = agent.SubscribeAny(evt =>
        {
            if (evt is ThreadRunCompletedEvent completed &&
                completed.SessionId == sessionId &&
                completed.ThreadId == threadId &&
                completed.RuntimeRunId == run.RuntimeRunId)
            {
                _sessionManager.CompleteThreadRun(sessionId, threadId, completed.RuntimeRunId);
                completionSubscription?.Dispose();
            }
        });

        try
        {
            await agent.StartAsync(input.RunConfig, CancellationToken.None).ConfigureAwait(false);
            await agent.RunAsync(input, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            completionSubscription.Dispose();
            _sessionManager.CompleteThreadRun(sessionId, threadId, run.RuntimeRunId);
            throw;
        }

        return AgentServiceResult<InputSubmissionDto>.Success(
            new InputSubmissionDto(
                run.RuntimeRunId,
                run.StartedAt));
    }

    public async Task<AgentServiceResult<ThreadRuntimeStateDto>> GetThreadStateAsync(
        string agentId,
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var document = await _sessionManager.Store.LoadThreadDocumentAsync(sessionId, threadId, cancellationToken)
            .ConfigureAwait(false);
        if (document is null && await _sessionManager.Store.LoadThreadAsync(sessionId, threadId, cancellationToken)
                .ConfigureAwait(false) is null)
        {
            return AgentServiceResult<ThreadRuntimeStateDto>.NotFound;
        }

        var events = document?.Events.OrderBy(static evt => evt.SequenceNumber).ToList() ?? [];
        var activeState = _sessionManager.GetActiveThreadRun(sessionId, threadId);
        ThreadRunDto? activeRun = activeState is not null && activeState.AgentId == agentId
            ? ToRunDto(activeState)
            : null;

        return AgentServiceResult<ThreadRuntimeStateDto>.Success(new ThreadRuntimeStateDto(
            (document?.NextSequenceNumber ?? 1) - 1,
            activeRun,
            events));
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

        var thread = await _sessionManager.Store.LoadThreadAsync(sessionId, threadId, cancellationToken)
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
        var lease = await GetAgentForThreadAsync(agentId, sessionId, threadId, cancellationToken)
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
            var document = await _sessionManager.Store.LoadThreadDocumentAsync(sessionId, threadId, cancellationToken)
                .ConfigureAwait(false);
            var expectedRun = string.IsNullOrWhiteSpace(expectedRuntimeRunId)
                ? null
                : ThreadRunProjector.Project(agentId, sessionId, threadId, document?.Events ?? [])
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

        await lease.Value!.Agent.RunAsync(scoped, cancellationToken).ConfigureAwait(false);
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

}
