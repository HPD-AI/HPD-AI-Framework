using HPD.Agent;

namespace HPD.Agent.TUI.Runtime;

public interface IHpdAgentTuiRuntime
{
    Task<AgentTuiTargetResolution> ResolveInitialTargetAsync(
        AgentTuiExecutionTarget? requested,
        CancellationToken cancellationToken = default);

    Task<AgentTuiExecutionTarget> EnsureDurableTargetAsync(
        AgentTuiExecutionTarget target,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<AgentTuiEventBatch> ObserveAsync(
        AgentTuiExecutionTarget target,
        ThreadJournalCursor after,
        ThreadJournalCursor initialObservedCursor,
        CancellationToken cancellationToken = default);

    Task<AgentTuiSubmitResult> SubmitInputAsync(
        AgentTuiExecutionTarget target,
        AgentInputEvent input,
        CancellationToken cancellationToken = default);

    Task<AgentTuiSubmitResult> CancelExecutionAsync(
        AgentTuiRuntimeScope scope,
        string threadExecutionId,
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

public readonly record struct AgentTuiTargetResolution(
    AgentTuiExecutionTarget Target,
    bool IsDurable);
