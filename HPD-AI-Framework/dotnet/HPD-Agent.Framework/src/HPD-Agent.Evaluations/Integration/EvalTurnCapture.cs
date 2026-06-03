// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Collections.Concurrent;
using HPD.Agent.Middleware;
using HPD.Agent.Evaluations.Tracing;

namespace HPD.Agent.Evaluations.Integration;

internal sealed class EvalTurnCapture
{
    private readonly ConcurrentDictionary<string, TurnEventBuffer> _buffersByTraceId = new();
    private readonly AsyncLocal<TurnEventBuffer?> _buffer = new();
    private readonly AsyncLocal<EvalContextData?> _evalData = new();

    public void Begin(BeforeMessageTurnContext context)
    {
        var evalData = EvalContext.Activate();
        _evalData.Value = evalData;

        var buffer = new TurnEventBuffer();
        _buffer.Value = buffer;
        if (!string.IsNullOrWhiteSpace(context.TraceId))
        {
            _buffersByTraceId[context.TraceId] = buffer;
        }
    }

    public async Task<TurnEvaluationContext?> CompleteAsync(
        AfterMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        var traceId = context.TraceId;
        TurnEventBuffer? traceBuffer = null;
        var hasTraceBuffer = !string.IsNullOrWhiteSpace(traceId) &&
            _buffersByTraceId.TryGetValue(traceId!, out traceBuffer);
        var buffer = hasTraceBuffer
            ? traceBuffer!
            : _buffer.Value ?? new TurnEventBuffer();
        _buffer.Value = null;
        await WaitForObserverBufferAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (hasTraceBuffer)
        {
            _buffersByTraceId.TryRemove(traceId!, out _);
        }

        var evalData = _evalData.Value ?? new EvalContextData();
        EvalContext.Deactivate();
        _evalData.Value = null;

        var groundTruth = context.RunConfig.ContextOverrides?.TryGetValue("groundTruth", out var gt) == true
            ? gt?.ToString()
            : null;

        try
        {
            return TurnEvaluationContextBuilder.FromAfterMessageTurn(context, buffer, evalData, groundTruth);
        }
        catch
        {
            return null;
        }
    }

    public ValueTask HandleAsync(AgentEvent evt)
    {
        TurnEventBuffer? buffer = null;
        if (!string.IsNullOrWhiteSpace(evt.TraceId))
        {
            _buffersByTraceId.TryGetValue(evt.TraceId, out buffer);
        }

        buffer ??= _buffer.Value;
        if (buffer is null)
            return ValueTask.CompletedTask;

        switch (evt)
        {
            case MessageTurnStartedEvent e:
                buffer.RecordTurnStarted(e.MessageTurnId, e.Timestamp);
                break;

            case MessageTurnFinishedEvent e:
                buffer.RecordTurnFinished(e.Duration);
                break;

            case AgentTurnStartedEvent e:
                buffer.RecordIterationStarted(e.Iteration, e.Timestamp);
                break;

            case AgentTurnFinishedEvent e:
                buffer.RecordIterationFinished(e.Iteration, e.Timestamp);
                break;

            case ToolCallStartEvent e:
                buffer.RecordToolCallStarted(e.CallId, e.Name, e.ToolHarnessName, e.Timestamp);
                break;

            case ToolCallEndEvent e:
                buffer.RecordToolCallEnded(e.CallId, e.Timestamp);
                break;

            case PermissionDeniedEvent e:
                buffer.RecordPermissionDenied(e.CallId);
                break;
        }

        return ValueTask.CompletedTask;
    }

    private static async Task WaitForObserverBufferAsync(
        TurnEventBuffer buffer,
        CancellationToken cancellationToken)
    {
        if (buffer.HasTurnFinished)
            return;

        for (int i = 0; i < 10 && !buffer.HasTurnFinished; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }
}
