using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class SessionLifecycleReducerV1Tests
{
    [Fact]
    public void ReserveReadyDrainComplete_PreservesFiveStateAndOrthogonalTruth()
    {
        var starting = Applied(null, Reserve());
        Assert.Equal((SessionLifecycleStateWireV1.Starting, SessionAdmissionWireV1.Closed,
            SessionReadinessWireV1.Unpublished, SessionMutationFenceWireV1.Open),
            (starting.State, starting.Admission, starting.Readiness, starting.MutationFence));

        var active = Applied(starting, new SessionLifecycleCommandBodyV1.PublishReady(
            Operation(), Position(1), SessionAvailabilityWireV1.Available));
        Assert.Equal((SessionLifecycleStateWireV1.Active, SessionAdmissionWireV1.Open,
            SessionReadinessWireV1.Succeeded), (active.State, active.Admission, active.Readiness));

        var draining = Applied(active, new SessionLifecycleCommandBodyV1.BeginDrain(Operation(), Position(2)));
        Assert.Equal(SessionLifecycleStateWireV1.Draining, draining.State);
        Assert.Equal(SessionAdmissionWireV1.Closed, draining.Admission);

        var completed = Applied(draining, new SessionLifecycleCommandBodyV1.Complete(Operation(), Position(3), true));
        Assert.Equal(SessionLifecycleStateWireV1.Completed, completed.State);
        Assert.Equal(SessionTerminalIntentWireV1.GracefulStop, completed.EstablishingTerminalIntent);
        Assert.Equal(SessionTerminalCauseWireV1.Requested, completed.EstablishingTerminalCause);
        Assert.Equal(SessionTerminalSeverityWireV1.Informational, completed.TerminalSeverity);
        Assert.Equal(SessionMutationFenceWireV1.Fenced, completed.MutationFence);
        Assert.True(completed.ConversationStopped);
    }

    [Fact]
    public void OnlyReserveMayTransitionAbsenceAndReserveCannotReplaceExistingState()
    {
        Assert.Equal("session-not-reserved", Assert.IsType<SessionLifecycleReductionV1.InvalidPredecessor>(
            SessionLifecycleReducerV1.Apply(
                null, new SessionLifecycleCommandBodyV1.BeginDrain(Operation(), Position(1)))).SafeCode.ToString());
        var starting = Applied(null, Reserve());
        Assert.Equal("session-already-reserved", Rejected(starting, Reserve()).SafeCode.ToString());
    }

    [Fact]
    public void StartingMayDrainOrTerminateButCannotPublishReadinessAfterCut()
    {
        var starting = Applied(null, Reserve());
        var draining = Applied(starting, new SessionLifecycleCommandBodyV1.BeginDrain(Operation(), Position(1)));
        Assert.Equal("readiness-after-admission-cut", Rejected(draining,
            new SessionLifecycleCommandBodyV1.PublishReady(Operation(), Position(2), SessionAvailabilityWireV1.Available)).SafeCode.ToString());

        var terminating = Applied(starting, Fault(Position(1)));
        Assert.Equal(SessionReadinessWireV1.Failed, terminating.Readiness);
        Assert.Equal(SessionMutationFenceWireV1.Fenced, terminating.MutationFence);
    }

    [Fact]
    public void EscalationIsMonotoneAndPreservesEstablishingCause()
    {
        var terminating = Applied(Applied(null, Reserve()), Fault(Position(1)));
        var advanced = Applied(terminating, new SessionLifecycleCommandBodyV1.AdvanceTermination(
            Operation(), Position(2), SessionConvergencePhaseWireV1.Containing,
            SessionTerminalIntentWireV1.DeadlineContainment, SessionTerminalCauseWireV1.DeadlineExpired,
            SessionTerminalSeverityWireV1.Fatal, true));
        Assert.Equal(SessionTerminalIntentWireV1.Fault, advanced.EstablishingTerminalIntent);
        Assert.Equal(SessionTerminalCauseWireV1.ParticipantFault, advanced.EstablishingTerminalCause);
        Assert.Equal(SessionTerminalIntentWireV1.DeadlineContainment, advanced.CurrentTerminalIntent);
        Assert.Equal(SessionTerminalCauseWireV1.DeadlineExpired, advanced.CurrentTerminalCause);
        Assert.Equal("terminal-regression", Rejected(advanced,
            new SessionLifecycleCommandBodyV1.AdvanceTermination(
                Operation(), Position(3), SessionConvergencePhaseWireV1.Stopping,
                SessionTerminalIntentWireV1.Fault, SessionTerminalCauseWireV1.ParticipantFault,
                SessionTerminalSeverityWireV1.Recoverable, false)).SafeCode.ToString());
    }

    [Fact]
    public void DrainingToTermination_CannotRegressConvergencePhase()
    {
        var starting = Applied(null, Reserve());
        var draining = Applied(starting, new SessionLifecycleCommandBodyV1.BeginDrain(Operation(), Position(1)));
        Assert.Equal("terminal-regression", Rejected(draining,
            new SessionLifecycleCommandBodyV1.BeginTermination(
                Operation(), Position(2), SessionTerminalIntentWireV1.Fault,
                SessionTerminalCauseWireV1.ParticipantFault, SessionTerminalSeverityWireV1.Recoverable,
                SessionConvergencePhaseWireV1.Quiescing)).SafeCode.ToString());
        var terminating = Applied(draining, new SessionLifecycleCommandBodyV1.BeginTermination(
            Operation(), Position(2), SessionTerminalIntentWireV1.Fault,
            SessionTerminalCauseWireV1.ParticipantFault, SessionTerminalSeverityWireV1.Recoverable,
            SessionConvergencePhaseWireV1.Fencing));
        Assert.Equal(SessionConvergencePhaseWireV1.Fencing, terminating.ConvergencePhase);
    }

    [Fact]
    public void CompletedIsImmutableForEveryCommandFamily()
    {
        var starting = Applied(null, Reserve());
        var draining = Applied(starting, new SessionLifecycleCommandBodyV1.BeginDrain(Operation(), Position(1)));
        var completed = Applied(draining, new SessionLifecycleCommandBodyV1.Complete(Operation(), Position(2), false));
        SessionLifecycleCommandBodyV1[] commands =
        [
            Reserve(),
            new SessionLifecycleCommandBodyV1.PublishReady(Operation(), Position(3), SessionAvailabilityWireV1.Available),
            new SessionLifecycleCommandBodyV1.BeginDrain(Operation(), Position(3)),
            Fault(Position(3)),
            new SessionLifecycleCommandBodyV1.AdvanceTermination(Operation(), Position(3),
                SessionConvergencePhaseWireV1.Containing, SessionTerminalIntentWireV1.Abort,
                SessionTerminalCauseWireV1.HostForced, SessionTerminalSeverityWireV1.Fatal, true),
            new SessionLifecycleCommandBodyV1.Complete(Operation(), Position(3), true),
        ];
        foreach (var command in commands)
            Assert.Same(completed, Assert.IsType<SessionLifecycleReductionV1.Idempotent>(
                SessionLifecycleReducerV1.Apply(completed, command)).Snapshot);
    }

    [Fact]
    public void EveryReachableStateAndCommandFamilyReturnsOneClosedResult()
    {
        var starting = Applied(null, Reserve());
        var active = Applied(starting, new SessionLifecycleCommandBodyV1.PublishReady(
            Operation(), Position(1), SessionAvailabilityWireV1.Available));
        var draining = Applied(active, new SessionLifecycleCommandBodyV1.BeginDrain(Operation(), Position(2)));
        var terminating = Applied(active, Fault(Position(2)));
        var completed = Applied(draining, new SessionLifecycleCommandBodyV1.Complete(Operation(), Position(3), false));
        SessionLifecycleCommandBodyV1[] commands =
        [
            Reserve(),
            new SessionLifecycleCommandBodyV1.PublishReady(Operation(), Position(4), SessionAvailabilityWireV1.Available),
            new SessionLifecycleCommandBodyV1.BeginDrain(Operation(), Position(4)),
            Fault(Position(4)),
            new SessionLifecycleCommandBodyV1.AdvanceTermination(Operation(), Position(4),
                SessionConvergencePhaseWireV1.Finalizing, SessionTerminalIntentWireV1.Fault,
                SessionTerminalCauseWireV1.ParticipantFault, SessionTerminalSeverityWireV1.Recoverable, false),
            new SessionLifecycleCommandBodyV1.Complete(Operation(), Position(4), false),
        ];
        foreach (var state in new[] { starting, active, draining, terminating, completed })
        foreach (var command in commands)
            Assert.NotNull(SessionLifecycleReducerV1.Apply(state, command));
    }

    private static SessionLifecycleCommandBodyV1.ReserveStarting Reserve() =>
        new(Operation(), Hash256.Compute("request"u8));

    private static SessionLifecycleCommandBodyV1.BeginTermination Fault(JournalPositionV1 position) => new(
        Operation(), position, SessionTerminalIntentWireV1.Fault,
        SessionTerminalCauseWireV1.ParticipantFault, SessionTerminalSeverityWireV1.Recoverable,
        SessionConvergencePhaseWireV1.Fencing);

    private static SessionLifecycleSnapshotBodyV1 Applied(
        SessionLifecycleSnapshotBodyV1? current, SessionLifecycleCommandBodyV1 command) =>
        Assert.IsType<SessionLifecycleReductionV1.Applied>(SessionLifecycleReducerV1.Apply(current, command)).Snapshot;

    private static SessionLifecycleReductionV1.Rejected Rejected(
        SessionLifecycleSnapshotBodyV1 current, SessionLifecycleCommandBodyV1 command) =>
        Assert.IsType<SessionLifecycleReductionV1.Rejected>(SessionLifecycleReducerV1.Apply(current, command));

    private static OperationId Operation() => OperationId.Create();

    private static JournalPositionV1 Position(long sequence) => new(
        new SessionAuthorityStampV1(
            RuntimeGenerationId.FromValue(StableId128.FromBytes(Convert.FromHexString("101112131415161718191a1b1c1d1e1f"))),
            LiveSessionId.FromValue(StableId128.FromBytes(Convert.FromHexString("202122232425262728292a2b2c2d2e2f")))), sequence);
}
