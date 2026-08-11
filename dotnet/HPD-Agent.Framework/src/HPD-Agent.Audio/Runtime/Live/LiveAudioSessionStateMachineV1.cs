namespace HPD.Agent.Audio;

internal abstract record LiveAudioLifecycleCommandV1
{
    private LiveAudioLifecycleCommandV1() { }
    internal sealed record PublishReady(LiveAudioAvailabilityV1 Availability) : LiveAudioLifecycleCommandV1;
    internal sealed record BeginDrain : LiveAudioLifecycleCommandV1;
    internal sealed record BeginTermination(
        LiveAudioTerminalIntentV1 Intent,
        LiveAudioTerminalCauseV1 Cause,
        LiveAudioTerminalSeverityV1 Severity,
        LiveAudioConvergencePhaseV1 Phase) : LiveAudioLifecycleCommandV1;
    internal sealed record AdvanceTermination(
        LiveAudioConvergencePhaseV1 Phase,
        LiveAudioTerminalIntentV1 Intent,
        LiveAudioTerminalCauseV1 Cause,
        LiveAudioTerminalSeverityV1 Severity,
        bool ConversationStopped) : LiveAudioLifecycleCommandV1;
    internal sealed record Complete(bool ConversationStopped) : LiveAudioLifecycleCommandV1;
}

internal abstract record LiveAudioLifecycleTransitionV1
{
    private LiveAudioLifecycleTransitionV1() { }
    internal sealed record Applied(LiveAudioLifecycleSnapshotV1 Snapshot) : LiveAudioLifecycleTransitionV1;
    internal sealed record Idempotent(LiveAudioLifecycleSnapshotV1 Snapshot) : LiveAudioLifecycleTransitionV1;
    internal sealed record Rejected(LiveAudioLifecycleSnapshotV1 Snapshot, string SafeCode) : LiveAudioLifecycleTransitionV1;
}

internal static class LiveAudioSessionStateMachineV1
{
    internal static LiveAudioLifecycleSnapshotV1 Initial { get; } = new(
        LiveAudioSessionStateV1.Starting,
        LiveAudioAdmissionStateV1.Closed,
        LiveAudioAvailabilityV1.Unavailable,
        LiveAudioReadinessV1.Unpublished,
        LiveAudioTerminalIntentV1.None,
        LiveAudioTerminalCauseV1.None,
        LiveAudioTerminalIntentV1.None,
        LiveAudioTerminalCauseV1.None,
        LiveAudioTerminalSeverityV1.None,
        LiveAudioConvergencePhaseV1.None,
        LiveAudioMutationFenceV1.Open,
        conversationStopped: false);

