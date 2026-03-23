using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace HPD.Agent.Hosting.Tests.Infrastructure;

/// <summary>
/// Fake chat client for testing that allows queuing predefined responses.
/// Simulates streaming behavior and tool calls without actual LLM communication.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    private readonly Queue<QueuedResponse> _queuedResponses = new();
    private readonly List<IList<ChatMessage>> _capturedRequests = new();
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
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Capture the request
        _capturedRequests.Add(chatMessages.ToList());

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
            ResponseType.TextWithToolCall => CreateTextWithToolCallCompletion(response),
            ResponseType.StreamingText => CreateTextCompletion(response with { Text = string.Join("", response.TextChunks) }),
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
        }
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
        TextWithToolCall
    }

    private record QueuedResponse
    {
        public required ResponseType Type { get; init; }
        public string? Text { get; init; }
        public List<string>? TextChunks { get; init; }
        public string? FunctionName { get; init; }
        public string? CallId { get; init; }
        public Dictionary<string, object?>? Arguments { get; init; }
        public string? FinishReason { get; init; }
    }
}
