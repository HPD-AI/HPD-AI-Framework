using HPD.Agent;

namespace HPD.Agent.TUI.Runtime;

public sealed record AgentTuiThreadState(
    ThreadJournalCursor ObservedCursor,
    AgentTuiThreadExecution? ActiveExecution,
    IReadOnlyList<AgentEvent> PendingRequests);

/// <summary>Describes admission of a TUI semantic input.</summary>
public sealed record AgentTuiSubmitResult(
    AgentInputDisposition Disposition,
    string? ThreadExecutionId = null,
    AgentTuiThreadExecution? ActiveExecution = null);

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
