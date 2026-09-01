// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Collections.Concurrent;
using System.Text.Json;
using HPD.Agent.Middleware;
using HPD.Agent.Evaluations.Tracing;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Evaluations.Integration;

internal sealed class EvalTurnCapture
{
    private readonly ConcurrentDictionary<string, CaptureState> _capturesByTraceId = new();
    private readonly ConcurrentDictionary<string, CaptureState> _capturesByMessageTurnId = new();
    private readonly AsyncLocal<CaptureState?> _current = new();

    public void Begin(BeforeMessageTurnContext context)
    {
        var evalData = EvalContext.Activate();
        var state = new CaptureState(new TurnEventBuffer(), evalData);
        state.Initialize(context);
        _current.Value = state;
        if (!string.IsNullOrWhiteSpace(context.TraceId))
            _capturesByTraceId[context.TraceId] = state;
        _capturesByMessageTurnId[context.MessageTurnId] = state;
    }

    public void Prepare(
        AfterMessageTurnContext context,
        Action<TurnEvaluationContext> completed,
        Action<Exception>? failed = null,
        Action<TurnEvaluationContext>? prepared = null)
    {
        ArgumentNullException.ThrowIfNull(completed);
        var state = Resolve(context.MessageTurnId, context.TraceId)
            ?? throw new InvalidOperationException("Evaluation capture was not activated.");
        try
        {
            var preparedContext = state.Prepare(context, completed, failed);
            prepared?.Invoke(preparedContext);
        }
        catch (Exception ex)
        {
            Remove(context.TraceId, state);
            failed?.Invoke(ex);
            throw;
        }
        TryComplete(context.TraceId, state);
    }

    public ValueTask HandleAsync(AgentEvent evt)
    {
        var state = evt switch
        {
            MessageTurnFinishedEvent terminal => Resolve(terminal.MessageTurnId, terminal.TraceId),
            MessageTurnErrorEvent error => Resolve(error.MessageTurnId, error.TraceId),
            _ => Resolve(evt.TraceId)
        };
        if (state is null)
            return ValueTask.CompletedTask;
        var buffer = state.Buffer;

        switch (evt)
        {
            case MessageTurnStartedEvent e:
                buffer.RecordTurnStarted(e.MessageTurnId, e.Timestamp);
                break;

            case MessageTurnFinishedEvent e:
                buffer.RecordTurnFinished(e.Duration);
                state.ObserveTerminal(e);
                break;

            case MessageTurnErrorEvent error:
                Fail(error.TraceId, error.MessageTurnId, error.Exception ?? new InvalidOperationException(error.ErrorMessage));
                return ValueTask.CompletedTask;

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

            case PermissionRequestEvent e:
                buffer.RecordPermissionRequest(e.PermissionId, e.CallId);
                break;

            case PermissionResponseEvent e:
                buffer.RecordPermissionResponse();
                break;

            case PermissionDeniedEvent e:
                buffer.RecordPermissionDenied(e.FunctionCallId);
                break;

            case AgentTurnCapabilitiesPinnedEvent e:
                buffer.RecordCapabilities(e.Identity);
                break;

            case AgentOperationRegisteredEvent e:
                buffer.RecordOperation(e.Operation);
                break;

            case AgentOperationTransitionedEvent e:
                buffer.RecordOperation(e.Operation);
                break;
        }

        TryComplete(evt.TraceId, state);

        return ValueTask.CompletedTask;
    }

    public void Fail(string? traceId, Exception error)
    {
        var state = Resolve(traceId);
        if (state is null)
            return;
        Remove(traceId, state);
        state.Fail(error)?.Invoke(error);
    }

    public void Fail(string? traceId, string? messageTurnId, Exception error)
    {
        var state = !string.IsNullOrWhiteSpace(messageTurnId) &&
                    _capturesByMessageTurnId.TryGetValue(messageTurnId, out var byTurn)
            ? byTurn
            : Resolve(traceId);
        if (state is null)
            return;
        Remove(traceId ?? state.TraceId, state);
        state.Fail(error)?.Invoke(error);
    }

    public void EndInputScope()
    {
        _current.Value = null;
        EvalContext.Deactivate();
    }

    private CaptureState? Resolve(string? traceId)
        => !string.IsNullOrWhiteSpace(traceId) && _capturesByTraceId.TryGetValue(traceId, out var state)
            ? state
            : _current.Value;

    private CaptureState? Resolve(string messageTurnId, string? traceId)
        => _capturesByMessageTurnId.TryGetValue(messageTurnId, out var state) &&
           (string.IsNullOrWhiteSpace(traceId) || string.Equals(traceId, state.TraceId, StringComparison.Ordinal))
            ? state
            : null;

    private void TryComplete(string? traceId, CaptureState state)
    {
        if (!state.TryComplete(out var context, out var completed, out var failure, out var failed))
            return;
        Remove(traceId, state);
        if (failure is not null)
            failed?.Invoke(failure);
        else
            completed!(context!);
    }

