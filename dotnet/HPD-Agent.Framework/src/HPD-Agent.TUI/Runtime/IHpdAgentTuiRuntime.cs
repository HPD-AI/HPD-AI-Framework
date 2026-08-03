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
        ThreadJournalCursor after,
        ThreadJournalCursor initialObservedCursor,
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

    Task<AgentRespondResult> AnswerRequestAsync(
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