    internal static LiveAudioLifecycleTransitionV1 Apply(
        LiveAudioLifecycleSnapshotV1 current,
        LiveAudioLifecycleCommandV1 command)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(command);
        if (current.State == LiveAudioSessionStateV1.Completed)
            return new LiveAudioLifecycleTransitionV1.Idempotent(current);
        return command switch
        {
            LiveAudioLifecycleCommandV1.PublishReady ready => Ready(current, ready),
            LiveAudioLifecycleCommandV1.BeginDrain => Drain(current),
            LiveAudioLifecycleCommandV1.BeginTermination terminate => Terminate(current, terminate),
            LiveAudioLifecycleCommandV1.AdvanceTermination advance => Advance(current, advance),
            LiveAudioLifecycleCommandV1.Complete complete => Complete(current, complete),
            _ => Reject(current, "unknown-command"),
        };
    }

    private static LiveAudioLifecycleTransitionV1 Ready(
        LiveAudioLifecycleSnapshotV1 current, LiveAudioLifecycleCommandV1.PublishReady command)
    {
        if (command.Availability != LiveAudioAvailabilityV1.Available)
            return Reject(current, "invalid-readiness-availability");
        if (current.State == LiveAudioSessionStateV1.Active)
            return current.Availability == command.Availability
                ? new LiveAudioLifecycleTransitionV1.Idempotent(current)
                : Reject(current, "readiness-already-published");
        if (current.State != LiveAudioSessionStateV1.Starting)
            return Reject(current, "readiness-after-admission-cut");
        return Applied(new(
            LiveAudioSessionStateV1.Active, LiveAudioAdmissionStateV1.Open, command.Availability,
            LiveAudioReadinessV1.Succeeded, current.EstablishingTerminalIntent, current.EstablishingTerminalCause,
            current.CurrentTerminalIntent, current.CurrentTerminalCause,
            current.TerminalSeverity, current.ConvergencePhase, current.MutationFence,
            current.ConversationStopped));
    }

    private static LiveAudioLifecycleTransitionV1 Drain(LiveAudioLifecycleSnapshotV1 current)
    {
        if (current.State == LiveAudioSessionStateV1.Draining)
            return new LiveAudioLifecycleTransitionV1.Idempotent(current);
        if (current.State is not (LiveAudioSessionStateV1.Starting or LiveAudioSessionStateV1.Active))
            return Reject(current, "drain-after-termination");
        return Applied(new(
            LiveAudioSessionStateV1.Draining, LiveAudioAdmissionStateV1.Closed, current.Availability,
            current.Readiness, current.EstablishingTerminalIntent, current.EstablishingTerminalCause,
            current.CurrentTerminalIntent, current.CurrentTerminalCause, current.TerminalSeverity,
            LiveAudioConvergencePhaseV1.Draining, current.MutationFence, current.ConversationStopped));
    }

    private static LiveAudioLifecycleTransitionV1 Terminate(
        LiveAudioLifecycleSnapshotV1 current, LiveAudioLifecycleCommandV1.BeginTermination command)
    {
        if (!ValidTerminal(command.Intent, command.Cause, command.Severity, command.Phase))
            return Reject(current, "invalid-terminal-command");
        if (current.State == LiveAudioSessionStateV1.Terminating)
            return Escalate(current, command.Intent, command.Cause, command.Severity, command.Phase, false);
        return Applied(new(
            LiveAudioSessionStateV1.Terminating, LiveAudioAdmissionStateV1.Closed,
            LiveAudioAvailabilityV1.Unavailable,
            current.Readiness == LiveAudioReadinessV1.Unpublished
                ? LiveAudioReadinessV1.Failed
                : current.Readiness,
            command.Intent, command.Cause, command.Intent, command.Cause, command.Severity, command.Phase,
            LiveAudioMutationFenceV1.Fenced, current.ConversationStopped));
    }

    private static LiveAudioLifecycleTransitionV1 Advance(
        LiveAudioLifecycleSnapshotV1 current, LiveAudioLifecycleCommandV1.AdvanceTermination command)
    {
        if (current.State != LiveAudioSessionStateV1.Terminating)
            return Reject(current, "advance-outside-termination");
        if (!ValidTerminal(command.Intent, command.Cause, command.Severity, command.Phase))
            return Reject(current, "invalid-terminal-command");
        return Escalate(current, command.Intent, command.Cause, command.Severity, command.Phase,
            command.ConversationStopped);
    }

    private static LiveAudioLifecycleTransitionV1 Escalate(
        LiveAudioLifecycleSnapshotV1 current,
        LiveAudioTerminalIntentV1 intent,
        LiveAudioTerminalCauseV1 cause,
        LiveAudioTerminalSeverityV1 severity,
        LiveAudioConvergencePhaseV1 phase,
        bool conversationStopped)
    {
        if (severity < current.TerminalSeverity || phase < current.ConvergencePhase)
            return Reject(current, "terminal-regression");
        var next = new LiveAudioLifecycleSnapshotV1(
            current.State, LiveAudioAdmissionStateV1.Closed, LiveAudioAvailabilityV1.Unavailable,
            current.Readiness, current.EstablishingTerminalIntent, current.EstablishingTerminalCause,
            intent, cause, severity, phase, LiveAudioMutationFenceV1.Fenced,
            current.ConversationStopped || conversationStopped);
        return next == current
            ? new LiveAudioLifecycleTransitionV1.Idempotent(current)
            : Applied(next);
    }

    private static LiveAudioLifecycleTransitionV1 Complete(
        LiveAudioLifecycleSnapshotV1 current, LiveAudioLifecycleCommandV1.Complete command)
    {
        if (current.State is not (LiveAudioSessionStateV1.Draining or LiveAudioSessionStateV1.Terminating))
            return Reject(current, "completion-before-convergence");
        var establishingIntent = current.EstablishingTerminalIntent == LiveAudioTerminalIntentV1.None
            ? LiveAudioTerminalIntentV1.GracefulStop
            : current.EstablishingTerminalIntent;
        var establishingCause = current.EstablishingTerminalCause == LiveAudioTerminalCauseV1.None
            ? LiveAudioTerminalCauseV1.Requested
            : current.EstablishingTerminalCause;
        var currentIntent = current.CurrentTerminalIntent == LiveAudioTerminalIntentV1.None
            ? establishingIntent
            : current.CurrentTerminalIntent;
        var currentCause = current.CurrentTerminalCause == LiveAudioTerminalCauseV1.None
            ? establishingCause
            : current.CurrentTerminalCause;
        var severity = current.TerminalSeverity == LiveAudioTerminalSeverityV1.None
            ? LiveAudioTerminalSeverityV1.Informational
            : current.TerminalSeverity;
        var finalPhase = current.ConvergencePhase > LiveAudioConvergencePhaseV1.Reporting
            ? current.ConvergencePhase
            : LiveAudioConvergencePhaseV1.Reporting;
        return Applied(new(
            LiveAudioSessionStateV1.Completed, LiveAudioAdmissionStateV1.Closed,
            LiveAudioAvailabilityV1.Unavailable, current.Readiness,
            establishingIntent, establishingCause, currentIntent, currentCause, severity,
            finalPhase, LiveAudioMutationFenceV1.Fenced,
            current.ConversationStopped || command.ConversationStopped));
    }

    private static bool ValidTerminal(
        LiveAudioTerminalIntentV1 intent,
        LiveAudioTerminalCauseV1 cause,
        LiveAudioTerminalSeverityV1 severity,
        LiveAudioConvergencePhaseV1 phase) =>
        Enum.IsDefined(intent) && intent != LiveAudioTerminalIntentV1.None &&
        Enum.IsDefined(cause) && cause != LiveAudioTerminalCauseV1.None &&
        Enum.IsDefined(severity) && severity != LiveAudioTerminalSeverityV1.None &&
        Enum.IsDefined(phase) && phase != LiveAudioConvergencePhaseV1.None;

    private static LiveAudioLifecycleTransitionV1 Applied(LiveAudioLifecycleSnapshotV1 snapshot) =>
        new LiveAudioLifecycleTransitionV1.Applied(snapshot);

    private static LiveAudioLifecycleTransitionV1 Reject(
        LiveAudioLifecycleSnapshotV1 snapshot, string code) =>
        new LiveAudioLifecycleTransitionV1.Rejected(snapshot, code);
}