    private void Remove(string? traceId, CaptureState state)
    {
        if (!string.IsNullOrWhiteSpace(traceId))
            _capturesByTraceId.TryRemove(new KeyValuePair<string, CaptureState>(traceId!, state));
        _capturesByMessageTurnId.TryRemove(new KeyValuePair<string, CaptureState>(state.MessageTurnId, state));
        if (ReferenceEquals(_current.Value, state))
            _current.Value = null;
        EvalContext.Deactivate();
    }

    private sealed class CaptureState(TurnEventBuffer buffer, EvalContextData evalData)
    {
        private readonly object _gate = new();
        private TurnEvaluationContext? _prepared;
        private MessageTurnFinishedEvent? _terminal;
        private Action<TurnEvaluationContext>? _completed;
        private Action<Exception>? _failed;
        private bool _done;

        public TurnEventBuffer Buffer { get; } = buffer;
        public EvalContextData EvalData { get; } = evalData;
        public string MessageTurnId { get; private set; } = string.Empty;
        public string? TraceId { get; private set; }

        public void Initialize(BeforeMessageTurnContext context)
        {
            MessageTurnId = context.MessageTurnId;
            TraceId = context.TraceId;
        }

        public void ObserveTerminal(MessageTurnFinishedEvent terminal)
        {
            lock (_gate)
            {
                if (_done || _terminal is not null) return;
                _terminal = terminal;
            }
        }

        public TurnEvaluationContext Prepare(
            AfterMessageTurnContext context,
            Action<TurnEvaluationContext> completed,
            Action<Exception>? failed)
        {
            lock (_gate)
            {
                if (_done)
                    throw new InvalidOperationException("Evaluation capture is already complete.");
                try
                {
                    _prepared = Snapshot(TurnEvaluationContextBuilder.FromAfterMessageTurn(
                        context,
                        Buffer,
                        EvalData,
                        context.RunConfig.Get()?.ExecutionState.GroundTruth));
                }
                catch { throw; }
                _completed = completed;
                _failed = failed;
                return _prepared;
            }
        }

        public bool TryComplete(
            out TurnEvaluationContext? context,
            out Action<TurnEvaluationContext>? completed,
            out Exception? failure,
            out Action<Exception>? failed)
        {
            lock (_gate)
            {
                context = null;
                completed = null;
                failure = null;
                failed = null;
                if (_done || _terminal is null || _prepared is null || _completed is null)
                    return false;
                try
                {
                    context = WithTerminal(_prepared, _terminal);
                }
                catch (Exception ex)
                {
                    failure = ex;
                    failed = _failed;
                    _done = true;
                    return true;
                }
                completed = _completed;
                _done = true;
                return true;
            }
        }

        public Action<Exception>? Fail(Exception error)
        {
            lock (_gate)
            {
                if (_done) return null;
                _done = true;
                return _failed;
            }
        }

        private static TurnEvaluationContext Snapshot(TurnEvaluationContext source)
            => Copy(source, source.Duration, source.MessageTurnUsage);

        private static TurnEvaluationContext WithTerminal(TurnEvaluationContext source, MessageTurnFinishedEvent terminal)
            => Copy(source, terminal.Duration, terminal.Usage);

        private static TurnEvaluationContext Copy(
            TurnEvaluationContext source,
            TimeSpan duration,
            MessageTurnUsageSummary? terminalUsage) => new()
        {
            AgentName = source.AgentName,
            SessionId = source.SessionId,
            ThreadId = source.ThreadId,
            ConversationId = source.ConversationId,
            TurnIndex = source.TurnIndex,
            UserInput = source.UserInput,
            ConversationHistory = source.ConversationHistory.Select(CloneMessage).ToArray(),
            EvaluationMessages = source.EvaluationMessages.Select(CloneMessage).ToArray(),
            OutputText = source.OutputText,
            FinalResponse = new ChatResponse(source.FinalResponse.Messages.Select(CloneMessage).ToArray())
            {
                ModelId = source.FinalResponse.ModelId,
                FinishReason = source.FinalResponse.FinishReason,
                Usage = CloneUsage(source.FinalResponse.Usage),
                AdditionalProperties = source.FinalResponse.AdditionalProperties is null
                    ? null
                    : CloneProperties(source.FinalResponse.AdditionalProperties)
            },
            ReasoningText = source.ReasoningText,
            ToolCalls = source.ToolCalls.ToArray(),
            Trace = CloneTrace(source.Trace),
            TurnUsage = CloneUsage(source.TurnUsage),
            MessageTurnUsage = CloneTurnUsage(terminalUsage),
            IterationUsage = source.IterationUsage.Select(CloneUsage).ToArray(),
            IterationCount = source.IterationCount,
            Duration = duration,
            ModelId = source.ModelId,
            ResponseModelId = source.ResponseModelId,
            ProviderKey = source.ProviderKey,
            Attributes = source.Attributes.ToDictionary(
                pair => pair.Key,
                pair => CloneValue(pair.Value)!,
                StringComparer.Ordinal),
            Metrics = new Dictionary<string, double>(source.Metrics),
            StopKind = source.StopKind,
            GroundTruth = source.GroundTruth,
            ExperimentContext = source.ExperimentContext is null
                ? null
                : source.ExperimentContext.ToDictionary(
                    pair => pair.Key,
                    pair => CloneValue(pair.Value)!,
                    StringComparer.Ordinal)
        };

