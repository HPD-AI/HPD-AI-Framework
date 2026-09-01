using Microsoft.Extensions.AI;
using HPD.Agent.Providers.OpenAI;

namespace HPD.Agent.Providers.Tests;

public sealed class OpenAICodexMessagePolicyChatClientTests
{
    [Fact]
    public void ApplyMessagePolicy_LowersOnlySystemMessagesToDeveloperInPlace()
    {
        var user = new ChatMessage(ChatRole.User, "before") { MessageId = "user-1" };
        var system = new ChatMessage(ChatRole.System, "notification") { MessageId = "system-1" };
        var assistant = new ChatMessage(ChatRole.Assistant, "after") { MessageId = "assistant-1" };

        var result = OpenAICodexMessagePolicyChatClient.ApplyMessagePolicy(
            [user, system, assistant]);

        Assert.Same(user, result[0]);
        Assert.Equal(new ChatRole("developer"), result[1].Role);
        Assert.Equal("notification", result[1].Text);
        Assert.Equal("system-1", result[1].MessageId);
        Assert.NotSame(system, result[1]);
        Assert.Equal(ChatRole.System, system.Role);
        Assert.Same(assistant, result[2]);
    }

    [Fact]
    public void ApplyMessagePolicy_RejectsNonTextPrivilegedContent()
    {
        var message = new ChatMessage(
            ChatRole.System,
            [new FunctionCallContent("call-1", "ReadFile", new Dictionary<string, object?>())]);

        var error = Assert.Throws<NotSupportedException>(() =>
            OpenAICodexMessagePolicyChatClient.ApplyMessagePolicy([message]));

        Assert.Contains("only text content", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetResponseAsync_PreservesOptionsAndCancellationWhileLoweringMessages()
    {
        var inner = new CaptureChatClient();
        using var client = new OpenAICodexMessagePolicyChatClient(inner);
        var options = new ChatOptions { Instructions = "stable instructions" };
        using var cancellation = new CancellationTokenSource();

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.System, "runtime context")],
            options,
            cancellation.Token);

        Assert.Same(options, inner.LastOptions);
        Assert.Equal(cancellation.Token, inner.LastCancellationToken);
        Assert.Equal(new ChatRole("developer"), Assert.Single(inner.LastMessages!).Role);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_UsesTheSameMessagePolicy()
    {
        var inner = new CaptureChatClient();
        using var client = new OpenAICodexMessagePolicyChatClient(inner);

        await foreach (var _ in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.System, "runtime context")]))
        {
        }

        Assert.Equal(new ChatRole("developer"), Assert.Single(inner.LastMessages!).Role);
    }

    private sealed class CaptureChatClient : IChatClient
    {
        public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }
        public ChatOptions? LastOptions { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public void Dispose()
        {
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToList();
            LastOptions = options;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToList();
            LastOptions = options;
            LastCancellationToken = cancellationToken;
            await Task.CompletedTask;
            yield break;
        }
    }
}
