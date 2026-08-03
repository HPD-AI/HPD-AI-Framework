// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent.Middleware;

namespace HPD.Agent.Evaluations.Integration;

internal sealed class BatchEvalCaptureMiddleware : IAgentMiddleware
{
    private readonly EvalTurnCapture _capture = new();
    private readonly TaskCompletionSource<TurnEvaluationContext> _captured =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<TurnEvaluationContext> Captured => _captured.Task;

    public Task BeforeMessageTurnAsync(
        BeforeMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        if (TryGetCaptureRequestId(context.RunConfig) is not null)
            _capture.Begin(context);

        return Task.CompletedTask;
    }

    public async Task AfterMessageTurnAsync(
        AfterMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        if (TryGetCaptureRequestId(context.RunConfig) is null)
            return;

        var turnCtx = await _capture.CompleteAsync(context, cancellationToken).ConfigureAwait(false);
        if (turnCtx is null)
            return;

        _captured.TrySetResult(turnCtx);
    }

    public ValueTask HandleAsync(AgentEvent evt)
        => _capture.HandleAsync(evt);

    private static string? TryGetCaptureRequestId(AgentRunConfig runConfig)
    {
        if (runConfig.Get()?.SuppressionReason == EvaluationSuppressionReason.JudgeCall)
            return null;

        return runConfig.Get()?.ExecutionState.CaptureRequestId;
    }
}