        private static ChatMessage CloneMessage(ChatMessage message) => new(message.Role, message.Contents.Select(CloneContent).ToArray())
        {
            MessageId = message.MessageId,
            AuthorName = message.AuthorName,
            CreatedAt = message.CreatedAt,
            AdditionalProperties = message.AdditionalProperties is null
                ? null
                : CloneProperties(message.AdditionalProperties)
        };

        private static AIContent CloneContent(AIContent content) => content switch
        {
            TextContent text => new TextContent(text.Text)
            {
                AdditionalProperties = text.AdditionalProperties is null ? null : CloneProperties(text.AdditionalProperties)
            },
            FunctionCallContent call => new FunctionCallContent(
                call.CallId,
                call.Name,
                call.Arguments is null ? null : (IDictionary<string, object?>)CloneValue(call.Arguments)!)
            {
                AdditionalProperties = call.AdditionalProperties is null ? null : CloneProperties(call.AdditionalProperties)
            },
            FunctionResultContent result => new FunctionResultContent(result.CallId, CloneValue(result.Result))
            {
                AdditionalProperties = result.AdditionalProperties is null ? null : CloneProperties(result.AdditionalProperties)
            },
            DataContent data => new DataContent(data.Data.ToArray(), data.MediaType)
            {
                Name = data.Name,
                AdditionalProperties = data.AdditionalProperties is null ? null : CloneProperties(data.AdditionalProperties)
            },
            UriContent uri => new UriContent(uri.Uri, uri.MediaType)
            {
                AdditionalProperties = uri.AdditionalProperties is null ? null : CloneProperties(uri.AdditionalProperties)
            },
            _ => throw new InvalidOperationException(
                $"Evaluation capture cannot snapshot unsupported mutable AI content '{content.GetType().FullName}'.")
        };

        private static UsageDetails? CloneUsage(UsageDetails? usage)
        {
            if (usage is null)
                return null;
            var clone = new UsageDetails();
            clone.Add(usage);
            return clone;
        }

        private static MessageTurnUsageSummary? CloneTurnUsage(MessageTurnUsageSummary? summary)
            => summary is null
                ? null
                : new MessageTurnUsageSummary(summary.Operations
                    .Select(operation => operation with { Usage = CloneUsage(operation.Usage) })
                    .ToArray());

        private static AdditionalPropertiesDictionary CloneProperties(IDictionary<string, object?> source)
            => new(source.ToDictionary(pair => pair.Key, pair => CloneValue(pair.Value), StringComparer.Ordinal));

        private static object? CloneValue(object? value) => value switch
        {
            null or string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal or DateTime or DateTimeOffset or TimeSpan or Guid or Enum => value,
            ToolCallRecord toolCall => toolCall with { },
            JsonElement json => json.Clone(),
            IDictionary<string, object?> dictionary => dictionary.ToDictionary(
                pair => pair.Key,
                pair => CloneValue(pair.Value),
                StringComparer.Ordinal),
            IReadOnlyDictionary<string, object> dictionary => dictionary.ToDictionary(
                pair => pair.Key,
                pair => CloneValue(pair.Value)!,
                StringComparer.Ordinal),
            IEnumerable<ToolCallRecord> toolCalls => toolCalls.Select(toolCall => toolCall with { }).ToArray(),
            System.Collections.IDictionary dictionary => dictionary.Keys.Cast<object>()
                .ToDictionary(key => CloneValue(key)!, key => CloneValue(dictionary[key])),
            System.Collections.IEnumerable sequence => sequence.Cast<object?>().Select(CloneValue).ToArray(),
            _ => throw new InvalidOperationException(
                $"Evaluation capture cannot snapshot unsupported mutable value '{value.GetType().FullName}'.")
        };

        private static TurnTrace CloneTrace(TurnTrace trace) => new()
        {
            MessageTurnId = trace.MessageTurnId,
            AgentName = trace.AgentName,
            StartedAt = trace.StartedAt,
            Duration = trace.Duration,
            CapabilityIdentity = trace.CapabilityIdentity,
            Operations = trace.Operations.Select(operation => operation with { }).ToArray(),
            Iterations = trace.Iterations.Select(iteration => new IterationSpan
            {
                IterationNumber = iteration.IterationNumber,
                Usage = CloneUsage(iteration.Usage),
                ToolCalls = iteration.ToolCalls.Select(call => new ToolCallSpan
                {
                    CallId = call.CallId,
                    Name = call.Name,
                    ToolHarnessName = call.ToolHarnessName,
                    ArgumentsJson = call.ArgumentsJson,
                    Result = call.Result,
                    Duration = call.Duration,
                    WasPermissionDenied = call.WasPermissionDenied
                }).ToArray(),
                AssistantText = iteration.AssistantText,
                ReasoningText = iteration.ReasoningText,
                FinishReason = iteration.FinishReason,
                Duration = iteration.Duration
            }).ToArray()
        };
    }
}
