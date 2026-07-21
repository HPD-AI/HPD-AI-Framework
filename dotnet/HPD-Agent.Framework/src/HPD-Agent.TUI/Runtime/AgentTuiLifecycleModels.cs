using HPD.Agent;

namespace HPD.Agent.TUI.Runtime;

public sealed record AgentTuiThreadState(
    ThreadJournalCursor ObservedCursor,
    AgentTuiThreadExecution? ActiveExecution,
    IReadOnlyList<AgentEvent> PendingRequests);

public sealed record AgentTuiSubmitResult(AgentTuiThreadExecution Execution);

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
    NoActiveExecution,
    ActiveExecutionMismatch
}

public sealed record AgentTuiInterruptResult(
    AgentTuiInterruptStatus Status,
    AgentTuiThreadExecution? ActiveExecution = null);
