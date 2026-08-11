using HPD.Agent.Audio;

namespace HPD.Agent.Audio.V2.Tests;

public sealed class LiveAudioSessionStateMachineV1Tests
{
    [Fact]
    public void InitialReadyDrainCompletePreservesFiveStateAndOrthogonalTruth()
    {
        var initial = LiveAudioSessionStateMachineV1.Initial;
        Assert.Equal(LiveAudioSessionStateV1.Starting, initial.State);
        Assert.Equal(LiveAudioAdmissionStateV1.Closed, initial.Admission);
        Assert.Equal(LiveAudioReadinessV1.Unpublished, initial.Readiness);
        Assert.Equal(LiveAudioMutationFenceV1.Open, initial.MutationFence);

        var active = Applied(initial,
            new LiveAudioLifecycleCommandV1.PublishReady(LiveAudioAvailabilityV1.Available));
        Assert.Equal(LiveAudioSessionStateV1.Active, active.State);
        Assert.Equal(LiveAudioAdmissionStateV1.Open, active.Admission);
        Assert.Equal(LiveAudioReadinessV1.Succeeded, active.Readiness);

        var draining = Applied(active, new LiveAudioLifecycleCommandV1.BeginDrain());
        Assert.Equal(LiveAudioSessionStateV1.Draining, draining.State);
        Assert.Equal(LiveAudioAdmissionStateV1.Closed, draining.Admission);
        Assert.Equal(LiveAudioMutationFenceV1.Open, draining.MutationFence);

        var completed = Applied(draining, new LiveAudioLifecycleCommandV1.Complete(true));
        Assert.Equal(LiveAudioSessionStateV1.Completed, completed.State);
        Assert.Equal(LiveAudioTerminalIntentV1.GracefulStop, completed.EstablishingTerminalIntent);
        Assert.Equal(LiveAudioTerminalCauseV1.Requested, completed.EstablishingTerminalCause);
        Assert.Equal(LiveAudioTerminalSeverityV1.Informational, completed.TerminalSeverity);
        Assert.Equal(LiveAudioMutationFenceV1.Fenced, completed.MutationFence);
        Assert.True(completed.ConversationStopped);
    }

    [Fact]
    public void StartingMayDrainOrTerminateButCannotPublishReadinessAfterItsAdmissionCut()
    {
        var draining = Applied(LiveAudioSessionStateMachineV1.Initial,
            new LiveAudioLifecycleCommandV1.BeginDrain());
        Assert.Equal(LiveAudioReadinessV1.Unpublished, draining.Readiness);
        Assert.Equal("readiness-after-admission-cut", Rejected(draining,
            new LiveAudioLifecycleCommandV1.PublishReady(LiveAudioAvailabilityV1.Available)).SafeCode);

        var terminating = Applied(LiveAudioSessionStateMachineV1.Initial, Fault());
        Assert.Equal(LiveAudioSessionStateV1.Terminating, terminating.State);
        Assert.Equal(LiveAudioReadinessV1.Failed, terminating.Readiness);
        Assert.Equal(LiveAudioMutationFenceV1.Fenced, terminating.MutationFence);
    }

