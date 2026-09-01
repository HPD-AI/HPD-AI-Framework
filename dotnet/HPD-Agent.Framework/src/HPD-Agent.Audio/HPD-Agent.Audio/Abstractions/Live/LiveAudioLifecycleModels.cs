namespace HPD.Agent.Audio;

/// <summary>Identifies the five durable lifecycle states of one live Audio session.</summary>
public enum LiveAudioSessionStateV1 : ushort
{
    /// <summary>The session is visible but has not published readiness.</summary>
    Starting = 1,
    /// <summary>The session is ready and admits promised operations.</summary>
    Active = 2,
    /// <summary>The session rejects new admission while settling admitted work.</summary>
    Draining = 3,
    /// <summary>The session is monotonically converging after a terminal command or cause.</summary>
    Terminating = 4,
    /// <summary>The immutable terminal lifecycle state.</summary>
    Completed = 5,
}

/// <summary>Reports whether the session accepts new operations.</summary>
public enum LiveAudioAdmissionStateV1 : ushort
{
    /// <summary>New operations may be admitted subject to their own guards.</summary>
    Open = 1,
    /// <summary>No new operation may be admitted.</summary>
    Closed = 2,
}

/// <summary>Reports the session's current service availability without changing lifecycle authority.</summary>
public enum LiveAudioAvailabilityV1 : ushort
{
    /// <summary>The session cannot currently provide its promised service.</summary>
    Unavailable = 1,
    /// <summary>The promised service is currently available.</summary>
    Available = 2,
    /// <summary>The service is intentionally suspended.</summary>
    Suspended = 3,
    /// <summary>The service is reconnecting under the same admitted session authority.</summary>
    Reconnecting = 4,
    /// <summary>The service remains usable with a declared degradation.</summary>
    Degraded = 5,
}

/// <summary>Reports whether session readiness has been published.</summary>
public enum LiveAudioReadinessV1 : ushort
{
    /// <summary>No readiness outcome has been published.</summary>
    Unpublished = 1,
    /// <summary>All required readiness gates succeeded atomically.</summary>
    Succeeded = 2,
    /// <summary>A required readiness gate failed.</summary>
    Failed = 3,
}

/// <summary>Identifies the accepted terminal intent independently of lifecycle phase.</summary>
public enum LiveAudioTerminalIntentV1 : ushort
{
    /// <summary>No terminal intent has been accepted.</summary>
    None = 0,
    /// <summary>A graceful stop was accepted.</summary>
    GracefulStop = 1,
    /// <summary>A fault requires terminal convergence.</summary>
    Fault = 2,
    /// <summary>An abort requires immediate bounded convergence.</summary>
    Abort = 3,
    /// <summary>The owning runtime is shutting down.</summary>
    RuntimeShutdown = 4,
    /// <summary>An absolute deadline requires containment.</summary>
    DeadlineContainment = 5,
}

/// <summary>Identifies the typed cause that established or escalated terminal intent.</summary>
public enum LiveAudioTerminalCauseV1 : ushort
{
    /// <summary>No terminal cause exists.</summary>
    None = 0,
    /// <summary>A caller requested graceful termination.</summary>
    Requested = 1,
    /// <summary>Required startup failed.</summary>
    StartFailed = 2,
    /// <summary>A required participant reported a terminal fault.</summary>
    ParticipantFault = 3,
    /// <summary>An absolute deadline expired.</summary>
    DeadlineExpired = 4,
    /// <summary>The owning runtime began shutdown.</summary>
    RuntimeStopping = 5,
    /// <summary>The host forced containment.</summary>
    HostForced = 6,
    /// <summary>A policy revision revoked continued operation.</summary>
    PolicyRevoked = 7,
}

/// <summary>Reports the monotone severity of the terminal cause.</summary>
public enum LiveAudioTerminalSeverityV1 : ushort
{
    /// <summary>No terminal severity exists.</summary>
    None = 0,
    /// <summary>The terminal path is expected and informational.</summary>
    Informational = 1,
    /// <summary>The terminal path follows a contained recoverable failure.</summary>
    Recoverable = 2,
    /// <summary>The terminal path follows a fatal or forced condition.</summary>
    Fatal = 3,
}

