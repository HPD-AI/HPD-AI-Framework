using System.Formats.Cbor;

namespace HPD.Agent.Authority;

internal enum SessionLifecycleCommandKindV1 : ushort
{
    ReserveStarting = 1,
    PublishReady = 2,
    BeginDrain = 3,
    BeginTermination = 4,
    AdvanceTermination = 5,
    Complete = 6,
}

internal enum SessionLifecycleOutcomeV1 : ushort
{
    Applied = 1,
    Idempotent = 2,
    Rejected = 3,
}

internal enum SessionLifecycleStateWireV1 : ushort
{
    Starting = 1,
    Active = 2,
    Draining = 3,
    Terminating = 4,
    Completed = 5,
}

internal enum SessionAdmissionWireV1 : ushort { Open = 1, Closed = 2 }
internal enum SessionAvailabilityWireV1 : ushort { Unavailable = 1, Available = 2, Suspended = 3, Reconnecting = 4, Degraded = 5 }
internal enum SessionReadinessWireV1 : ushort { Unpublished = 1, Succeeded = 2, Failed = 3 }
internal enum SessionTerminalIntentWireV1 : ushort { None = 0, GracefulStop = 1, Fault = 2, Abort = 3, RuntimeShutdown = 4, DeadlineContainment = 5 }
internal enum SessionTerminalCauseWireV1 : ushort { None = 0, Requested = 1, StartFailed = 2, ParticipantFault = 3, DeadlineExpired = 4, RuntimeStopping = 5, HostForced = 6, PolicyRevoked = 7 }
internal enum SessionTerminalSeverityWireV1 : ushort { None = 0, Informational = 1, Recoverable = 2, Fatal = 3 }
internal enum SessionConvergencePhaseWireV1 : ushort { None = 0, Quiescing = 1, Draining = 2, Fencing = 3, Stopping = 4, Disposing = 5, Finalizing = 6, Reporting = 7, Containing = 8 }
internal enum SessionMutationFenceWireV1 : ushort { Open = 1, Fenced = 2 }

internal abstract class SessionLifecycleCommandBodyV1
{
    private SessionLifecycleCommandBodyV1(
        SessionLifecycleCommandKindV1 kind,
        OperationId operationId,
        JournalPositionV1? expectedLifecycleFact)
    {
        if (!operationId.IsValid) throw new ArgumentException("A lifecycle operation identity is required.", nameof(operationId));
        if (expectedLifecycleFact is { IsValid: false }) throw new ArgumentException("A present lifecycle predecessor must be valid.", nameof(expectedLifecycleFact));
        Kind = kind;
        OperationId = operationId;
        ExpectedLifecycleFact = expectedLifecycleFact;
    }

    internal SessionLifecycleCommandKindV1 Kind { get; }
    internal OperationId OperationId { get; }
    internal JournalPositionV1? ExpectedLifecycleFact { get; }

    internal sealed class ReserveStarting : SessionLifecycleCommandBodyV1
    {
        internal ReserveStarting(OperationId operationId, Hash256 admissionFingerprint) :
            base(SessionLifecycleCommandKindV1.ReserveStarting, operationId, null)
        {
            Span<byte> bytes = stackalloc byte[32];
            if (!admissionFingerprint.TryWriteBytes(bytes)) throw new ArgumentException("An admission fingerprint is required.", nameof(admissionFingerprint));
            AdmissionFingerprint = admissionFingerprint;
        }

        internal Hash256 AdmissionFingerprint { get; }
    }

    internal sealed class PublishReady : SessionLifecycleCommandBodyV1
    {
        internal PublishReady(OperationId operationId, JournalPositionV1 expectedLifecycleFact, SessionAvailabilityWireV1 availability) :
            base(SessionLifecycleCommandKindV1.PublishReady, operationId, Require(expectedLifecycleFact))
        {
            if (availability != SessionAvailabilityWireV1.Available) throw new ArgumentException("Readiness requires Available.", nameof(availability));
            Availability = availability;
        }

        internal SessionAvailabilityWireV1 Availability { get; }
    }

    internal sealed class BeginDrain : SessionLifecycleCommandBodyV1
    {
        internal BeginDrain(OperationId operationId, JournalPositionV1 expectedLifecycleFact) :
            base(SessionLifecycleCommandKindV1.BeginDrain, operationId, Require(expectedLifecycleFact)) { }
    }

