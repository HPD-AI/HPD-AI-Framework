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

        var startedEvent = new ThreadRunStartedEvent(run.RuntimeRunId, agentId, run.StartedAt)
        {
            SessionId = sessionId,
            ThreadId = threadId
        };

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
            await _sessionManager.Store.AppendThreadEventAsync(
                sessionId,
                threadId,
                startedEvent,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await agent.StartAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
            await agent.RunAsync(input, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            completionSubscription.Dispose();
            _sessionManager.CompleteThreadRun(sessionId, threadId, run.RuntimeRunId);
            var completedEvent = new ThreadRunCompletedEvent(
                run.RuntimeRunId,
                agentId,
                ex is OperationCanceledException,
                ex.GetType().Name,
                ex.Message)
            {
                SessionId = sessionId,
                ThreadId = threadId
            };

            try
            {
                await _sessionManager.Store.AppendThreadEventAsync(
                    sessionId,
                    threadId,
                    completedEvent,
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Preserve the original submission failure.
            }

            throw;
        }

        return AgentServiceResult<InputSubmissionDto>.Success(
            new InputSubmissionDto(run.RuntimeRunId));
    }

    public async Task<AgentServiceResult> InterruptAsync(
        string agentId,
        string sessionId,
        string threadId,
        InterruptionRequestEvent interruption,
        CancellationToken cancellationToken = default)
    {
        var lease = await GetAgentForThreadAsync(agentId, sessionId, threadId, cancellationToken)
            .ConfigureAwait(false);
        if (lease.Status != AgentServiceStatus.Success)
            return new AgentServiceResult(lease.Status, lease.ErrorCode, lease.ErrorMessage, lease.ErrorMessages);

        var activeRun = _sessionManager.GetActiveThreadRun(sessionId, threadId);
        if (activeRun == null)
        {
            return AgentServiceResult.ConflictWith(
                "ThreadRunNotActive",
                $"Thread '{threadId}' in session '{sessionId}' does not have an active run.");
        }

        var scoped = interruption with
        {
            AgentId = agentId,
            SessionId = sessionId,
            ThreadId = threadId,
            RuntimeRunId = activeRun.RuntimeRunId
        };

        await lease.Value!.Agent.RunAsync(scoped, cancellationToken).ConfigureAwait(false);
        return AgentServiceResult.Success;
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
                RuntimeRunId = runtimeRunId ?? messages.RuntimeRunId
            },
            InterruptionRequestEvent interruption => interruption with
            {
                AgentId = agentId,
                SessionId = sessionId,
                ThreadId = threadId,
                RuntimeRunId = runtimeRunId ?? interruption.RuntimeRunId
            },
            _ => input
        };
    }
}
