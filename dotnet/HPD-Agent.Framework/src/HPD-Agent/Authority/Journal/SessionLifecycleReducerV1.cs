namespace HPD.Agent.Authority;

internal abstract record SessionLifecycleReductionV1
{
    private SessionLifecycleReductionV1() { }
    internal sealed record Applied(SessionLifecycleSnapshotBodyV1 Snapshot) : SessionLifecycleReductionV1;
    internal sealed record Idempotent(SessionLifecycleSnapshotBodyV1 Snapshot) : SessionLifecycleReductionV1;
    internal sealed record Rejected(SessionLifecycleSnapshotBodyV1 Snapshot, BoundedAscii SafeCode) : SessionLifecycleReductionV1;
    internal sealed record InvalidPredecessor(BoundedAscii SafeCode) : SessionLifecycleReductionV1;
}

internal static class SessionLifecycleReducerV1
{
    internal static SessionLifecycleReductionV1 Apply(
        SessionLifecycleSnapshotBodyV1? current,
        SessionLifecycleCommandBodyV1 command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (current is null)
            return command is SessionLifecycleCommandBodyV1.ReserveStarting
                ? new SessionLifecycleReductionV1.Applied(Starting())
                : new SessionLifecycleReductionV1.InvalidPredecessor(new BoundedAscii("session-not-reserved"));
        if (current.State == SessionLifecycleStateWireV1.Completed)
            return new SessionLifecycleReductionV1.Idempotent(current);
        return command switch
        {
            SessionLifecycleCommandBodyV1.ReserveStarting => Reject(current, "session-already-reserved"),
            SessionLifecycleCommandBodyV1.PublishReady ready => Ready(current, ready),
            SessionLifecycleCommandBodyV1.BeginDrain => Drain(current),
            SessionLifecycleCommandBodyV1.BeginTermination terminate => Terminate(current, terminate),
            SessionLifecycleCommandBodyV1.AdvanceTermination advance => Advance(current, advance),
            SessionLifecycleCommandBodyV1.Complete complete => Complete(current, complete),
            _ => Reject(current, "unknown-command"),
        };
    }

    private static SessionLifecycleReductionV1 Ready(
        SessionLifecycleSnapshotBodyV1 current,
        SessionLifecycleCommandBodyV1.PublishReady command)
    {
        if (current.State == SessionLifecycleStateWireV1.Active)
            return current.Availability == command.Availability
                ? new SessionLifecycleReductionV1.Idempotent(current)
                : Reject(current, "readiness-already-published");
        if (current.State != SessionLifecycleStateWireV1.Starting)
            return Reject(current, "readiness-after-admission-cut");
        return Applied(new(
            SessionLifecycleStateWireV1.Active, SessionAdmissionWireV1.Open, command.Availability,
            SessionReadinessWireV1.Succeeded, current.EstablishingTerminalIntent, current.EstablishingTerminalCause,
            current.CurrentTerminalIntent, current.CurrentTerminalCause, current.TerminalSeverity,
            current.ConvergencePhase, current.MutationFence, current.ConversationStopped));
    }

    private static SessionLifecycleReductionV1 Drain(SessionLifecycleSnapshotBodyV1 current)
    {
        if (current.State == SessionLifecycleStateWireV1.Draining)
            return new SessionLifecycleReductionV1.Idempotent(current);
        if (current.State is not (SessionLifecycleStateWireV1.Starting or SessionLifecycleStateWireV1.Active))
            return Reject(current, "drain-after-termination");
        return Applied(new(
            SessionLifecycleStateWireV1.Draining, SessionAdmissionWireV1.Closed, current.Availability,
            current.Readiness, current.EstablishingTerminalIntent, current.EstablishingTerminalCause,
            current.CurrentTerminalIntent, current.CurrentTerminalCause, current.TerminalSeverity,
            SessionConvergencePhaseWireV1.Draining, current.MutationFence, current.ConversationStopped));
    }

    private static SessionLifecycleReductionV1 Terminate(
        SessionLifecycleSnapshotBodyV1 current,
        SessionLifecycleCommandBodyV1.BeginTermination command)
    {
        if (current.State == SessionLifecycleStateWireV1.Terminating)
            return Escalate(current, command.Intent, command.Cause, command.Severity, command.Phase, false);
        if (command.Phase < current.ConvergencePhase)
            return Reject(current, "terminal-regression");
        return Applied(new(
            SessionLifecycleStateWireV1.Terminating, SessionAdmissionWireV1.Closed,
            SessionAvailabilityWireV1.Unavailable,
            current.Readiness == SessionReadinessWireV1.Unpublished
                ? SessionReadinessWireV1.Failed
                : current.Readiness,
            command.Intent, command.Cause, command.Intent, command.Cause,
            command.Severity, command.Phase, SessionMutationFenceWireV1.Fenced,
            current.ConversationStopped));
    }

