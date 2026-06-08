using HPD.Agent;

namespace HPD.Agent.TUI.Runtime;

public interface IHpdAgentTuiRuntime
{
    Task<AgentTuiRuntimeScope> EnsureScopeAsync(
        AgentTuiRuntimeScope? requested,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<AgentEvent> ObserveAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken = default);

    Task SubmitInputAsync(
        AgentTuiRuntimeScope scope,
        AgentInputEvent input,
        CancellationToken cancellationToken = default);

    Task RespondAsync(
        AgentTuiRuntimeScope scope,
        AgentEvent response,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentEvent>> GetBranchEventsAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken = default);

    Task<AgentTuiBranchRun?> GetActiveRunAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken = default);
}
