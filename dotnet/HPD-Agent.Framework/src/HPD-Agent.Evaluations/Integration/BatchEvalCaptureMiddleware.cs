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

    public Task AfterMessageTurnAsync(
        AfterMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        if (TryGetCaptureRequestId(context.RunConfig) is null)
            return Task.CompletedTask;
        try
        {
            _capture.Prepare(
                context,
                turnCtx => _captured.TrySetResult(turnCtx),
                error => _captured.TrySetException(error));
        }
        catch (Exception ex)
        {
            _captured.TrySetException(ex);
        }
        return Task.CompletedTask;
    }

    public Task AfterInputAsync(AfterInputContext context, CancellationToken cancellationToken)
    {
        if (context.Result.Finished is null)
        {
            var traceId = context.Result.Started?.TraceId ?? context.Result.Events.FirstOrDefault()?.TraceId;
            var messageTurnId = context.Result.Started?.MessageTurnId ??
                context.Result.Events.OfType<MessageTurnStartedEvent>().FirstOrDefault()?.MessageTurnId;
            _capture.Fail(traceId, messageTurnId, context.Error ?? new OperationCanceledException("Evaluation input did not complete."));
        }
        _capture.EndInputScope();
        return Task.CompletedTask;
    }

    public async ValueTask HandleAsync(AgentEvent evt)
    {
        try
        {
            await _capture.HandleAsync(evt).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var messageTurnId = evt switch
            {
                MessageTurnFinishedEvent finished => finished.MessageTurnId,
                MessageTurnErrorEvent error => error.MessageTurnId,
                _ => null
            };
            _capture.Fail(evt.TraceId, messageTurnId, ex);
            _captured.TrySetException(ex);
        }
    }

    private static string? TryGetCaptureRequestId(AgentRunConfig runConfig)
    {
        if (runConfig.Get()?.SuppressionReason == EvaluationSuppressionReason.JudgeCall)
            return null;

        return runConfig.Get()?.ExecutionState.CaptureRequestId;
    }
}