    private static SessionLifecycleReductionV1 Advance(
        SessionLifecycleSnapshotBodyV1 current,
        SessionLifecycleCommandBodyV1.AdvanceTermination command)
    {
        if (current.State != SessionLifecycleStateWireV1.Terminating)
            return Reject(current, "advance-outside-termination");
        return Escalate(current, command.Intent, command.Cause, command.Severity,
            command.Phase, command.ConversationStopped);
    }

    private static SessionLifecycleReductionV1 Escalate(
        SessionLifecycleSnapshotBodyV1 current,
        SessionTerminalIntentWireV1 intent,
        SessionTerminalCauseWireV1 cause,
        SessionTerminalSeverityWireV1 severity,
        SessionConvergencePhaseWireV1 phase,
        bool conversationStopped)
    {
        if (severity < current.TerminalSeverity || phase < current.ConvergencePhase)
            return Reject(current, "terminal-regression");
        var next = new SessionLifecycleSnapshotBodyV1(
            current.State, SessionAdmissionWireV1.Closed, SessionAvailabilityWireV1.Unavailable,
            current.Readiness, current.EstablishingTerminalIntent, current.EstablishingTerminalCause,
            intent, cause, severity, phase, SessionMutationFenceWireV1.Fenced,
            current.ConversationStopped || conversationStopped);
        return next == current
            ? new SessionLifecycleReductionV1.Idempotent(current)
            : Applied(next);
    }

    private static SessionLifecycleReductionV1 Complete(
        SessionLifecycleSnapshotBodyV1 current,
        SessionLifecycleCommandBodyV1.Complete command)
    {
        if (current.State is not (SessionLifecycleStateWireV1.Draining or SessionLifecycleStateWireV1.Terminating))
            return Reject(current, "completion-before-convergence");
        var establishingIntent = current.EstablishingTerminalIntent == SessionTerminalIntentWireV1.None
            ? SessionTerminalIntentWireV1.GracefulStop : current.EstablishingTerminalIntent;
        var establishingCause = current.EstablishingTerminalCause == SessionTerminalCauseWireV1.None
            ? SessionTerminalCauseWireV1.Requested : current.EstablishingTerminalCause;
        var currentIntent = current.CurrentTerminalIntent == SessionTerminalIntentWireV1.None
            ? establishingIntent : current.CurrentTerminalIntent;
        var currentCause = current.CurrentTerminalCause == SessionTerminalCauseWireV1.None
            ? establishingCause : current.CurrentTerminalCause;
        var severity = current.TerminalSeverity == SessionTerminalSeverityWireV1.None
            ? SessionTerminalSeverityWireV1.Informational : current.TerminalSeverity;
        var phase = current.ConvergencePhase > SessionConvergencePhaseWireV1.Reporting
            ? current.ConvergencePhase : SessionConvergencePhaseWireV1.Reporting;
        return Applied(new(
            SessionLifecycleStateWireV1.Completed, SessionAdmissionWireV1.Closed,
            SessionAvailabilityWireV1.Unavailable, current.Readiness,
            establishingIntent, establishingCause, currentIntent, currentCause,
            severity, phase, SessionMutationFenceWireV1.Fenced,
            current.ConversationStopped || command.ConversationStopped));
    }

    private static SessionLifecycleSnapshotBodyV1 Starting() => new(
        SessionLifecycleStateWireV1.Starting, SessionAdmissionWireV1.Closed,
        SessionAvailabilityWireV1.Unavailable, SessionReadinessWireV1.Unpublished,
        SessionTerminalIntentWireV1.None, SessionTerminalCauseWireV1.None,
        SessionTerminalIntentWireV1.None, SessionTerminalCauseWireV1.None,
        SessionTerminalSeverityWireV1.None, SessionConvergencePhaseWireV1.None,
        SessionMutationFenceWireV1.Open, false);

    private static SessionLifecycleReductionV1 Applied(SessionLifecycleSnapshotBodyV1 value) =>
        new SessionLifecycleReductionV1.Applied(value);

    private static SessionLifecycleReductionV1 Reject(SessionLifecycleSnapshotBodyV1 value, string code) =>
        new SessionLifecycleReductionV1.Rejected(value, new BoundedAscii(code));
}