    [Fact]
    public void TerminalEscalationIsMonotoneAndDoesNotCollapseCauseIntoLifecycleState()
    {
        var terminating = Applied(LiveAudioSessionStateMachineV1.Initial, Fault());
        var advanced = Applied(terminating, new LiveAudioLifecycleCommandV1.AdvanceTermination(
            LiveAudioConvergencePhaseV1.Containing,
            LiveAudioTerminalIntentV1.DeadlineContainment,
            LiveAudioTerminalCauseV1.DeadlineExpired,
            LiveAudioTerminalSeverityV1.Fatal,
            ConversationStopped: true));
        Assert.Equal(LiveAudioSessionStateV1.Terminating, advanced.State);
        Assert.Equal(LiveAudioConvergencePhaseV1.Containing, advanced.ConvergencePhase);
        Assert.Equal(LiveAudioTerminalIntentV1.Fault, advanced.EstablishingTerminalIntent);
        Assert.Equal(LiveAudioTerminalCauseV1.ParticipantFault, advanced.EstablishingTerminalCause);
        Assert.Equal(LiveAudioTerminalIntentV1.DeadlineContainment, advanced.CurrentTerminalIntent);
        Assert.Equal(LiveAudioTerminalCauseV1.DeadlineExpired, advanced.CurrentTerminalCause);
        Assert.Equal("terminal-regression", Rejected(advanced,
            new LiveAudioLifecycleCommandV1.AdvanceTermination(
                LiveAudioConvergencePhaseV1.Stopping,
                LiveAudioTerminalIntentV1.Fault,
                LiveAudioTerminalCauseV1.ParticipantFault,
                LiveAudioTerminalSeverityV1.Recoverable,
                ConversationStopped: false)).SafeCode);

        var completed = Applied(advanced, new LiveAudioLifecycleCommandV1.Complete(false));
        Assert.Equal(LiveAudioConvergencePhaseV1.Containing, completed.ConvergencePhase);
        Assert.True(completed.ConversationStopped);
    }

    [Fact]
    public void CompletedIsImmutableForEveryCommandShape()
    {
        var draining = Applied(LiveAudioSessionStateMachineV1.Initial,
            new LiveAudioLifecycleCommandV1.BeginDrain());
        var completed = Applied(draining, new LiveAudioLifecycleCommandV1.Complete(false));
        LiveAudioLifecycleCommandV1[] commands =
        [
            new LiveAudioLifecycleCommandV1.PublishReady(LiveAudioAvailabilityV1.Available),
            new LiveAudioLifecycleCommandV1.BeginDrain(),
            Fault(),
            new LiveAudioLifecycleCommandV1.AdvanceTermination(
                LiveAudioConvergencePhaseV1.Containing,
                LiveAudioTerminalIntentV1.DeadlineContainment,
                LiveAudioTerminalCauseV1.DeadlineExpired,
                LiveAudioTerminalSeverityV1.Fatal,
                true),
            new LiveAudioLifecycleCommandV1.Complete(true),
        ];

        foreach (var command in commands)
        {
            var result = Assert.IsType<LiveAudioLifecycleTransitionV1.Idempotent>(
                LiveAudioSessionStateMachineV1.Apply(completed, command));
            Assert.Same(completed, result.Snapshot);
        }
    }

    [Fact]
    public void IllegalTransitionsAndInvalidEnumValuesFailClosed()
    {
        var initial = LiveAudioSessionStateMachineV1.Initial;
        Assert.Equal("completion-before-convergence", Rejected(initial,
            new LiveAudioLifecycleCommandV1.Complete(false)).SafeCode);
        Assert.Equal("invalid-readiness-availability", Rejected(initial,
            new LiveAudioLifecycleCommandV1.PublishReady(LiveAudioAvailabilityV1.Unavailable)).SafeCode);
        Assert.Equal("invalid-terminal-command", Rejected(initial,
            new LiveAudioLifecycleCommandV1.BeginTermination(
                LiveAudioTerminalIntentV1.None,
                LiveAudioTerminalCauseV1.None,
                LiveAudioTerminalSeverityV1.None,
                LiveAudioConvergencePhaseV1.None)).SafeCode);
        Assert.Equal("invalid-readiness-availability", Rejected(initial,
            new LiveAudioLifecycleCommandV1.PublishReady((LiveAudioAvailabilityV1)999)).SafeCode);
        foreach (var unavailable in new[]
                 {
                     LiveAudioAvailabilityV1.Suspended,
                     LiveAudioAvailabilityV1.Reconnecting,
                     LiveAudioAvailabilityV1.Degraded,
                 })
            Assert.Equal("invalid-readiness-availability", Rejected(initial,
                new LiveAudioLifecycleCommandV1.PublishReady(unavailable)).SafeCode);

        var active = Applied(initial,
            new LiveAudioLifecycleCommandV1.PublishReady(LiveAudioAvailabilityV1.Available));
        Assert.Equal("completion-before-convergence", Rejected(active,
            new LiveAudioLifecycleCommandV1.Complete(false)).SafeCode);
        Assert.Equal("invalid-readiness-availability", Rejected(active,
            new LiveAudioLifecycleCommandV1.PublishReady(LiveAudioAvailabilityV1.Degraded)).SafeCode);
    }

