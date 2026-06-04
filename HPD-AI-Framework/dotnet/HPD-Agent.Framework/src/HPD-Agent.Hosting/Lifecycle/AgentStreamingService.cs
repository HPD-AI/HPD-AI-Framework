using HPD.Agent;

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

    public async Task<AgentServiceResult<AgentStreamLease>> GetAgentForBranchAsync(
        string agentId,
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        if (await _sessionManager.Repository.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult<AgentStreamLease>.NotFound;

        if (await _sessionManager.Repository.LoadBranchAsync(sessionId, branchId, cancellationToken) == null)
            return AgentServiceResult<AgentStreamLease>.NotFound;

        var agent = await _agentManager.GetOrBuildAgentRuntimeAsync(agentId, sessionId, branchId, cancellationToken);
        return AgentServiceResult<AgentStreamLease>.Success(new AgentStreamLease(agent));
    }

    public async Task<AgentServiceResult> SubmitInputAsync(
        string agentId,
        string sessionId,
        string branchId,
        AgentInputEvent input,
        CancellationToken cancellationToken = default)
    {
        var lease = await GetAgentForBranchAsync(agentId, sessionId, branchId, cancellationToken)
            .ConfigureAwait(false);
        if (lease.Status != AgentServiceStatus.Success)
            return new AgentServiceResult(lease.Status, lease.ErrorCode, lease.ErrorMessage, lease.ErrorMessages);

        if (!_sessionManager.TryStartBranchRun(agentId, sessionId, branchId, out var run))
        {
            return AgentServiceResult.ConflictWith(
                "BranchRunActive",
                $"Branch '{branchId}' in session '{sessionId}' already has an active run.");
        }

        var agent = lease.Value!.Agent;
        input = ApplyRouteScope(input, agentId, sessionId, branchId, run.RuntimeRunId);
        var startedEvent = new BranchRunStartedEvent(run.RuntimeRunId, agentId, run.StartedAt)
        {
            SessionId = sessionId,
            BranchId = branchId
        };

        IDisposable? completionSubscription = null;
        completionSubscription = agent.SubscribeAny(evt =>
        {
            if (evt is BranchRunCompletedEvent completed &&
                completed.SessionId == sessionId &&
                completed.BranchId == branchId &&
                completed.RuntimeRunId == run.RuntimeRunId)
            {
                _sessionManager.CompleteBranchRun(sessionId, branchId, completed.RuntimeRunId);
                completionSubscription?.Dispose();
            }
        });

        try
        {
            await _sessionManager.Repository.AppendBranchEventAsync(
                sessionId,
                branchId,
                startedEvent,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await agent.StartAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
            await agent.RunAsync(input, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            completionSubscription.Dispose();
            _sessionManager.CompleteBranchRun(sessionId, branchId, run.RuntimeRunId);
            var completedEvent = new BranchRunCompletedEvent(
                run.RuntimeRunId,
                agentId,
                ex is OperationCanceledException,
                ex.GetType().Name,
                ex.Message)
            {
                SessionId = sessionId,
                BranchId = branchId
            };

            try
            {
                await _sessionManager.Repository.AppendBranchEventAsync(
                    sessionId,
                    branchId,
                    completedEvent,
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Preserve the original submission failure.
            }

            throw;
        }

        return AgentServiceResult.Success;
    }

    public async Task<AgentServiceResult> InterruptAsync(
        string agentId,
        string sessionId,
        string branchId,
        InterruptionRequestEvent interruption,
        CancellationToken cancellationToken = default)
    {
        var lease = await GetAgentForBranchAsync(agentId, sessionId, branchId, cancellationToken)
            .ConfigureAwait(false);
        if (lease.Status != AgentServiceStatus.Success)
            return new AgentServiceResult(lease.Status, lease.ErrorCode, lease.ErrorMessage, lease.ErrorMessages);

        var activeRun = _sessionManager.GetActiveBranchRun(sessionId, branchId);
        if (activeRun == null)
        {
            return AgentServiceResult.ConflictWith(
                "BranchRunNotActive",
                $"Branch '{branchId}' in session '{sessionId}' does not have an active run.");
        }

        var scoped = interruption with
        {
            AgentId = agentId,
            SessionId = sessionId,
            BranchId = branchId,
            RuntimeRunId = activeRun.RuntimeRunId
        };

        await lease.Value!.Agent.RunAsync(scoped, cancellationToken).ConfigureAwait(false);
        return AgentServiceResult.Success;
    }

    public AgentInputEvent ApplyRouteScope(
        AgentInputEvent input,
        string agentId,
        string sessionId,
        string branchId,
        string? runtimeRunId = null)
    {
        return input switch
        {
            UserTextInputEvent text => text with
            {
                AgentId = agentId,
                SessionId = sessionId,
                BranchId = branchId,
                RuntimeRunId = runtimeRunId ?? text.RuntimeRunId
            },
            UserMessagesInputEvent messages => messages with
            {
                AgentId = agentId,
                SessionId = sessionId,
                BranchId = branchId,
                RuntimeRunId = runtimeRunId ?? messages.RuntimeRunId
            },
            InterruptionRequestEvent interruption => interruption with
            {
                AgentId = agentId,
                SessionId = sessionId,
                BranchId = branchId,
                RuntimeRunId = runtimeRunId ?? interruption.RuntimeRunId
            },
            _ => input
        };
    }
}
