using HPD.Agent.Serialization;

namespace HPD.Agent.Goals;

/// <summary>Committed Goal started lifecycle projection.</summary>
[DurableEvent]
public sealed record GoalStartedEvent(GoalData Goal, string Reason) : AgentEvent;

/// <summary>Committed Goal updated lifecycle projection.</summary>
[DurableEvent]
public sealed record GoalUpdatedEvent(GoalData Goal, string Reason) : AgentEvent;

/// <summary>Committed Goal paused lifecycle projection.</summary>
[DurableEvent]
public sealed record GoalPausedEvent(GoalData Goal, string Reason) : AgentEvent
{
    /// <summary>Gets the trusted cancellation that paused this execution, when applicable.</summary>
    public AgentInputCancellation? Cancellation { get; init; }
}

/// <summary>Committed Goal resumed lifecycle projection.</summary>
[DurableEvent]
public sealed record GoalResumedEvent(GoalData Goal, string Reason) : AgentEvent;

/// <summary>Committed Goal edited lifecycle projection.</summary>
[DurableEvent]
public sealed record GoalEditedEvent(GoalData Goal, string Reason) : AgentEvent;

/// <summary>Committed Goal cleared lifecycle projection.</summary>
[DurableEvent]
public sealed record GoalClearedEvent(GoalData Goal, string Reason) : AgentEvent;

/// <summary>Committed Goal continuation scheduled lifecycle projection.</summary>
[DurableEvent]
public sealed record GoalContinuationScheduledEvent(GoalData Goal, string Reason) : AgentEvent;

/// <summary>Committed Goal continuation started lifecycle projection.</summary>
[DurableEvent]
public sealed record GoalContinuationStartedEvent(GoalData Goal, string Reason) : AgentEvent;

/// <summary>Committed Goal continuation skipped lifecycle projection.</summary>
[DurableEvent]
public sealed record GoalContinuationSkippedEvent(GoalData Goal, string Reason) : AgentEvent;

/// <summary>Committed Goal progress accounted lifecycle projection.</summary>
[DurableEvent]
public sealed record GoalProgressAccountedEvent(GoalData Goal, string Reason) : AgentEvent;

/// <summary>Committed Goal completion proposed lifecycle projection.</summary>
[DurableEvent]
public sealed record GoalCompletionProposedEvent(GoalData Goal, string Reason) : AgentEvent;

/// <summary>Committed Goal completion rejected lifecycle projection.</summary>
[DurableEvent]
public sealed record GoalCompletionRejectedEvent(GoalData Goal, string Reason) : AgentEvent;

/// <summary>Committed Goal completed lifecycle projection.</summary>
[DurableEvent]
public sealed record GoalCompletedEvent(GoalData Goal, string Reason) : AgentEvent
{
    /// <summary>Gets the proposal accepted at successful terminal closure.</summary>
    public GoalCompletionProposal? AcceptedProposal { get; init; }
}

/// <summary>Committed Goal blocker reported lifecycle projection.</summary>
[DurableEvent]
public sealed record GoalBlockerReportedEvent(GoalData Goal, string Reason) : AgentEvent;

/// <summary>Committed Goal blocker rejected lifecycle projection.</summary>
[DurableEvent]
public sealed record GoalBlockerRejectedEvent(GoalData Goal, string Reason) : AgentEvent;

/// <summary>Committed Goal awaiting input lifecycle projection.</summary>
[DurableEvent]
public sealed record GoalAwaitingInputEvent(GoalData Goal, string Reason) : AgentEvent;

/// <summary>Committed Goal blocked lifecycle projection.</summary>
[DurableEvent]
public sealed record GoalBlockedEvent(GoalData Goal, string Reason) : AgentEvent;

/// <summary>Committed Goal usage limited lifecycle projection.</summary>
[DurableEvent]
public sealed record GoalUsageLimitedEvent(GoalData Goal, string Reason) : AgentEvent;

/// <summary>Committed Goal faulted lifecycle projection.</summary>
[DurableEvent]
public sealed record GoalFaultedEvent(GoalData Goal, string Reason) : AgentEvent;

