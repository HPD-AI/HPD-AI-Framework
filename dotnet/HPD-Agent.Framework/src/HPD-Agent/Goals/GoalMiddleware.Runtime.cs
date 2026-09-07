using System.Text.Json;

namespace HPD.Agent.Goals;

internal sealed partial class GoalMiddleware
{
    internal async ValueTask ReconcileAsync(ISessionStore store, IAgentEventPublisher publisher, Thread thread,
        AgentRunConfig? runConfig, Func<GoalContinuationInputEvent, ValueTask<bool>> admit)
    {
        var key = new ThreadKey(thread.SessionId, thread.Id);
        if ((await ThreadExecutionControllerRegistry.For(store).FindActiveAsync(key).ConfigureAwait(false)).IsActive) return;
        await RecoverPendingAsync(store, publisher, thread, runConfig, null).ConfigureAwait(false);
        while (true)
        {
            var snapshot = await GoalPersistence.ReadAsync(store, key, CancellationToken.None).ConfigureAwait(false);
            if (snapshot.Goal.PendingExecution is not null || snapshot.Goal.Current is not { Status: GoalStatus.Active } goal) return;
            if (goal.Continuation is null)
            {
                var sourceExecution = goal.Accounting.LastAccountedExecutionId ?? "runtime_restore";
                var facts = new GoalPolicyContext(goal, sourceExecution, MessageTurnUsageSummary.Empty,
                    false, false, true, false, goal.ConsecutiveNoProgressExecutions,
                    _config.RequiredConsecutiveBlockerExecutions, _config.MaximumConsecutiveNoProgressExecutions) { IsRecovery = true };
                var decision = await _policies.Resolve(runConfig?.Goals).Continuation.EvaluateAsync(facts, CancellationToken.None).ConfigureAwait(false);
                if (decision.Disposition != GoalPolicyDisposition.Continue)
                {
                    if (decision.Disposition == GoalPolicyDisposition.Rejected) return;
                    if (decision.Disposition is not (GoalPolicyDisposition.Paused or GoalPolicyDisposition.AwaitingInput))
                        throw new InvalidOperationException("goal_continuation_policy_invalid");
                    var suspended = GoalTransitions.ChangeStatus(goal,
                        decision.Disposition == GoalPolicyDisposition.Paused ? GoalStatus.Paused : GoalStatus.AwaitingInput,
                        DateTimeOffset.UtcNow);
                    var suspendedState = snapshot.Goal with { Current = suspended };
                    AgentEvent suspendedEvent = suspended.Status == GoalStatus.Paused
                        ? new GoalPausedEvent(suspended, decision.Reason) : new GoalAwaitingInputEvent(suspended, decision.Reason);
                    try
                    {
                        await GoalPersistence.CommitAsync(publisher, key, snapshot, suspendedState, suspendedEvent, CancellationToken.None).ConfigureAwait(false);
                        thread.MiddlewareState[GoalPersistence.StateKey] = JsonSerializer.Serialize(suspendedState, GoalJsonContext.Default.GoalPersistentState);
                        return;
                    }
                    catch (ThreadAppendConflictException) { continue; }
                }
                var reserved = GoalTransitions.Reserve(goal, sourceExecution, DateTimeOffset.UtcNow);
                var state = snapshot.Goal with { Current = reserved };
                try
                {
                    await GoalPersistence.CommitAsync(publisher, key, snapshot, state,
                        new GoalContinuationScheduledEvent(reserved, "runtime_restored"), CancellationToken.None).ConfigureAwait(false);
                    goal = reserved;
                }
                catch (ThreadAppendConflictException) { continue; }
            }
            await ActivateAndAdmitAsync(store, publisher, thread, goal, runConfig, null, null, admit).ConfigureAwait(false);
            return;
        }
    }

    private async ValueTask ActivateAndAdmitAsync(ISessionStore store, IAgentEventPublisher publisher,
        Thread thread, GoalData reserved, AgentRunConfig? runConfig, SubAgentRunConfig? subAgentRunConfig,
        string? agentId, Func<GoalContinuationInputEvent, ValueTask<bool>> admit)
    {
        var key = new ThreadKey(thread.SessionId, thread.Id);
        var owner = _activationOwner;
        while (true)
        {
            var snapshot = await GoalPersistence.ReadAsync(store, key, CancellationToken.None).ConfigureAwait(false);
            if (snapshot.Goal.Current is not { Status: GoalStatus.Active, Continuation: { } reservation } goal ||
                goal.GoalId != reserved.GoalId || reservation.Generation != reserved.Continuation!.Generation ||
                reservation.ActivationOwner == owner) return;
            var activated = GoalTransitions.Activate(goal, owner, DateTimeOffset.UtcNow);
            var state = snapshot.Goal with { Current = activated };
            try
            {
                await GoalPersistence.CommitAsync(publisher, key, snapshot, state,
                    new GoalUpdatedEvent(activated, "continuation_activated"), CancellationToken.None).ConfigureAwait(false);
            }
            catch (ThreadAppendConflictException) { continue; }
            catch
            {
                // A live publication error may follow a successful atomic append.
                // Release that exact claim before surfacing the failure.
                await WithdrawReservationAsync(store, publisher, thread, activated).ConfigureAwait(false);
                throw;
            }
            thread.MiddlewareState[GoalPersistence.StateKey] = JsonSerializer.Serialize(state, GoalJsonContext.Default.GoalPersistentState);
            bool admitted;
            try
            {
                admitted = await admit(new()
            {
                GoalId = activated.GoalId, ExpectedRevision = activated.Revision,
                Generation = activated.Continuation!.Generation,
                SessionId = key.SessionId, ThreadId = key.ThreadId, AgentId = agentId,
                RunConfig = runConfig, SubAgentRunConfig = subAgentRunConfig
            }).ConfigureAwait(false);
            }
            catch
            {
                await WithdrawReservationAsync(store, publisher, thread, activated).ConfigureAwait(false);
                throw;
            }
            if (!admitted) await WithdrawReservationAsync(store, publisher, thread, activated).ConfigureAwait(false);
            return;
        }
    }

