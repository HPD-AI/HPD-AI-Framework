using HPD.Agent.Tests.Infrastructure;
using HPD.Agent.Providers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Core;

public sealed class DistributedCachingChatClientTests
{
    [Fact]
    public async Task HpdCache_SkipsProviderOwnedResponseState()
    {
        var storage = new InMemoryDistributedCache();
        var innerClient = new FakeChatClient();
        innerClient.EnqueueTextResponse("cached");
        innerClient.EnqueueTextResponse("background");
        innerClient.EnqueueTextResponse("continuation");
        using var client = new HpdDistributedCachingChatClient(innerClient, storage);
        ChatMessage[] messages = [new(ChatRole.User, "hello")];

        var first = await client.GetResponseAsync(messages);
        var cached = await client.GetResponseAsync(messages);

        Assert.Equal("cached", first.Text);
        Assert.Equal("cached", cached.Text);
        Assert.Single(innerClient.CapturedRequests);

        var background = await client.GetResponseAsync(
            messages,
            new ChatOptions { AllowBackgroundResponses = true });
        var continuation = await client.GetResponseAsync(
            messages,
            new ChatOptions
            {
                ContinuationToken = ResponseContinuationToken.FromBytes(new byte[] { 1, 2, 3 })
            });

        Assert.Equal("background", background.Text);
        Assert.Equal("continuation", continuation.Text);
        Assert.Equal(3, innerClient.CapturedRequests.Count);
    }

    [Fact]
    public async Task AgentBuilder_DistributedCaching_ReusesMatchingResponse()
    {
        var storage = new InMemoryDistributedCache();
        var innerClient = new FakeChatClient();
        innerClient.EnqueueTextResponse("cached");
        var config = new AgentConfig
        {
            Name = "caching-test",
            SystemInstructions = "Answer tersely."
        };
        config.EnsureChatClientConfig().Override = ClientOverride<IChatClient>.Borrow(
            innerClient,
            "test",
            "local",
            "test.chat");

        await using var agent = await new AgentBuilder(config)
            .WithEventComposition(TestEventApplication.Composition)
            .WithDistributedCaching(storage)
            .BuildAsync();

        await agent.RunAsync("hello");
        await agent.RunAsync("hello");

        Assert.Single(innerClient.CapturedRequests);
    }

    private sealed class InMemoryDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);

        public byte[]? Get(string key) =>
            _values.TryGetValue(key, out var value) ? value.ToArray() : null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Get(key));
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            _values[key] = value.ToArray();

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public void Remove(string key) =>
            _values.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            Remove(key);
            return Task.CompletedTask;
        }
    }
}
