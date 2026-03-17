using FluentAssertions;
using HPD.Agent.Adapters.Slack.OAuth;
using HPD.Agent.Secrets;

namespace HPD.Agent.Adapters.Tests.Unit.SlackOAuth;

public class TokenStoreSecretResolverTests
{
    private static InMemorySlackTokenStore StoreWith(string teamId, string token)
    {
        var store = new InMemorySlackTokenStore();
        store.SaveAsync(teamId, token).GetAwaiter().GetResult();
        return store;
    }

    [Fact]
    public async Task ResolveAsync_KnownTeamId_ReturnsToken()
    {
        var resolver = new TokenStoreSecretResolver(StoreWith("T123", "xoxb-abc"));
        var result = await resolver.ResolveAsync("slack:BotToken:T123");
        result.Should().NotBeNull();
        result!.Value.Value.Should().Be("xoxb-abc");
    }

    [Fact]
    public async Task ResolveAsync_UnknownTeamId_NoBase_ReturnsNull()
    {
        var resolver = new TokenStoreSecretResolver(new InMemorySlackTokenStore());
        var result = await resolver.ResolveAsync("slack:BotToken:T-unknown");
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_UnknownTeamId_DelegatesToBaseResolver()
    {
        var baseResolver = new ExplicitSecretResolver(
            new Dictionary<string, string> { ["slack:BotToken:T-base"] = "xoxb-from-base" });
        var resolver = new TokenStoreSecretResolver(new InMemorySlackTokenStore(), baseResolver);

        var result = await resolver.ResolveAsync("slack:BotToken:T-base");
        result.Should().NotBeNull();
        result!.Value.Value.Should().Be("xoxb-from-base");
    }

    [Fact]
    public async Task ResolveAsync_NonBotTokenKey_SkipsStore_DelegatesToBase()
    {
        var baseResolver = new ExplicitSecretResolver(
            new Dictionary<string, string> { ["anthropic:ApiKey"] = "sk-ant-123" });
        var resolver = new TokenStoreSecretResolver(new InMemorySlackTokenStore(), baseResolver);

        var result = await resolver.ResolveAsync("anthropic:ApiKey");
        result.Should().NotBeNull();
        result!.Value.Value.Should().Be("sk-ant-123");
    }

    [Fact]
    public async Task ResolveAsync_NonBotTokenKey_NoBase_ReturnsNull()
    {
        var resolver = new TokenStoreSecretResolver(new InMemorySlackTokenStore());
        var result = await resolver.ResolveAsync("anthropic:ApiKey");
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_CaseInsensitivePrefix()
    {
        var resolver = new TokenStoreSecretResolver(StoreWith("T1", "xoxb-case"));
        var result = await resolver.ResolveAsync("SLACK:BOTTOKEN:T1");
        result.Should().NotBeNull();
        result!.Value.Value.Should().Be("xoxb-case");
    }

    [Fact]
    public async Task ResolveAsync_Source_ContainsSlackTokenStorePrefix()
    {
        var resolver = new TokenStoreSecretResolver(StoreWith("T999", "xoxb-src"));
        var result = await resolver.ResolveAsync("slack:BotToken:T999");
        result!.Value.Source.Should().StartWith("slack-token-store:");
    }

    [Fact]
    public async Task ResolveAsync_BaseResolverNotCalledWhenStoreHits()
    {
        var neverCalled = new ThrowingSecretResolver();
        var resolver = new TokenStoreSecretResolver(StoreWith("T777", "xoxb-hit"), neverCalled);

        // Should not throw — base is never consulted when the store has the token
        var act = async () => await resolver.ResolveAsync("slack:BotToken:T777");
        await act.Should().NotThrowAsync();
    }

    /// <summary>Resolver that throws if ever consulted — used to assert it is never reached.</summary>
    private sealed class ThrowingSecretResolver : ISecretResolver
    {
        public ValueTask<ResolvedSecret?> ResolveAsync(string key, CancellationToken ct = default)
            => throw new InvalidOperationException("Base resolver must not be called.");
    }
}
