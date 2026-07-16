using HPD.Agent;

namespace HPD.Agent.TUI.Runtime;

public sealed record AgentTuiThreadState(
    ThreadJournalCursor ObservedCursor,
    AgentTuiThreadRun? ActiveRun,
    IReadOnlyList<AgentEvent> PendingRequests);

public sealed record AgentTuiSubmitResult(AgentTuiThreadRun Run);

public sealed record AgentTuiEventBatch(
    IReadOnlyList<AgentEvent> Events,
    AgentTuiEventDeliveryMode DeliveryMode,
    ThreadJournalCursor InitialObservedCursor,
    ThreadJournalCursor FirstCursor,
    ThreadJournalCursor LastCursor);

public enum AgentTuiEventDeliveryMode
{
    Historical,
    CatchUp,
    Live
}

public enum AgentTuiInterruptStatus
{
    Accepted,
    AlreadyTerminal,
    NoActiveRun,
    ActiveRunMismatch
}

public sealed record AgentTuiInterruptResult(
    AgentTuiInterruptStatus Status,
    AgentTuiThreadRun? ActiveRun = null);
