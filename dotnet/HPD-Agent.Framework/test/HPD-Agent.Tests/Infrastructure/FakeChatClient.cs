using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace HPD.Agent.Tests.Infrastructure;

/// <summary>
/// Fake chat client for testing that allows queuing predefined responses.
/// Simulates streaming behavior and tool calls without actual LLM communication.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    private readonly Queue<QueuedResponse> _queuedResponses = new();
    private readonly List<IList<ChatMessage>> _capturedRequests = new();
    private readonly List<ChatRequestSnapshot> _capturedRequestSnapshots = new();
    private ChatClientMetadata? _metadata;

    public ChatClientMetadata Metadata => _metadata ?? new ChatClientMetadata(
        providerName: "FakeChatClient",
        providerUri: null,
        defaultModelId: "fake-model");

    /// <summary>
    /// Gets all captured request message histories.
    /// Useful for verifying what was sent to the LLM.
    /// </summary>
    public IReadOnlyList<IList<ChatMessage>> CapturedRequests => _capturedRequests.AsReadOnly();

    /// <summary>
    /// Immutable snapshots of the messages, tools, and instructions supplied to each model call.
    /// Unlike <see cref="CapturedRequests"/>, these snapshots retain the option values from the
    /// instant of invocation even when the caller later reuses and mutates its <see cref="ChatOptions"/>.
    /// </summary>
    public IReadOnlyList<ChatRequestSnapshot> CapturedRequestSnapshots => _capturedRequestSnapshots.AsReadOnly();

    /// <summary>
    /// Enqueues a simple text response.
    /// </summary>
    public void EnqueueTextResponse(string text, string? finishReason = "stop")
    {
        _queuedResponses.Enqueue(new QueuedResponse
        {
            Type = ResponseType.Text,
            Text = text,
            FinishReason = finishReason
        });
    }

    /// <summary>
    /// Enqueues a streaming text response (multiple chunks).
    /// Simulates token-by-token streaming.
    /// </summary>
    public void EnqueueStreamingResponse(params string[] textChunks)
    {
        _queuedResponses.Enqueue(new QueuedResponse
        {
            Type = ResponseType.StreamingText,
            TextChunks = textChunks.ToList(),
            FinishReason = "stop"
        });
    }

    /// <summary>
    /// Enqueues a response that streams reasoning before final text.
    /// </summary>
    public void EnqueueReasoningThenTextResponse(
        string reasoning,
        string text,
        string? protectedData = null)
    {
        _queuedResponses.Enqueue(new QueuedResponse
        {
            Type = ResponseType.ReasoningThenText,
            Reasoning = reasoning,
            ReasoningProtectedData = protectedData,
            Text = text,
            FinishReason = "stop"
        });
    }

    /// <summary>
    /// Enqueues a tool call response.
    /// </summary>
    public void EnqueueToolCall(
        string functionName,
        string callId,
        Dictionary<string, object?>? args = null,
        string? finishReason = "tool_calls")
    {
        _queuedResponses.Enqueue(new QueuedResponse
        {
            Type = ResponseType.ToolCall,
            FunctionName = functionName,
            CallId = callId,
            Arguments = args ?? new Dictionary<string, object?>(),
            FinishReason = finishReason
        });
    }

    /// <summary>
    /// Enqueues one assistant response containing multiple tool calls.
    /// </summary>
    public void EnqueueToolCalls(params (string FunctionName, string CallId, Dictionary<string, object?>? Arguments)[] calls)
    {
        _queuedResponses.Enqueue(new QueuedResponse
        {
            Type = ResponseType.MultiToolCalls,
            ToolCalls = calls
                .Select(call => new QueuedToolCall(
                    call.FunctionName,
                    call.CallId,
                    call.Arguments ?? new Dictionary<string, object?>()))
                .ToList(),
            FinishReason = "tool_calls"
        });
    }

    /// <summary>
    /// Enqueues a response with both text and tool calls.
    /// </summary>
    public void EnqueueTextWithToolCall(
        string text,
        string functionName,
        string callId,
        Dictionary<string, object?>? args = null)
    {
        _queuedResponses.Enqueue(new QueuedResponse
        {
            Type = ResponseType.TextWithToolCall,
            Text = text,
            FunctionName = functionName,
            CallId = callId,
            Arguments = args ?? new Dictionary<string, object?>(),
            FinishReason = "tool_calls"
        });
    }

    /// <summary>
    /// Clears all queued responses and captured requests.
    /// </summary>
    public void Clear()
    {
        _queuedResponses.Clear();
        _capturedRequests.Clear();
        _capturedRequestSnapshots.Clear();
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Capture the request
        _capturedRequests.Add(chatMessages.ToList());
        CaptureRequest(chatMessages, options);

        // Get next queued response
        if (!_queuedResponses.TryDequeue(out var response))
        {
            throw new InvalidOperationException(
                "No responses queued. Use EnqueueTextResponse() or EnqueueToolCall() before calling GetResponseAsync()");
        }

        // Simulate small delay
        await Task.Delay(10, cancellationToken);

        return response.Type switch
        {
            ResponseType.Text => CreateTextCompletion(response),
            ResponseType.ToolCall => CreateToolCallCompletion(response),
            ResponseType.MultiToolCalls => CreateMultiToolCallCompletion(response),
            ResponseType.TextWithToolCall => CreateTextWithToolCallCompletion(response),
            ResponseType.StreamingText => CreateTextCompletion(response with { Text = string.Join("", response.TextChunks) }),
            ResponseType.ReasoningThenText => CreateReasoningThenTextCompletion(response),
            _ => throw new InvalidOperationException($"Unknown response type: {response.Type}")
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Capture the request
        _capturedRequests.Add(chatMessages.ToList());
        CaptureRequest(chatMessages, options);

        // Get next queued response
        if (!_queuedResponses.TryDequeue(out var response))
        {
            throw new InvalidOperationException(
                "No responses queued. Use EnqueueTextResponse() or EnqueueToolCall() before calling GetStreamingResponseAsync()");
        }

        switch (response.Type)
        {
            case ResponseType.Text:
                // Stream as single chunk
                await Task.Delay(5, cancellationToken);
                yield return new ChatResponseUpdate
                {
                    Contents = [new TextContent(response.Text!)],
                    FinishReason = ChatFinishReason.Stop
                };
                break;

            case ResponseType.StreamingText:
                // Stream multiple chunks
                foreach (var chunk in response.TextChunks!)
                {
                    await Task.Delay(5, cancellationToken); // Simulate streaming delay
                    yield return new ChatResponseUpdate
                    {
                        Contents = [new TextContent(chunk)]
                    };
                }
                // Final update with finish reason
                yield return new ChatResponseUpdate
                {
                    FinishReason = ChatFinishReason.Stop
                };
                break;

            case ResponseType.ToolCall:
                // Stream tool call
                await Task.Delay(5, cancellationToken);
                yield return new ChatResponseUpdate
                {
                    Contents = [new FunctionCallContent(
                        response.CallId!,
                        response.FunctionName!,
                        response.Arguments)],
                    FinishReason = ChatFinishReason.ToolCalls
                };
                break;

            case ResponseType.MultiToolCalls:
                await Task.Delay(5, cancellationToken);
                yield return new ChatResponseUpdate
                {
                    Contents = response.ToolCalls!
                        .Select(call => (AIContent)new FunctionCallContent(
                            call.CallId,
                            call.FunctionName,
                            call.Arguments))
                        .ToList(),
                    FinishReason = ChatFinishReason.ToolCalls
                };
                break;

            case ResponseType.TextWithToolCall:
                // Stream text first
                await Task.Delay(5, cancellationToken);
                yield return new ChatResponseUpdate
                {
                    Contents = [new TextContent(response.Text!)]
                };

                // Then stream tool call
                await Task.Delay(5, cancellationToken);
                yield return new ChatResponseUpdate
                {
                    Contents = [new FunctionCallContent(
                        response.CallId!,
                        response.FunctionName!,
                        response.Arguments)],
                    FinishReason = ChatFinishReason.ToolCalls
                };
                break;

            case ResponseType.ReasoningThenText:
                await Task.Delay(5, cancellationToken);
                yield return new ChatResponseUpdate
                {
                    Contents = [CreateReasoningContent(response.Reasoning!, response.ReasoningProtectedData)]
                };

                await Task.Delay(5, cancellationToken);
                yield return new ChatResponseUpdate
                {
                    Contents = [new TextContent(response.Text!)],
                    FinishReason = ChatFinishReason.Stop
                };
                break;
        }
    }

    private void CaptureRequest(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        _capturedRequestSnapshots.Add(new ChatRequestSnapshot(
            messages.Select(message => message.Role.ToString()).ToArray(),
            options?.Tools?
                .OfType<AIFunction>()
                .Select(function => function.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToArray() ?? Array.Empty<string>(),
            options?.Instructions));
    }

    private static ChatResponse CreateTextCompletion(QueuedResponse response)
    {
        return new ChatResponse(
            [new ChatMessage(ChatRole.Assistant, response.Text!)]);
    }

    private static ChatResponse CreateToolCallCompletion(QueuedResponse response)
    {
        var functionCall = new FunctionCallContent(
            response.CallId!,
            response.FunctionName!,
            response.Arguments);

        return new ChatResponse(
            [new ChatMessage(ChatRole.Assistant, [functionCall])]);
    }

    private static ChatResponse CreateMultiToolCallCompletion(QueuedResponse response)
    {
        var contents = response.ToolCalls!
            .Select(call => (AIContent)new FunctionCallContent(
                call.CallId,
                call.FunctionName,
                call.Arguments))
            .ToList();

        return new ChatResponse(
            [new ChatMessage(ChatRole.Assistant, contents)]);
    }

    private static ChatResponse CreateTextWithToolCallCompletion(QueuedResponse response)
    {
        var contents = new List<AIContent>
        {
            new TextContent(response.Text!),
            new FunctionCallContent(
                response.CallId!,
                response.FunctionName!,
                response.Arguments)
        };

        return new ChatResponse(
            [new ChatMessage(ChatRole.Assistant, contents)]);
    }

    private static ChatResponse CreateReasoningThenTextCompletion(QueuedResponse response)
    {
        var contents = new List<AIContent>
        {
            CreateReasoningContent(response.Reasoning!, response.ReasoningProtectedData),
            new TextContent(response.Text!)
        };

        return new ChatResponse(
            [new ChatMessage(ChatRole.Assistant, contents)]);
    }

    private static TextReasoningContent CreateReasoningContent(string text, string? protectedData)
    {
        return new TextReasoningContent(text)
        {
            ProtectedData = protectedData
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    public void Dispose()
    {
        // No resources to dispose
    }

    private enum ResponseType
    {
        Text,
        StreamingText,
        ToolCall,
        MultiToolCalls,
        TextWithToolCall,
        ReasoningThenText
    }

    private sealed record QueuedToolCall(
        string FunctionName,
        string CallId,
        Dictionary<string, object?> Arguments);

    private record QueuedResponse
    {
        public required ResponseType Type { get; init; }
        public string? Text { get; init; }
        public string? Reasoning { get; init; }
        public string? ReasoningProtectedData { get; init; }
        public List<string>? TextChunks { get; init; }
        public string? FunctionName { get; init; }
        public string? CallId { get; init; }
        public Dictionary<string, object?>? Arguments { get; init; }
        public List<QueuedToolCall>? ToolCalls { get; init; }
        public string? FinishReason { get; init; }
    }

    public sealed record ChatRequestSnapshot(
        IReadOnlyList<string> MessageRoles,
        IReadOnlyList<string> ToolNames,
        string? Instructions);
}
