using HPD.Agent;
using HPD.Agent.Hosting.Data;
using Microsoft.Extensions.Logging;
using HPD.Events.Struct;

namespace HPD.Agent.Hosting.Lifecycle;

public sealed class AgentStreamingService : IAgentStreamingService
{
    private readonly SessionManager _sessionManager;
    private readonly AgentManager _agentManager;
    private readonly ILogger<AgentStreamingService>? _logger;
    private readonly IAgentEventContentArchiver _eventContentArchiver;

    public AgentStreamingService(
        SessionManager sessionManager,
        AgentManager agentManager,
        ILogger<AgentStreamingService>? logger = null,
        IContentStore? contentStore = null,
        IStructEventHub? structEvents = null)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _agentManager = agentManager ?? throw new ArgumentNullException(nameof(agentManager));
        _logger = logger;
        _eventContentArchiver = new AgentEventContentArchiver(contentStore, diagnostic =>
        {
            if (structEvents is not null)
                structEvents.Route<AgentEventArchiveDiagnostic>().CreateEmitter().Emit(in diagnostic);
            _logger?.LogWarning(
                diagnostic.Exception,
                "Agent event content archival skipped or failed for {EventType}: {Reason}",
                diagnostic.EventType.Name,
                diagnostic.Reason);
        });
    }

    /// <inheritdoc />
    public async Task<AgentServiceResult<ThreadEventObservationLease>> ObserveThreadEventsAsync(
        string agentId,
        ThreadKey anchor,
        AgentEventHierarchy hierarchy = AgentEventHierarchy.ExactThread,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(anchor.SessionId) || string.IsNullOrWhiteSpace(anchor.ThreadId))
            return AgentServiceResult<ThreadEventObservationLease>.Validation(
                "InvalidThreadKey",
                "A complete non-empty session/thread key is required.");
        if (hierarchy is < AgentEventHierarchy.ExactThread or > AgentEventHierarchy.ThreadAndDescendants)
            return AgentServiceResult<ThreadEventObservationLease>.Validation(
                "InvalidEventHierarchy",
                $"Unknown agent event hierarchy value '{(int)hierarchy}'.");

        if (await _sessionManager.Store.LoadSessionAsync(anchor.SessionId, cancellationToken) == null)
            return AgentServiceResult<ThreadEventObservationLease>.NotFound;

        if (await _sessionManager.Store.GetThreadAsync(anchor, cancellationToken).ConfigureAwait(false) == null)
            return AgentServiceResult<ThreadEventObservationLease>.NotFound;

        var liveEvents = _agentManager.CreateRuntimeEventInbox(
            agentId,
            anchor.SessionId,
            anchor.ThreadId,
            hierarchy,
            HPD.Events.EventInboxOptions.Deterministic());
        return AgentServiceResult<ThreadEventObservationLease>.Success(
            new ThreadEventObservationLease(_sessionManager.Store, anchor, hierarchy, liveEvents));
    }

    public async Task<AgentServiceResult<InputSubmissionDto>> SubmitInputAsync(
        string agentId,
        string sessionId,
        string threadId,
        AgentInputEvent input,
        CancellationToken cancellationToken = default)
    {
        if (input is UserMessagesInputEvent { Delivery: AgentInputDelivery.Steer })
            return await SubmitActiveControlAsync(agentId, sessionId, threadId, input, cancellationToken)
                .ConfigureAwait(false);

        var registration = AgentInputDispatcher.GetBuiltInRegistration(input.GetType());
        if (registration.RoutingClass == AgentInputRoutingClass.ActiveControl)
            return await SubmitActiveControlAsync(agentId, sessionId, threadId, input, cancellationToken)
                .ConfigureAwait(false);
        if (registration.RoutingClass == AgentInputRoutingClass.SessionControl)
            return await SubmitSessionControlAsync(agentId, sessionId, threadId, input, cancellationToken)
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

        var publisher = new AgentEventPublisher(
            _sessionManager.Store,
            agent.EventCoordinator,
            _eventContentArchiver);
        execution.Reservation.BindPromotion(
            async ct =>
            {
                await publisher.CommitAndPublishAsync(
                    new ThreadKey(sessionId, threadId),
                    new ThreadExecutionStartedEvent(execution.ThreadExecutionId, agentId, DateTimeOffset.UtcNow)
                    {
                        SessionId = sessionId,
                        ThreadId = threadId
                    },
                    ct).ConfigureAwait(false);
                if (!_sessionManager.ActivateThreadExecution(sessionId, threadId, execution.ThreadExecutionId))
                    throw new InvalidOperationException($"Thread execution '{execution.ThreadExecutionId}' lost its reserved ownership before promotion.");
            },
            async (outcome, error, ct) => await CommitTerminalAsync(
                execution,
                publisher,
                outcome == ThreadExecutionOutcome.Cancelled,
                outcome == ThreadExecutionOutcome.Failed ? error : null,
                ct).ConfigureAwait(false));
        input = ApplyRouteScope(input, agentId, sessionId, threadId, execution.ThreadExecutionId);
        input = agent.AuthorizeCoordinatorAssignedWork(input, execution.Reservation);

        try
        {
            await agent.StartAsync(input.RunConfig, CancellationToken.None).ConfigureAwait(false);
            var submission = await agent.SubmitRuntimeInputAsync(input, CancellationToken.None).ConfigureAwait(false);
            _ = ObserveExecutionAsync(execution, submission, runtimePin);
        }
        catch
        {
            _sessionManager.ReleaseThreadExecution(sessionId, threadId, execution.ThreadExecutionId);
            runtimePin.Dispose();
            throw;
        }

        return AgentServiceResult<InputSubmissionDto>.Success(
            new InputSubmissionDto(
                "queued",
                execution.ThreadExecutionId,
                execution.StartedAt));
    }

    /// <inheritdoc />
    public async Task<AgentServiceResult<InputSubmissionDto>> SubmitSubAgentInputAsync(
        string controllerAgentId,
        string controllerSessionId,
        string controllerThreadId,
        SubAgentLocalId localId,
        string childAgentId,
        string childSessionId,
        string childThreadId,
        AgentInputEvent input,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(controllerAgentId) ||
            string.IsNullOrWhiteSpace(childAgentId) ||
            string.IsNullOrWhiteSpace(controllerSessionId) ||
            string.IsNullOrWhiteSpace(controllerThreadId) ||
            string.IsNullOrWhiteSpace(childSessionId) ||
            string.IsNullOrWhiteSpace(childThreadId))
            return AgentServiceResult<InputSubmissionDto>.Validation(
                "SubAgentRouteRequired", "A complete controller and child route is required.");

        try
        {
            var controllerThread = new ThreadKey(controllerSessionId, controllerThreadId);
            var controllerDescriptor = await _sessionManager.Store
                .GetThreadAsync(controllerThread, cancellationToken)
                .ConfigureAwait(false);
            if (controllerDescriptor is null || !string.Equals(
                    controllerDescriptor.DefaultAgent.AgentId,
                    controllerAgentId,
                    StringComparison.Ordinal))
                return AgentServiceResult<InputSubmissionDto>.Validation(
                    "subagent_controller_route_mismatch",
                    "The claimed controller agent does not own the controller thread.");

            var submission = await SubAgentRuntime.SubmitControlledInputAsync(
                _sessionManager.Store,
                new HostedAgentRuntimeResolver(_agentManager),
                controllerThread,
                localId,
                childAgentId,
                new ThreadKey(childSessionId, childThreadId),
                input,
                cancellationToken).ConfigureAwait(false);
            return AgentServiceResult<InputSubmissionDto>.Success(new InputSubmissionDto(
                ToWireDisposition(submission.Disposition),
                submission.ThreadExecutionId,
                DateTimeOffset.UtcNow));
        }
        catch (InvalidOperationException exception) when (exception.Message is
            "subagent_unknown" or "subagent_route_mismatch" or "subagent_controller_grant_required" or
            "subagent_locked_client_override_forbidden")
        {
            return AgentServiceResult<InputSubmissionDto>.Validation(exception.Message, exception.Message);
        }
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
        var (disposition, executionId) = result switch
        {
            AgentInputResult.Control control => (control.Disposition, control.ThreadExecutionId),
            AgentInputResult.Steered steered => (AgentInputDisposition.Accepted, steered.ThreadExecutionId),
            AgentInputResult.Completed completed => (AgentInputDisposition.Completed, completed.ThreadExecutionId),
            _ => throw new InvalidOperationException($"Unsupported input result '{result.GetType().Name}'.")
        };
        return AgentServiceResult<InputSubmissionDto>.Success(new InputSubmissionDto(
            ToWireDisposition(disposition),
            executionId ?? activeExecution.ThreadExecutionId,
            ActiveExecution: ToExecutionDto(activeExecution)));
    }

    private async Task<AgentServiceResult<InputSubmissionDto>> SubmitSessionControlAsync(
        string agentId,
        string sessionId,
        string threadId,
        AgentInputEvent input,
        CancellationToken cancellationToken)
    {
        var lease = await GetAgentForExecutionAsync(agentId, sessionId, threadId, cancellationToken)
            .ConfigureAwait(false);
        if (lease.Status != AgentServiceStatus.Success)
            return new AgentServiceResult<InputSubmissionDto>(
                lease.Status, default, lease.ErrorCode, lease.ErrorMessage, lease.ErrorMessages);

        var runtimePin = _agentManager.PinRuntime(agentId, sessionId, threadId);
        try
        {
            var agent = lease.Value!;
            var scoped = ApplyRouteScope(input, agentId, sessionId, threadId);
            await agent.StartAsync(scoped.RunConfig, CancellationToken.None).ConfigureAwait(false);
            var result = await agent.RunAsync(scoped, cancellationToken).ConfigureAwait(false);
            return AgentServiceResult<InputSubmissionDto>.Success(new InputSubmissionDto(
                "completed",
                Result: result));
        }
        finally
        {
            runtimePin.Dispose();
        }
    }

    private async Task ObserveExecutionAsync(
        ThreadExecutionState execution,
        RuntimeInputReceipt submission,
        IDisposable runtimePin)
    {
        try
        {
            _ = await submission.Completion.ConfigureAwait(false);
            runtimePin.Dispose();
            submission.Dispose();
        }
        catch (Exception ex)
        {
            submission.Dispose();
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
        IAgentEventPublisher publisher,
        bool cancelled,
        Exception? error,
        CancellationToken cancellationToken)
    {
        var key = new ThreadKey(execution.SessionId, execution.ThreadId);
        var journal = await _sessionManager.Store.CollectThreadEventsAsync(key, cancellationToken)
            .ConfigureAwait(false) ?? [];
        var terminalEvents = AgentRequestProjector
            .ProjectPending(journal, execution.ThreadExecutionId)
            .OfType<IAgentRequestEvent>()
            .Select(request => (AgentEvent)new AgentRequestTerminatedEvent(
                request.RequestId,
                request.SourceName,
                AgentRequestTerminalKind.Abandoned,
                "The owning thread execution finished before the request received a response.",
                DateTimeOffset.UtcNow)
            {
                SessionId = execution.SessionId,
                ThreadId = execution.ThreadId,
                ThreadExecutionId = execution.ThreadExecutionId
            })
            .ToList();
        terminalEvents.Add(new ThreadExecutionFinishedEvent(
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
            });

        await publisher.CommitAndPublishAsync(
            key,
            terminalEvents,
            cancellationToken: cancellationToken).ConfigureAwait(false);

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
        var key = new ThreadKey(sessionId, threadId);
        var journal = await _sessionManager.Store.CollectThreadEventsAsync(key, cancellationToken)
            .ConfigureAwait(false);
        if (journal is null)
            return AgentServiceResult<ThreadRuntimeStateDto>.NotFound;

        var activeState = _sessionManager.GetActiveThreadExecution(sessionId, threadId);
        journal = await ReconcileInterruptedExecutionsAsync(
            agentId,
            key,
            journal,
            activeState?.ThreadExecutionId,
            cancellationToken).ConfigureAwait(false);
        var head = await _sessionManager.Store.GetThreadEventHeadAsync(key, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Thread '{sessionId}/{threadId}' lost its journal during state reconciliation.");
        ThreadExecutionDto? activeExecution = activeState is not null && activeState.AgentId == agentId
            ? ToExecutionDto(activeState)
            : null;

        var pendingRequests = AgentRequestProjector
            .ProjectPending(journal, activeExecution?.ThreadExecutionId)
            .Select(item => new PendingAgentRequestDto(item, item.Timestamp))
            .ToArray();

        return AgentServiceResult<ThreadRuntimeStateDto>.Success(new ThreadRuntimeStateDto(
            head.Cursor,
            activeExecution,
            pendingRequests));
    }

    private async ValueTask<IReadOnlyList<AgentEvent>> ReconcileInterruptedExecutionsAsync(
        string agentId,
        ThreadKey thread,
        IReadOnlyList<AgentEvent> journal,
        string? activeThreadExecutionId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var head = await _sessionManager.Store.GetThreadEventHeadAsync(thread, cancellationToken)
                .ConfigureAwait(false);
            if (head is null)
                return journal;

            journal = await _sessionManager.Store.CollectThreadEventsAsync(thread, cancellationToken)
                .ConfigureAwait(false) ?? journal;
            var verifiedHead = await _sessionManager.Store.GetThreadEventHeadAsync(thread, cancellationToken)
                .ConfigureAwait(false);
            if (verifiedHead?.Cursor != head.Cursor)
                continue;

            var interrupted = ThreadExecutionProjector.Project(
                    agentId,
                    thread.SessionId,
                    thread.ThreadId,
                    journal,
                    activeThreadExecutionId)
                .Where(item => item.Status == ThreadExecutionStatus.Interrupted)
                .ToArray();
            if (interrupted.Length == 0)
                return journal;

            var terminalEvents = new List<AgentEvent>();
            foreach (var execution in interrupted)
            {
                terminalEvents.AddRange(AgentRequestProjector
                    .ProjectPending(journal, execution.ThreadExecutionId)
                    .OfType<IAgentRequestEvent>()
                    .Select(request => (AgentEvent)new AgentRequestTerminatedEvent(
                        request.RequestId,
                        request.SourceName,
                        AgentRequestTerminalKind.Abandoned,
                        "The host recovered an interrupted thread execution.",
                        DateTimeOffset.UtcNow)
                    {
                        SessionId = thread.SessionId,
                        ThreadId = thread.ThreadId,
                        ThreadExecutionId = execution.ThreadExecutionId
                    }));
                terminalEvents.Add(new ThreadExecutionFinishedEvent(
                    execution.ThreadExecutionId,
                    execution.AgentId,
                    ThreadExecutionOutcome.Failed,
                    DateTimeOffset.UtcNow,
                    new ThreadExecutionError(
                        "HostExecutionLost",
                        "The host recovered an execution that had no live runtime owner."))
                {
                    SessionId = thread.SessionId,
                    ThreadId = thread.ThreadId
                });
            }

            var runtimeAgent = _agentManager.GetRuntimeAgent(
                agentId, thread.SessionId, thread.ThreadId);
            var coordinator = runtimeAgent?.EventCoordinator ??
                new global::HPD.Events.Core.EventCoordinator();
            var ownsCoordinator = runtimeAgent is null;
            try
            {
                await new AgentEventPublisher(_sessionManager.Store, coordinator, _eventContentArchiver)
                    .CommitAndPublishAsync(
                        thread,
                        terminalEvents,
                        new ThreadAppendCondition(head.Cursor),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ThreadAppendConflictException)
            {
                continue;
            }
            finally
            {
                if (ownsCoordinator)
                    ((IDisposable)coordinator).Dispose();
            }

            return await _sessionManager.Store.CollectThreadEventsAsync(thread, cancellationToken)
                .ConfigureAwait(false) ?? journal;
        }
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