/// <summary>Identifies the current monotone convergence phase.</summary>
public enum LiveAudioConvergencePhaseV1 : ushort
{
    /// <summary>No convergence phase has begun.</summary>
    None = 0,
    /// <summary>New local work is being quiesced.</summary>
    Quiescing = 1,
    /// <summary>Already admitted work is draining.</summary>
    Draining = 2,
    /// <summary>Mutation authority is being fenced.</summary>
    Fencing = 3,
    /// <summary>Participants are stopping.</summary>
    Stopping = 4,
    /// <summary>Owned resources are being disposed.</summary>
    Disposing = 5,
    /// <summary>Required finalizers are settling.</summary>
    Finalizing = 6,
    /// <summary>Immutable terminal results are being reported.</summary>
    Reporting = 7,
    /// <summary>Residual resources are being contained for bounded reaping.</summary>
    Containing = 8,
}

/// <summary>Reports whether mutation authority remains open.</summary>
public enum LiveAudioMutationFenceV1 : ushort
{
    /// <summary>Mutation authority remains open subject to ordinary guards.</summary>
    Open = 1,
    /// <summary>Mutation authority is irreversibly fenced.</summary>
    Fenced = 2,
}

/// <summary>Contains the immutable folded lifecycle truth for one live Audio session.</summary>
public sealed record LiveAudioLifecycleSnapshotV1
{
    internal LiveAudioLifecycleSnapshotV1(
        LiveAudioSessionStateV1 state,
        LiveAudioAdmissionStateV1 admission,
        LiveAudioAvailabilityV1 availability,
        LiveAudioReadinessV1 readiness,
        LiveAudioTerminalIntentV1 establishingTerminalIntent,
        LiveAudioTerminalCauseV1 establishingTerminalCause,
        LiveAudioTerminalIntentV1 currentTerminalIntent,
        LiveAudioTerminalCauseV1 currentTerminalCause,
        LiveAudioTerminalSeverityV1 terminalSeverity,
        LiveAudioConvergencePhaseV1 convergencePhase,
        LiveAudioMutationFenceV1 mutationFence,
        bool conversationStopped)
    {
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

    /// <summary>Gets the five-state lifecycle axis.</summary>
    public LiveAudioSessionStateV1 State { get; }
    /// <summary>Gets the independent operation-admission state.</summary>
    public LiveAudioAdmissionStateV1 Admission { get; }
    /// <summary>Gets the independent service availability.</summary>
    public LiveAudioAvailabilityV1 Availability { get; }
    /// <summary>Gets the session-readiness outcome.</summary>
    public LiveAudioReadinessV1 Readiness { get; }
    /// <summary>Gets the intent that first established terminal convergence.</summary>
    public LiveAudioTerminalIntentV1 EstablishingTerminalIntent { get; }
    /// <summary>Gets the cause that first established terminal convergence.</summary>
    public LiveAudioTerminalCauseV1 EstablishingTerminalCause { get; }
    /// <summary>Gets the most recently accepted terminal intent without replacing the establishing intent.</summary>
    public LiveAudioTerminalIntentV1 CurrentTerminalIntent { get; }
    /// <summary>Gets the most recently accepted terminal cause without replacing the establishing cause.</summary>
    public LiveAudioTerminalCauseV1 CurrentTerminalCause { get; }
    /// <summary>Gets the monotone terminal severity.</summary>
    public LiveAudioTerminalSeverityV1 TerminalSeverity { get; }
    /// <summary>Gets the current convergence phase.</summary>
    public LiveAudioConvergencePhaseV1 ConvergencePhase { get; }
    /// <summary>Gets the irreversible mutation fence.</summary>
    public LiveAudioMutationFenceV1 MutationFence { get; }
    /// <summary>Gets whether the distinct conversation-stopped fact was published.</summary>
    public bool ConversationStopped { get; }
}
