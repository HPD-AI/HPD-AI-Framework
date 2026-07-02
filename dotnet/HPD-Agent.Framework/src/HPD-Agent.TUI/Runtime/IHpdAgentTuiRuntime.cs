using HPD.Agent;

namespace HPD.Agent.TUI.Runtime;

public interface IHpdAgentTuiRuntime
{
    Task<AgentTuiScopeResolution> ResolveInitialScopeAsync(
        AgentTuiRuntimeScope? requested,
        CancellationToken cancellationToken = default);

    Task<AgentTuiRuntimeScope> EnsureDurableScopeAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<AgentEvent> ObserveAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken = default);

    Task SubmitInputAsync(
        AgentTuiRuntimeScope scope,
        AgentInputEvent input,
        CancellationToken cancellationToken = default);

    Task InterruptAsync(
        AgentTuiRuntimeScope scope,
        string reason,
        CancellationToken cancellationToken = default);

    Task AnswerRequestAsync(
        AgentTuiRuntimeScope scope,
        AgentEvent response,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentEvent>> GetThreadEventsAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken = default);

    Task<AgentTuiThreadRun?> GetActiveRunAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken = default);
}

public readonly record struct AgentTuiScopeResolution(
    AgentTuiRuntimeScope Scope,
    bool IsDurable);
