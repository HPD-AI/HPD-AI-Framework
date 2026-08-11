using HPD.Agent.Audio;
using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class SessionLifecycleAudioEquivalenceV1Tests
{
    [Fact]
    public void WireEnumValues_AreExhaustivelyEqualToPublicAudioEnums()
    {
        Equal<SessionLifecycleStateWireV1, LiveAudioSessionStateV1>();
        Equal<SessionAdmissionWireV1, LiveAudioAdmissionStateV1>();
        Equal<SessionAvailabilityWireV1, LiveAudioAvailabilityV1>();
        Equal<SessionReadinessWireV1, LiveAudioReadinessV1>();
        Equal<SessionTerminalIntentWireV1, LiveAudioTerminalIntentV1>();
        Equal<SessionTerminalCauseWireV1, LiveAudioTerminalCauseV1>();
        Equal<SessionTerminalSeverityWireV1, LiveAudioTerminalSeverityV1>();
        Equal<SessionConvergencePhaseWireV1, LiveAudioConvergencePhaseV1>();
        Equal<SessionMutationFenceWireV1, LiveAudioMutationFenceV1>();
    }

    [Fact]
    public void CoreReserve_ProducesExactFormerAudioInitialSnapshot()
    {
        var core = Assert.IsType<SessionLifecycleReductionV1.Applied>(SessionLifecycleReducerV1.Apply(
            null, new SessionLifecycleCommandBodyV1.ReserveStarting(Operation(), Hash256.Compute("request"u8)))).Snapshot;
        AssertSnapshot(core, LiveAudioSessionStateMachineV1.Initial);
    }

    [Fact]
    public void RepresentativeLifecycleAxesAndValidCommandFamilies_AgreeExceptCorrectedRegression()
    {
        var audioStarting = LiveAudioSessionStateMachineV1.Initial;
        var coreStarting = CoreStarting();
        var audioActive = AudioApplied(audioStarting,
            new LiveAudioLifecycleCommandV1.PublishReady(LiveAudioAvailabilityV1.Available));
        var coreActive = CoreApplied(coreStarting, Ready());
        var audioDraining = AudioApplied(audioActive, new LiveAudioLifecycleCommandV1.BeginDrain());
        var coreDraining = CoreApplied(coreActive, Drain());
        var audioTerminating = AudioApplied(audioActive, AudioFault());
        var coreTerminating = CoreApplied(coreActive, CoreFault());
        var audioCompleted = AudioApplied(audioDraining, new LiveAudioLifecycleCommandV1.Complete(false));
        var coreCompleted = CoreApplied(coreDraining, Complete(false));

        var pairs = new[]
        {
            (coreStarting, audioStarting), (coreActive, audioActive), (coreDraining, audioDraining),
            (coreTerminating, audioTerminating), (coreCompleted, audioCompleted),
        };
        var commands = new (Func<SessionLifecycleCommandBodyV1> Core, Func<LiveAudioLifecycleCommandV1> Audio)[]
        {
            (() => Ready(), () => new LiveAudioLifecycleCommandV1.PublishReady(LiveAudioAvailabilityV1.Available)),
            (() => Drain(), () => new LiveAudioLifecycleCommandV1.BeginDrain()),
            (() => CoreFault(), () => AudioFault()),
            (() => Advance(), () => new LiveAudioLifecycleCommandV1.AdvanceTermination(
                LiveAudioConvergencePhaseV1.Finalizing, LiveAudioTerminalIntentV1.Fault,
                LiveAudioTerminalCauseV1.ParticipantFault, LiveAudioTerminalSeverityV1.Recoverable, false)),
            (() => Complete(false), () => new LiveAudioLifecycleCommandV1.Complete(false)),
        };

        foreach (var (coreState, audioState) in pairs)
        foreach (var (coreCommand, audioCommand) in commands)
            AssertReduction(SessionLifecycleReducerV1.Apply(coreState, coreCommand()),
                LiveAudioSessionStateMachineV1.Apply(audioState, audioCommand()));
    }

    [Fact]
    public void EveryTerminalEnumAndMonotonicBoundary_AgreesWithFormerReducer()
    {
        var coreStarting = CoreStarting();
        var audioStarting = LiveAudioSessionStateMachineV1.Initial;
        var coreActive = CoreApplied(coreStarting, Ready());
        var audioActive = AudioApplied(audioStarting,
            new LiveAudioLifecycleCommandV1.PublishReady(LiveAudioAvailabilityV1.Available));
        var coreStartingDrain = CoreApplied(coreStarting, Drain());
        var audioStartingDrain = AudioApplied(audioStarting, new LiveAudioLifecycleCommandV1.BeginDrain());
        var coreActiveDrain = CoreApplied(coreActive, Drain());
        var audioActiveDrain = AudioApplied(audioActive, new LiveAudioLifecycleCommandV1.BeginDrain());
        var sources = new[]
        {
            (coreStarting, audioStarting), (coreActive, audioActive),
            (coreStartingDrain, audioStartingDrain), (coreActiveDrain, audioActiveDrain),
        };
        var intents = Enum.GetValues<SessionTerminalIntentWireV1>().Where(static value => value != SessionTerminalIntentWireV1.None);
        var causes = Enum.GetValues<SessionTerminalCauseWireV1>().Where(static value => value != SessionTerminalCauseWireV1.None);
        var severities = Enum.GetValues<SessionTerminalSeverityWireV1>().Where(static value => value != SessionTerminalSeverityWireV1.None);
        var phases = Enum.GetValues<SessionConvergencePhaseWireV1>().Where(static value => value != SessionConvergencePhaseWireV1.None);
        var checkedBegins = 0;
        foreach (var (coreSource, audioSource) in sources)
        foreach (var intent in intents)
        foreach (var cause in causes)
        foreach (var severity in severities)
        foreach (var phase in phases)
        {
            var coreCommand = new SessionLifecycleCommandBodyV1.BeginTermination(
                Operation(), Position(), intent, cause, severity, phase);
            var audioCommand = new LiveAudioLifecycleCommandV1.BeginTermination(
                (LiveAudioTerminalIntentV1)intent, (LiveAudioTerminalCauseV1)cause,
                (LiveAudioTerminalSeverityV1)severity, (LiveAudioConvergencePhaseV1)phase);
            if (coreSource.State == SessionLifecycleStateWireV1.Draining &&
                phase < SessionConvergencePhaseWireV1.Draining)
            {
                Assert.IsType<SessionLifecycleReductionV1.Rejected>(SessionLifecycleReducerV1.Apply(coreSource, coreCommand));
                Assert.IsType<LiveAudioLifecycleTransitionV1.Applied>(LiveAudioSessionStateMachineV1.Apply(audioSource, audioCommand));
            }
            else
                AssertReduction(SessionLifecycleReducerV1.Apply(coreSource, coreCommand),
                    LiveAudioSessionStateMachineV1.Apply(audioSource, audioCommand));
            checkedBegins++;
        }
        Assert.Equal(3_360, checkedBegins);

        var checkedAdvances = 0;
        foreach (var initialIntent in intents)
        foreach (var initialCause in causes)
        foreach (var initialSeverity in severities)
        foreach (var initialPhase in phases)
        {
            var coreTerminal = CoreApplied(coreStarting, new SessionLifecycleCommandBodyV1.BeginTermination(
                Operation(), Position(), initialIntent, initialCause, initialSeverity, initialPhase));
            var audioTerminal = AudioApplied(audioStarting, new LiveAudioLifecycleCommandV1.BeginTermination(
                (LiveAudioTerminalIntentV1)initialIntent, (LiveAudioTerminalCauseV1)initialCause,
                (LiveAudioTerminalSeverityV1)initialSeverity, (LiveAudioConvergencePhaseV1)initialPhase));
            foreach (var nextIntent in intents)
            foreach (var nextCause in causes)
            foreach (var nextSeverity in severities)
            foreach (var nextPhase in phases)
            foreach (var stopped in new[] { false, true })
            {
                AssertReduction(SessionLifecycleReducerV1.Apply(coreTerminal,
                        new SessionLifecycleCommandBodyV1.AdvanceTermination(
                            Operation(), Position(), nextPhase, nextIntent, nextCause, nextSeverity, stopped)),
                    LiveAudioSessionStateMachineV1.Apply(audioTerminal,
                        new LiveAudioLifecycleCommandV1.AdvanceTermination(
                            (LiveAudioConvergencePhaseV1)nextPhase, (LiveAudioTerminalIntentV1)nextIntent,
                            (LiveAudioTerminalCauseV1)nextCause, (LiveAudioTerminalSeverityV1)nextSeverity, stopped)));
                checkedAdvances++;
            }
            foreach (var stopped in new[] { false, true })
                AssertReduction(SessionLifecycleReducerV1.Apply(coreTerminal, Complete(stopped)),
                    LiveAudioSessionStateMachineV1.Apply(audioTerminal,
                        new LiveAudioLifecycleCommandV1.Complete(stopped)));
        }
        Assert.Equal(1_411_200, checkedAdvances);
    }

    [Fact]
    public void CoreIntentionallyRejectsFormerDrainingToQuiescingRegression()
    {
        var coreDraining = CoreApplied(CoreStarting(), Drain());
        var audioDraining = AudioApplied(LiveAudioSessionStateMachineV1.Initial,
            new LiveAudioLifecycleCommandV1.BeginDrain());
        var core = Assert.IsType<SessionLifecycleReductionV1.Rejected>(SessionLifecycleReducerV1.Apply(
            coreDraining, new SessionLifecycleCommandBodyV1.BeginTermination(
                Operation(), Position(), SessionTerminalIntentWireV1.Fault,
                SessionTerminalCauseWireV1.ParticipantFault, SessionTerminalSeverityWireV1.Recoverable,
                SessionConvergencePhaseWireV1.Quiescing)));
        var audio = Assert.IsType<LiveAudioLifecycleTransitionV1.Applied>(LiveAudioSessionStateMachineV1.Apply(
            audioDraining, new LiveAudioLifecycleCommandV1.BeginTermination(
                LiveAudioTerminalIntentV1.Fault, LiveAudioTerminalCauseV1.ParticipantFault,
                LiveAudioTerminalSeverityV1.Recoverable, LiveAudioConvergencePhaseV1.Quiescing)));
        Assert.Equal("terminal-regression", core.SafeCode.ToString());
        Assert.Equal(LiveAudioConvergencePhaseV1.Quiescing, audio.Snapshot.ConvergencePhase);
    }

    [Theory]
    [InlineData((ushort)SessionConvergencePhaseWireV1.Reporting, false)]
    [InlineData((ushort)SessionConvergencePhaseWireV1.Reporting, true)]
    [InlineData((ushort)SessionConvergencePhaseWireV1.Containing, false)]
    [InlineData((ushort)SessionConvergencePhaseWireV1.Containing, true)]
    public void CompletionAfterEscalation_PreservesEstablishingAndCurrentTerminalAxes(
        ushort phaseValue,
        bool alreadyStopped)
    {
        var phase = (SessionConvergencePhaseWireV1)phaseValue;
        var coreInitial = CoreApplied(CoreStarting(), new SessionLifecycleCommandBodyV1.BeginTermination(
            Operation(), Position(), SessionTerminalIntentWireV1.GracefulStop,
            SessionTerminalCauseWireV1.Requested, SessionTerminalSeverityWireV1.Informational,
            SessionConvergencePhaseWireV1.Fencing));
        var audioInitial = AudioApplied(LiveAudioSessionStateMachineV1.Initial,
            new LiveAudioLifecycleCommandV1.BeginTermination(
                LiveAudioTerminalIntentV1.GracefulStop, LiveAudioTerminalCauseV1.Requested,
                LiveAudioTerminalSeverityV1.Informational, LiveAudioConvergencePhaseV1.Fencing));
        var coreAdvanced = CoreApplied(coreInitial, new SessionLifecycleCommandBodyV1.AdvanceTermination(
            Operation(), Position(), phase, SessionTerminalIntentWireV1.DeadlineContainment,
            SessionTerminalCauseWireV1.HostForced, SessionTerminalSeverityWireV1.Fatal, alreadyStopped));
        var audioAdvanced = AudioApplied(audioInitial, new LiveAudioLifecycleCommandV1.AdvanceTermination(
            (LiveAudioConvergencePhaseV1)phase, LiveAudioTerminalIntentV1.DeadlineContainment,
            LiveAudioTerminalCauseV1.HostForced, LiveAudioTerminalSeverityV1.Fatal, alreadyStopped));

        foreach (var completionStopped in new[] { false, true })
            AssertReduction(SessionLifecycleReducerV1.Apply(coreAdvanced, Complete(completionStopped)),
                LiveAudioSessionStateMachineV1.Apply(audioAdvanced,
                    new LiveAudioLifecycleCommandV1.Complete(completionStopped)));
    }

    private static void AssertReduction(SessionLifecycleReductionV1 core, LiveAudioLifecycleTransitionV1 audio)
    {
        switch (core)
        {
            case SessionLifecycleReductionV1.Applied applied:
                AssertSnapshot(applied.Snapshot, Assert.IsType<LiveAudioLifecycleTransitionV1.Applied>(audio).Snapshot);
                break;
            case SessionLifecycleReductionV1.Idempotent idempotent:
                AssertSnapshot(idempotent.Snapshot, Assert.IsType<LiveAudioLifecycleTransitionV1.Idempotent>(audio).Snapshot);
                break;
            case SessionLifecycleReductionV1.Rejected rejected:
                var audioRejected = Assert.IsType<LiveAudioLifecycleTransitionV1.Rejected>(audio);
                Assert.Equal(rejected.SafeCode.ToString(), audioRejected.SafeCode);
                AssertSnapshot(rejected.Snapshot, audioRejected.Snapshot);
                break;
            default:
                throw new InvalidOperationException("The valid-state matrix cannot produce missing-predecessor quarantine.");
        }
    }

    private static void AssertSnapshot(SessionLifecycleSnapshotBodyV1 core, LiveAudioLifecycleSnapshotV1 audio)
    {
        Assert.Equal((ushort)core.State, (ushort)audio.State);
        Assert.Equal((ushort)core.Admission, (ushort)audio.Admission);
        Assert.Equal((ushort)core.Availability, (ushort)audio.Availability);
        Assert.Equal((ushort)core.Readiness, (ushort)audio.Readiness);
        Assert.Equal((ushort)core.EstablishingTerminalIntent, (ushort)audio.EstablishingTerminalIntent);
        Assert.Equal((ushort)core.EstablishingTerminalCause, (ushort)audio.EstablishingTerminalCause);
        Assert.Equal((ushort)core.CurrentTerminalIntent, (ushort)audio.CurrentTerminalIntent);
        Assert.Equal((ushort)core.CurrentTerminalCause, (ushort)audio.CurrentTerminalCause);
        Assert.Equal((ushort)core.TerminalSeverity, (ushort)audio.TerminalSeverity);
        Assert.Equal((ushort)core.ConvergencePhase, (ushort)audio.ConvergencePhase);
        Assert.Equal((ushort)core.MutationFence, (ushort)audio.MutationFence);
        Assert.Equal(core.ConversationStopped, audio.ConversationStopped);
    }

    private static void Equal<TCore, TAudio>() where TCore : struct, Enum where TAudio : struct, Enum
    {
        var core = Enum.GetValues<TCore>().Select(static value => (Name: value.ToString(), Value: Convert.ToUInt16(value))).ToArray();
        var audio = Enum.GetValues<TAudio>().Select(static value => (Name: value.ToString(), Value: Convert.ToUInt16(value))).ToArray();
        Assert.Equal(core, audio);
    }

    private static SessionLifecycleSnapshotBodyV1 CoreStarting() =>
        Assert.IsType<SessionLifecycleReductionV1.Applied>(SessionLifecycleReducerV1.Apply(
            null, new SessionLifecycleCommandBodyV1.ReserveStarting(Operation(), Hash256.Compute("request"u8)))).Snapshot;
    private static SessionLifecycleSnapshotBodyV1 CoreApplied(SessionLifecycleSnapshotBodyV1 state, SessionLifecycleCommandBodyV1 command) =>
        Assert.IsType<SessionLifecycleReductionV1.Applied>(SessionLifecycleReducerV1.Apply(state, command)).Snapshot;
    private static LiveAudioLifecycleSnapshotV1 AudioApplied(LiveAudioLifecycleSnapshotV1 state, LiveAudioLifecycleCommandV1 command) =>
        Assert.IsType<LiveAudioLifecycleTransitionV1.Applied>(LiveAudioSessionStateMachineV1.Apply(state, command)).Snapshot;
    private static SessionLifecycleCommandBodyV1.PublishReady Ready() => new(Operation(), Position(), SessionAvailabilityWireV1.Available);
    private static SessionLifecycleCommandBodyV1.BeginDrain Drain() => new(Operation(), Position());
    private static SessionLifecycleCommandBodyV1.BeginTermination CoreFault() => new(
        Operation(), Position(), SessionTerminalIntentWireV1.Fault, SessionTerminalCauseWireV1.ParticipantFault,
        SessionTerminalSeverityWireV1.Recoverable, SessionConvergencePhaseWireV1.Fencing);
    private static LiveAudioLifecycleCommandV1.BeginTermination AudioFault() => new(
        LiveAudioTerminalIntentV1.Fault, LiveAudioTerminalCauseV1.ParticipantFault,
        LiveAudioTerminalSeverityV1.Recoverable, LiveAudioConvergencePhaseV1.Fencing);
    private static SessionLifecycleCommandBodyV1.AdvanceTermination Advance() => new(
        Operation(), Position(), SessionConvergencePhaseWireV1.Finalizing, SessionTerminalIntentWireV1.Fault,
        SessionTerminalCauseWireV1.ParticipantFault, SessionTerminalSeverityWireV1.Recoverable, false);
    private static SessionLifecycleCommandBodyV1.Complete Complete(bool stopped) => new(Operation(), Position(), stopped);
    private static OperationId Operation() => OperationId.Create();
    private static JournalPositionV1 Position() => new(
        new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()), 1);
}
