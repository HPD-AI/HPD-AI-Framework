using Microsoft.Extensions.AI;
using System.Text.Json;

namespace HPD.Agent;

/// <summary>
/// Represents the completed result of an agent turn.
/// </summary>
public sealed record AgentTurnResult
{
    /// <summary>
    /// Gets an empty result for inputs that do not execute a complete inline turn.
    /// </summary>
    public static AgentTurnResult Empty { get; } = new();

    /// <summary>
    /// Gets the concatenated assistant text emitted by the turn.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Gets the events emitted by the turn.
    /// </summary>
    public IReadOnlyList<AgentEvent> Events { get; init; } = Array.Empty<AgentEvent>();

    /// <summary>
    /// Gets the tool calls completed during the turn.
    /// </summary>
    public IReadOnlyList<AgentToolCallResult> ToolCalls { get; init; } = Array.Empty<AgentToolCallResult>();

    /// <summary>
    /// Gets the turn start event, when available.
    /// </summary>
    public MessageTurnStartedEvent? Started { get; init; }

    /// <summary>
    /// Gets the turn completion event, when available.
    /// </summary>
    public MessageTurnFinishedEvent? Finished { get; init; }

    /// <summary>
    /// Gets usage details reported by the model turn, when available.
    /// </summary>
    public UsageDetails? Usage => Finished?.Usage;

    /// <summary>
    /// Gets the duration reported by the completed turn, when available.
    /// </summary>
    public TimeSpan? Duration => Finished?.Duration;
}

/// <summary>
/// Represents one tool call observed during an agent turn.
/// </summary>
public sealed record AgentToolCallResult
{
    /// <summary>
    /// Gets the provider call identifier.
    /// </summary>
    public string CallId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the tool name requested by the model.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the tool call argument JSON, when available.
    /// </summary>
    public string? ArgumentsJson { get; init; }

    /// <summary>
    /// Gets the tool result payload, when available.
    /// </summary>
    public ToolResultPayload? Result { get; init; }

    /// <summary>
    /// Gets a display-ready tool result text, when available.
    /// </summary>
    public string? Text => NormalizeResultText(Result?.Text);

    /// <summary>
    /// Gets the parent toolharness name, when available.
    /// </summary>
    public string? ToolHarnessName { get; init; }

    /// <summary>
    /// Gets the capability type behind the tool call, when available.
    /// </summary>
    public ToolCallType? CallType { get; init; }

    private static string? NormalizeResultText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.String)
            {
                return document.RootElement.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return text;
    }
}

internal sealed class AgentTurnResultBuilder
{
    private readonly List<AgentEvent> _events = new();
    private readonly List<string> _textMessageOrder = new();
    private readonly Dictionary<string, System.Text.StringBuilder> _textByMessageId = new();
    private readonly HashSet<string> _toolCallMessageIds = new();
    private readonly List<string> _toolCallOrder = new();
    private readonly Dictionary<string, ToolCallStartEvent> _toolCallStarts = new();
    private readonly Dictionary<string, string> _toolCallArgs = new();
    private readonly Dictionary<string, ToolCallResultEvent> _toolCallResults = new();
    private MessageTurnStartedEvent? _started;
    private MessageTurnFinishedEvent? _finished;

    public void Add(AgentEvent evt)
    {
        _events.Add(evt);

        switch (evt)
        {
            case TextDeltaEvent delta:
                if (!_textByMessageId.TryGetValue(delta.MessageId, out var text))
                {
                    text = new System.Text.StringBuilder();
                    _textByMessageId[delta.MessageId] = text;
                    _textMessageOrder.Add(delta.MessageId);
                }

                text.Append(delta.Text);
                break;

            case ToolCallStartEvent toolCall:
                _toolCallMessageIds.Add(toolCall.MessageId);
                if (!_toolCallStarts.ContainsKey(toolCall.CallId))
                {
                    _toolCallOrder.Add(toolCall.CallId);
                }

                _toolCallStarts[toolCall.CallId] = toolCall;
                break;

            case ToolCallArgsEvent args:
                _toolCallArgs[args.CallId] = args.ArgsJson;
                break;

            case ToolCallResultEvent result:
                _toolCallResults[result.CallId] = result;
                break;

            case MessageTurnStartedEvent started:
                _started ??= started;
                break;

            case MessageTurnFinishedEvent finished:
                _finished = finished;
                break;
        }
    }

    public AgentTurnResult Build() => new()
    {
        Text = string.Concat(_textMessageOrder
            .Where(messageId => !_toolCallMessageIds.Contains(messageId))
            .Select(messageId => _textByMessageId[messageId].ToString())),
        Events = _events.ToArray(),
        ToolCalls = _toolCallOrder
            .Where(callId => _toolCallStarts.ContainsKey(callId))
            .Select(callId =>
            {
                var start = _toolCallStarts[callId];
                _toolCallArgs.TryGetValue(callId, out var args);
                _toolCallResults.TryGetValue(callId, out var result);

                return new AgentToolCallResult
                {
                    CallId = callId,
                    Name = start.Name,
                    ArgumentsJson = args,
                    Result = result?.Result,
                    ToolHarnessName = result?.ToolHarnessName ?? start.ToolHarnessName,
                    CallType = result?.CallType ?? start.CallType
                };
            })
            .ToArray(),
        Started = _started,
        Finished = _finished
    };
}
