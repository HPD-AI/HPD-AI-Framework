using System.Runtime.CompilerServices;
using FluentAssertions;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Core;

public class AgentClientMiddlewareTests
{
    [Fact]
    public async Task ChatClientMiddleware_FirstRegisteredWrapperIsOutermost()
    {
        var calls = new List<string>();
        var fake = new FakeChatClient();
        fake.EnqueueTextResponse("ok");

        var agent = await new AgentBuilder(new AgentConfig
            {
                Name = "middleware-test",
                SystemInstructions = "You are terse."
            })
            .WithChatClient(fake)
            .UseChatClientMiddleware((client, _) => new RecordingChatClient(client, calls, "first"))
            .UseChatClientMiddleware((client, _) => new RecordingChatClient(client, calls, "second"))
            .BuildAsync();

        await agent.RunAsync("hello");

        calls.Should().Equal(
            "first:before",
            "second:before",
            "second:after",
            "first:after");
    }

    private sealed class RecordingChatClient(
        IChatClient inner,
        List<string> calls,
        string name) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            calls.Add($"{name}:before");
            var response = await inner.GetResponseAsync(chatMessages, options, cancellationToken)
                .ConfigureAwait(false);
            calls.Add($"{name}:after");
            return response;
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            calls.Add($"{name}:before");
            await foreach (var update in inner.GetStreamingResponseAsync(chatMessages, options, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return update;
            }
            calls.Add($"{name}:after");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            inner.GetService(serviceType, serviceKey);

        public void Dispose() => inner.Dispose();
    }
}