    [Fact]
    public void IntentNumbersDoNotDefineEscalationAndEstablishingCauseIsNeverLost()
    {
        var initial = Applied(LiveAudioSessionStateMachineV1.Initial,
            new LiveAudioLifecycleCommandV1.BeginTermination(
                LiveAudioTerminalIntentV1.DeadlineContainment,
                LiveAudioTerminalCauseV1.DeadlineExpired,
                LiveAudioTerminalSeverityV1.Fatal,
                LiveAudioConvergencePhaseV1.Fencing));
        var changedReason = Applied(initial,
            new LiveAudioLifecycleCommandV1.AdvanceTermination(
                LiveAudioConvergencePhaseV1.Stopping,
                LiveAudioTerminalIntentV1.Fault,
                LiveAudioTerminalCauseV1.ParticipantFault,
                LiveAudioTerminalSeverityV1.Fatal,
                false));

        Assert.Equal(LiveAudioTerminalIntentV1.DeadlineContainment, changedReason.EstablishingTerminalIntent);
        Assert.Equal(LiveAudioTerminalCauseV1.DeadlineExpired, changedReason.EstablishingTerminalCause);
        Assert.Equal(LiveAudioTerminalIntentV1.Fault, changedReason.CurrentTerminalIntent);
        Assert.Equal(LiveAudioTerminalCauseV1.ParticipantFault, changedReason.CurrentTerminalCause);
    }

    [Fact]
    public void EveryReachableStateReturnsOneClosedResultForEveryCommandFamily()
    {
        var starting = LiveAudioSessionStateMachineV1.Initial;
        var active = Applied(starting,
            new LiveAudioLifecycleCommandV1.PublishReady(LiveAudioAvailabilityV1.Available));
        var draining = Applied(active, new LiveAudioLifecycleCommandV1.BeginDrain());
        var terminating = Applied(active, Fault());
        var completed = Applied(draining, new LiveAudioLifecycleCommandV1.Complete(false));
        LiveAudioLifecycleCommandV1[] commands =
        [
            new LiveAudioLifecycleCommandV1.PublishReady(LiveAudioAvailabilityV1.Available),
            new LiveAudioLifecycleCommandV1.BeginDrain(),
            Fault(),
            new LiveAudioLifecycleCommandV1.AdvanceTermination(
                LiveAudioConvergencePhaseV1.Finalizing,
                LiveAudioTerminalIntentV1.Fault,
                LiveAudioTerminalCauseV1.ParticipantFault,
                LiveAudioTerminalSeverityV1.Recoverable,
                false),
            new LiveAudioLifecycleCommandV1.Complete(false),
        ];

        foreach (var state in new[] { starting, active, draining, terminating, completed })
        foreach (var command in commands)
            Assert.NotNull(LiveAudioSessionStateMachineV1.Apply(state, command));
    }

    private static LiveAudioLifecycleCommandV1.BeginTermination Fault() => new(
        LiveAudioTerminalIntentV1.Fault,
        LiveAudioTerminalCauseV1.ParticipantFault,
        LiveAudioTerminalSeverityV1.Recoverable,
        LiveAudioConvergencePhaseV1.Fencing);

    private static LiveAudioLifecycleSnapshotV1 Applied(
        LiveAudioLifecycleSnapshotV1 current, LiveAudioLifecycleCommandV1 command) =>
        Assert.IsType<LiveAudioLifecycleTransitionV1.Applied>(
            LiveAudioSessionStateMachineV1.Apply(current, command)).Snapshot;

    private static LiveAudioLifecycleTransitionV1.Rejected Rejected(
        LiveAudioLifecycleSnapshotV1 current, LiveAudioLifecycleCommandV1 command) =>
        Assert.IsType<LiveAudioLifecycleTransitionV1.Rejected>(
            LiveAudioSessionStateMachineV1.Apply(current, command));
}
