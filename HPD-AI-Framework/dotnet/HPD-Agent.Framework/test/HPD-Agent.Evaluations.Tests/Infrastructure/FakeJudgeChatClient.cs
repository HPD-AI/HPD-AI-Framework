// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Evaluations.Tests.Infrastructure;

/// <summary>
/// Fake IChatClient for testing LLM-as-judge evaluators.
/// Returns pre-queued text responses without any real LLM call.
/// Captures all requests for assertion.
/// </summary>
internal sealed class FakeJudgeChatClient : IChatClient
{
    private readonly Queue<string> _responses = new();
    private readonly List<IList<ChatMessage>> _capturedRequests = new();
    private bool _shouldThrow;
    private Exception? _exceptionToThrow;

    public ChatClientMetadata Metadata => new("FakeJudge", null, "fake-judge-model");

    public IReadOnlyList<IList<ChatMessage>> CapturedRequests => _capturedRequests.AsReadOnly();
    public int CallCount => _capturedRequests.Count;

    /// <summary>Queue a response text that will be returned for the next GetResponseAsync call.</summary>
    public void EnqueueResponse(string text) => _responses.Enqueue(text);

    /// <summary>Configure the client to throw on the next call.</summary>
    public void ThrowOn(Exception ex) { _shouldThrow = true; _exceptionToThrow = ex; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _capturedRequests.Add(chatMessages.ToList());

        if (_shouldThrow)
        {
            _shouldThrow = false;
            throw _exceptionToThrow!;
        }

        if (!_responses.TryDequeue(out var text))
            throw new InvalidOperationException(
                "FakeJudgeChatClient: no response queued. Call EnqueueResponse() first.");

        var response = new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(chatMessages, options, cancellationToken);
        yield return new ChatResponseUpdate { Contents = response.Messages[0].Contents };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