    internal sealed class BeginTermination : SessionLifecycleCommandBodyV1
    {
        internal BeginTermination(
            OperationId operationId,
            JournalPositionV1 expectedLifecycleFact,
            SessionTerminalIntentWireV1 intent,
            SessionTerminalCauseWireV1 cause,
            SessionTerminalSeverityWireV1 severity,
            SessionConvergencePhaseWireV1 phase) :
            base(SessionLifecycleCommandKindV1.BeginTermination, operationId, Require(expectedLifecycleFact))
        {
            ValidateTerminal(intent, cause, severity, phase);
            Intent = intent;
            Cause = cause;
            Severity = severity;
            Phase = phase;
        }

        internal SessionTerminalIntentWireV1 Intent { get; }
        internal SessionTerminalCauseWireV1 Cause { get; }
        internal SessionTerminalSeverityWireV1 Severity { get; }
        internal SessionConvergencePhaseWireV1 Phase { get; }
    }

    internal sealed class AdvanceTermination : SessionLifecycleCommandBodyV1
    {
        internal AdvanceTermination(
            OperationId operationId,
            JournalPositionV1 expectedLifecycleFact,
            SessionConvergencePhaseWireV1 phase,
            SessionTerminalIntentWireV1 intent,
            SessionTerminalCauseWireV1 cause,
            SessionTerminalSeverityWireV1 severity,
            bool conversationStopped) :
            base(SessionLifecycleCommandKindV1.AdvanceTermination, operationId, Require(expectedLifecycleFact))
        {
            ValidateTerminal(intent, cause, severity, phase);
            Phase = phase;
            Intent = intent;
            Cause = cause;
            Severity = severity;
            ConversationStopped = conversationStopped;
        }

        internal SessionConvergencePhaseWireV1 Phase { get; }
        internal SessionTerminalIntentWireV1 Intent { get; }
        internal SessionTerminalCauseWireV1 Cause { get; }
        internal SessionTerminalSeverityWireV1 Severity { get; }
        internal bool ConversationStopped { get; }
    }

    internal sealed class Complete : SessionLifecycleCommandBodyV1
    {
        internal Complete(OperationId operationId, JournalPositionV1 expectedLifecycleFact, bool conversationStopped) :
            base(SessionLifecycleCommandKindV1.Complete, operationId, Require(expectedLifecycleFact)) =>
            ConversationStopped = conversationStopped;

        internal bool ConversationStopped { get; }
    }

    private static JournalPositionV1 Require(JournalPositionV1 value) =>
        value.IsValid ? value : throw new ArgumentException("A lifecycle predecessor is required.", nameof(value));

    private static void ValidateTerminal(
        SessionTerminalIntentWireV1 intent,
        SessionTerminalCauseWireV1 cause,
        SessionTerminalSeverityWireV1 severity,
        SessionConvergencePhaseWireV1 phase)
    {
        if (!Enum.IsDefined(intent) || intent == SessionTerminalIntentWireV1.None ||
            !Enum.IsDefined(cause) || cause == SessionTerminalCauseWireV1.None ||
            !Enum.IsDefined(severity) || severity == SessionTerminalSeverityWireV1.None ||
            !Enum.IsDefined(phase) || phase == SessionConvergencePhaseWireV1.None)
            throw new ArgumentException("Terminal fields must be registered non-None values.");
    }
}

