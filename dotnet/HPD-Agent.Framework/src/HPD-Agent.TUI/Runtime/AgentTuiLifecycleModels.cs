using HPD.Agent;

namespace HPD.Agent.TUI.Runtime;

public sealed record AgentTuiThreadState(
    long ObservedHead,
    AgentTuiThreadRun? ActiveRun,
    IReadOnlyList<AgentEvent> PendingRequests);

public sealed record AgentTuiSubmitResult(AgentTuiThreadRun Run);

public sealed record AgentTuiEventBatch(
    IReadOnlyList<AgentEvent> Events,
    AgentTuiEventDeliveryMode DeliveryMode,
    long InitialObservedHead,
    long FirstThreadSequenceNumber,
    long LastThreadSequenceNumber);

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