    internal async ValueTask CloseExecutionAsync(ISessionStore store, IAgentEventPublisher publisher,
        Thread thread, AgentInputEvent source, bool succeeded, bool cancelled, bool stopping, bool running,
        AgentRunConfig? runConfig, Func<GoalContinuationInputEvent, ValueTask<bool>> admit,
        string continuationUnavailableReason = "runtime_not_started")
    {
        var key = new ThreadKey(thread.SessionId, thread.Id);
        while (true)
        {
            var snapshot = await GoalPersistence.ReadAsync(store, key, CancellationToken.None).ConfigureAwait(false);
            if (snapshot.Goal.PendingExecution is not { } pending || pending.ExecutionId != source.ThreadExecutionId) return;
            AgentEvent? terminal = null;
            var requestEvents = new List<AgentEvent>();
            await foreach (var batch in store.ReadThreadEventsAsync(key,
                new(ThreadJournalCursor.Start(snapshot.Cursor.Generation), snapshot.Cursor.SequenceNumber), CancellationToken.None).ConfigureAwait(false))
            {
                foreach (var evt in batch.Events)
                {
                    if (evt is IAgentRequestEvent or IAgentResponseEvent or AgentRequestTerminatedEvent) requestEvents.Add(evt);
                    if (evt is MessageTurnFinishedEvent finished && finished.MessageTurnId == pending.MessageTurnId ||
                        evt is MessageTurnErrorEvent error && error.MessageTurnId == pending.MessageTurnId)
                        terminal = evt;
                }
            }
            if (terminal is null) return; // An interrupted pre-terminal turn remains recoverable.
            var usage = terminal is MessageTurnFinishedEvent success ? success.Usage : ((MessageTurnErrorEvent)terminal).Usage;
            var elapsed = terminal is MessageTurnFinishedEvent timed ? timed.Duration : terminal.Timestamp - pending.StartedAt;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            var cancellation = (terminal as MessageTurnErrorEvent)?.Cancellation;
            stopping |= cancellation?.Cause == AgentInputCancellationCause.RuntimeShutdown;
            cancelled |= cancellation?.Cause is AgentInputCancellationCause.Caller or AgentInputCancellationCause.Explicit or AgentInputCancellationCause.Middleware;
            var closure = stopping ? GoalExecutionClosure.RuntimeStopped
                : cancelled ? GoalExecutionClosure.Cancelled
                : terminal is MessageTurnErrorEvent { Category: ErrorHandling.ErrorCategory.RateLimitTerminal } ? GoalExecutionClosure.UsageLimited
                : cancellation?.Cause == AgentInputCancellationCause.Unknown ? GoalExecutionClosure.Interrupted
                : succeeded && terminal is MessageTurnFinishedEvent ? GoalExecutionClosure.Succeeded : GoalExecutionClosure.Failed;
            var policies = _policies.Resolve(runConfig?.Goals);
            if (terminal is MessageTurnErrorEvent { ErrorType: "runtime_restart_recovery" })
                policies = policies with { Accounting = new RecoveryAccountingPolicy(policies.Accounting) };
            var facts = new GoalPolicyContext(snapshot.Goal.Current ?? pending.GoalSnapshot, pending.ExecutionId,
                usage, pending.HasIncompletePlan, pending.HasProgress, running,
                AgentRequestProjector.ProjectPending(requestEvents, pending.ExecutionId).Count != 0, 0,
                _config.RequiredConsecutiveBlockerExecutions, _config.MaximumConsecutiveNoProgressExecutions)
                { ContinuationUnavailableReason = continuationUnavailableReason };
            var result = await GoalTerminalFinalizer.FinalizeAsync(snapshot.Goal, pending.ExecutionId,
                pending.MessageTurnId, new(snapshot.Cursor.Generation, terminal.ThreadSequenceNumber), usage, elapsed,
                closure, facts, policies, terminal.Timestamp, CancellationToken.None).ConfigureAwait(false);
            if (result.Events.Count == 0) return;
            try
            {
                await GoalPersistence.CommitAsync(publisher, key, snapshot, result.State,
                    result.Events.Select(evt => evt is GoalPausedEvent paused
                        ? paused with { ThreadExecutionId = pending.ExecutionId, Cancellation = cancellation }
                        : evt with { ThreadExecutionId = pending.ExecutionId }).ToArray(),
                    CancellationToken.None).ConfigureAwait(false);
                thread.MiddlewareState[GoalPersistence.StateKey] = JsonSerializer.Serialize(result.State, GoalJsonContext.Default.GoalPersistentState);
            }
            catch (ThreadAppendConflictException) { continue; }
            if (result.State.Current is { Continuation: not null } goal)
                await ActivateAndAdmitAsync(store, publisher, thread, goal, runConfig, source.SubAgentRunConfig, source.AgentId, admit).ConfigureAwait(false);
            return;
        }
    }

