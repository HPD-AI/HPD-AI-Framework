using Microsoft.Extensions.AI;

namespace HPD.RAG.IntegrationTests.Fakes;

internal sealed class FakeChatClient : IChatClient
{
    private readonly string _fixedResponse;

    public FakeChatClient(string response = "fake response")
    {
        _fixedResponse = response;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, _fixedResponse)]));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        throw new NotSupportedException("Streaming not supported by FakeChatClient.");
        // unreachable — satisfies the async iterator state machine requirement
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    public ChatClientMetadata Metadata => new("fake", null, null);

    public void Dispose() { }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public TService? GetService<TService>(object? serviceKey = null) where TService : class => null;
}
