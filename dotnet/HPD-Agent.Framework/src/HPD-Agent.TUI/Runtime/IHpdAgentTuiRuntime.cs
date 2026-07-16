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

    IAsyncEnumerable<AgentTuiEventBatch> ObserveAsync(
        AgentTuiRuntimeScope scope,
        long afterSequenceNumber,
        long initialObservedHead,
        CancellationToken cancellationToken = default);

    Task<AgentTuiSubmitResult> SubmitInputAsync(
        AgentTuiRuntimeScope scope,
        AgentInputEvent input,
        CancellationToken cancellationToken = default);

    Task<ThreadContextUsage> EstimateContextUsageAsync(
        AgentTuiRuntimeScope scope,
        AgentRunConfig? runConfig = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new ThreadContextUsage
        {
            SessionId = scope.SessionId,
            ThreadId = scope.ThreadId,
            Source = "runtime-not-supported"
        });

    Task<AgentTuiInterruptResult> InterruptAsync(
        AgentTuiRuntimeScope scope,
        string? expectedRuntimeRunId,
        string reason,
        CancellationToken cancellationToken = default);

    Task AnswerRequestAsync(
        AgentTuiRuntimeScope scope,
        AgentEvent response,
        CancellationToken cancellationToken = default);

    Task<AgentTuiThreadState> GetThreadStateAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken = default);
}

public readonly record struct AgentTuiScopeResolution(
    AgentTuiRuntimeScope Scope,
    bool IsDurable);