internal sealed record SessionLifecycleSnapshotBodyV1
{
    internal SessionLifecycleSnapshotBodyV1(
        SessionLifecycleStateWireV1 state,
        SessionAdmissionWireV1 admission,
        SessionAvailabilityWireV1 availability,
        SessionReadinessWireV1 readiness,
        SessionTerminalIntentWireV1 establishingTerminalIntent,
        SessionTerminalCauseWireV1 establishingTerminalCause,
        SessionTerminalIntentWireV1 currentTerminalIntent,
        SessionTerminalCauseWireV1 currentTerminalCause,
        SessionTerminalSeverityWireV1 terminalSeverity,
        SessionConvergencePhaseWireV1 convergencePhase,
        SessionMutationFenceWireV1 mutationFence,
        bool conversationStopped)
    {
        if (!Enum.IsDefined(state) || !Enum.IsDefined(admission) || !Enum.IsDefined(availability) || !Enum.IsDefined(readiness) ||
            !Enum.IsDefined(establishingTerminalIntent) || !Enum.IsDefined(establishingTerminalCause) ||
            !Enum.IsDefined(currentTerminalIntent) || !Enum.IsDefined(currentTerminalCause) ||
            !Enum.IsDefined(terminalSeverity) || !Enum.IsDefined(convergencePhase) || !Enum.IsDefined(mutationFence))
            throw new ArgumentException("A lifecycle snapshot contains an unregistered enum value.");
        State = state;
        Admission = admission;
        Availability = availability;
        Readiness = readiness;
        EstablishingTerminalIntent = establishingTerminalIntent;
        EstablishingTerminalCause = establishingTerminalCause;
        CurrentTerminalIntent = currentTerminalIntent;
        CurrentTerminalCause = currentTerminalCause;
        TerminalSeverity = terminalSeverity;
        ConvergencePhase = convergencePhase;
        MutationFence = mutationFence;
        ConversationStopped = conversationStopped;
    }

    internal SessionLifecycleStateWireV1 State { get; }
    internal SessionAdmissionWireV1 Admission { get; }
    internal SessionAvailabilityWireV1 Availability { get; }
    internal SessionReadinessWireV1 Readiness { get; }
    internal SessionTerminalIntentWireV1 EstablishingTerminalIntent { get; }
    internal SessionTerminalCauseWireV1 EstablishingTerminalCause { get; }
    internal SessionTerminalIntentWireV1 CurrentTerminalIntent { get; }
    internal SessionTerminalCauseWireV1 CurrentTerminalCause { get; }
    internal SessionTerminalSeverityWireV1 TerminalSeverity { get; }
    internal SessionConvergencePhaseWireV1 ConvergencePhase { get; }
    internal SessionMutationFenceWireV1 MutationFence { get; }
    internal bool ConversationStopped { get; }
}

internal sealed class SessionLifecycleFactBodyV1
{
    internal SessionLifecycleFactBodyV1(
        OperationId operationId,
        JournalPositionV1 commandPosition,
        JournalPositionV1? commandExpectedLifecycleFact,
        JournalPositionV1? previousLifecycleFact,
        SessionLifecycleOutcomeV1 outcome,
        SessionLifecycleSnapshotBodyV1 snapshot,
        BoundedAscii? safeCode)
    {
        if (!operationId.IsValid) throw new ArgumentException("A lifecycle operation identity is required.", nameof(operationId));
        if (!commandPosition.IsValid) throw new ArgumentException("A command position is required.", nameof(commandPosition));
        if (commandExpectedLifecycleFact is { IsValid: false } || previousLifecycleFact is { IsValid: false })
            throw new ArgumentException("A present lifecycle predecessor must be valid.");
        if (commandExpectedLifecycleFact is { } expected && expected.Session != commandPosition.Session ||
            previousLifecycleFact is { } previous && previous.Session != commandPosition.Session)
            throw new ArgumentException("Lifecycle positions must belong to the command session.");
        if (!Enum.IsDefined(outcome)) throw new ArgumentException("The lifecycle outcome is not registered.", nameof(outcome));
        ArgumentNullException.ThrowIfNull(snapshot);
        if (outcome == SessionLifecycleOutcomeV1.Rejected)
        {
            if (safeCode is not { IsValid: true } code || code.ToString().Length > 64)
                throw new ArgumentException("A rejected result requires a one-to-64-byte safe code.", nameof(safeCode));
        }
        else if (safeCode is not null)
            throw new ArgumentException("Applied and idempotent results cannot carry a safe code.", nameof(safeCode));
        OperationId = operationId;
        CommandPosition = commandPosition;
        CommandExpectedLifecycleFact = commandExpectedLifecycleFact;
        PreviousLifecycleFact = previousLifecycleFact;
        Outcome = outcome;
        Snapshot = snapshot;
        SafeCode = safeCode;
    }

    internal OperationId OperationId { get; }
    internal JournalPositionV1 CommandPosition { get; }
    internal JournalPositionV1? CommandExpectedLifecycleFact { get; }
    internal JournalPositionV1? PreviousLifecycleFact { get; }
    internal SessionLifecycleOutcomeV1 Outcome { get; }
    internal SessionLifecycleSnapshotBodyV1 Snapshot { get; }
    internal BoundedAscii? SafeCode { get; }
}