    private sealed class RecoveryAccountingPolicy(IGoalAccountingPolicy inner) : IGoalAccountingPolicy
    {
        public GoalUsageProjection Project(MessageTurnUsageSummary usage)
        {
            var projection = inner.Project(usage);
            return projection.Quality == GoalUsageQuality.Exact ? projection with { Quality = GoalUsageQuality.Partial } : projection;
        }
    }

    private async ValueTask RecoverPendingAsync(ISessionStore store, IAgentEventPublisher publisher,
        Thread thread, AgentRunConfig? runConfig, string? currentExecutionId)
    {
        var key = new ThreadKey(thread.SessionId, thread.Id);
        while (true)
        {
            var snapshot = await GoalPersistence.ReadAsync(store, key, CancellationToken.None).ConfigureAwait(false);
            if (snapshot.Goal.PendingExecution is not { } pending || pending.ExecutionId == currentExecutionId) return;
            var active = await ThreadExecutionControllerRegistry.For(store).FindActiveAsync(key).ConfigureAwait(false);
            if (active.IsActive && active.ThreadExecutionId != currentExecutionId) return;
            AgentEvent? terminal = null;
            var measurements = new List<ProviderUsageMeasurement>();
            var lastObservedAt = pending.StartedAt;
            await foreach (var batch in store.ReadThreadEventsAsync(key,
                new(ThreadJournalCursor.Start(snapshot.Cursor.Generation), snapshot.Cursor.SequenceNumber)).ConfigureAwait(false))
            {
                foreach (var evt in batch.Events)
                {
                    if (evt.ThreadExecutionId == pending.ExecutionId && evt.Timestamp > lastObservedAt) lastObservedAt = evt.Timestamp;
                    if (evt is MessageTurnFinishedEvent finished && finished.MessageTurnId == pending.MessageTurnId ||
                        evt is MessageTurnErrorEvent error && error.MessageTurnId == pending.MessageTurnId) terminal = evt;
                    if (Agent.TryCreateProviderUsageMeasurement(evt, out var measurement) && measurement.MessageTurnId == pending.MessageTurnId)
                        measurements.Add(measurement);
                }
            }
            var interrupted = terminal is null;
            if (interrupted)
            {
                var recovered = new MessageTurnErrorEvent(pending.MessageTurnId, "Execution was interrupted before terminal closure.",
                    new(measurements)) { ErrorType = "runtime_restart_recovery", ThreadExecutionId = pending.ExecutionId, Timestamp = lastObservedAt };
                try
                {
                    await publisher.CommitAndPublishAsync(key, [recovered], new(snapshot.Cursor), CancellationToken.None).ConfigureAwait(false);
                }
                catch (ThreadAppendConflictException) { continue; }
            }
            await CloseExecutionAsync(store, publisher, thread,
                new UserMessagesInputEvent { SessionId = key.SessionId, ThreadId = key.ThreadId, ThreadExecutionId = pending.ExecutionId },
                succeeded: !interrupted, cancelled: false, stopping: interrupted, running: false, runConfig,
                _ => ValueTask.FromResult(false), "recovery_pending_input").ConfigureAwait(false);
            return;
        }
    }

    private static async ValueTask WithdrawReservationAsync(ISessionStore store, IAgentEventPublisher publisher,
        Thread thread, GoalData reserved)
    {
        var key = new ThreadKey(thread.SessionId, thread.Id);
        while (true)
        {
            var snapshot = await GoalPersistence.ReadAsync(store, key, CancellationToken.None).ConfigureAwait(false);
            var goal = snapshot.Goal.Current;
            if (goal?.GoalId != reserved.GoalId || goal.Revision != reserved.Revision || goal.Continuation is null) return;
            var withdrawn = goal with { Continuation = null, Revision = checked(goal.Revision + 1), UpdatedAt = DateTimeOffset.UtcNow };
            var state = snapshot.Goal with { Current = withdrawn };
            try
            {
                await GoalPersistence.CommitAsync(publisher, key, snapshot, state,
                    new GoalContinuationSkippedEvent(withdrawn, "runtime_stopping"), CancellationToken.None).ConfigureAwait(false);
                thread.MiddlewareState[GoalPersistence.StateKey] = JsonSerializer.Serialize(state, GoalJsonContext.Default.GoalPersistentState);
                return;
            }
            catch (ThreadAppendConflictException) { }
        }
    }
}
