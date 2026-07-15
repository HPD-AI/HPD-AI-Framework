using HPD.Agent;

namespace HPD.Agent.TUI.Runtime;

public sealed record AgentTuiThreadState(
    long LatestSequenceNumber,
    AgentTuiThreadRun? ActiveRun,
    IReadOnlyList<AgentEvent> Events);

public sealed record AgentTuiSubmitResult(AgentTuiThreadRun Run);

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
