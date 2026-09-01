using HPD.Agent.MCP;
using ModelContextProtocol.Authentication;

namespace HPD.Agent.Tests.MCPServer;

public sealed class McpAuthorizationTokenCacheTests
{
    [Fact]
    public async Task IssuerMismatchDeletesRecordAndNeverReturnsTokens()
    {
        var store = new RecordingStore();
        var cache = Cache(store, "resource-a");
        await cache.StoreTokensAsync(Tokens("https://issuer-a.example"), default);
        store.Record = store.Record! with { Issuer = "https://issuer-b.example" };

        var restored = await cache.GetTokensAsync(default);

        Assert.Null(restored);
        Assert.Equal(1, store.DeleteCount);
    }

    [Fact]
    public async Task MatchingIssuerClientAndScopesRoundTripThroughSourceGeneratedPayload()
    {
        var store = new RecordingStore();
        var cache = Cache(store, "resource-a");
        await cache.StoreTokensAsync(Tokens("https://issuer.example/"), default);

        var restored = await cache.GetTokensAsync(default);

        Assert.NotNull(restored);
        Assert.Equal("secret-token", restored!.AccessToken);
        Assert.Equal("https://issuer.example/", restored.AuthorizationServer);
        Assert.Equal("https://issuer.example", store.Record!.Issuer);
    }

    [Fact]
    public async Task ResourceRegistrationsUseDistinctStoreIdentities()
    {
        var store = new MultiRecordStore();
        var first = Cache(store, "resource-a");
        var second = Cache(store, "resource-b");

        await first.StoreTokensAsync(Tokens("https://issuer-a.example"), default);
        await second.StoreTokensAsync(Tokens("https://issuer-b.example"), default);

        Assert.Equal(2, store.Records.Count);
    }

    [Fact]
    public async Task MisroutedEnvelopeFromAnotherResourceIsDeleted()
    {
        var store = new RecordingStore();
        await Cache(store, "resource-a").StoreTokensAsync(
            Tokens("https://issuer.example"), default);
        var second = Cache(store, "resource-b");

        var restored = await second.GetTokensAsync(default);

        Assert.Null(restored);
        Assert.Equal(1, store.DeleteCount);
    }

    private static McpAuthorizationTokenCache Cache(IMcpAuthorizationStore store, string name) =>
        new(store, new McpServerConfig
        {
            Name = name,
            Transport = "http",
            Endpoint = new Uri($"https://{name}.example/mcp")
        }, new McpOAuthOptions
        {
            ClientId = "client",
            Scopes = ["read", "write"],
            RedirectUri = new Uri("https://client.example/callback"),
            RegistrationMode = McpOAuthClientRegistrationMode.PreRegistered
        });

    private static TokenContainer Tokens(string issuer) => new()
    {
        TokenType = "Bearer",
        AccessToken = "secret-token",
        RefreshToken = "refresh-token",
        ObtainedAt = DateTimeOffset.UtcNow,
        ClientId = "client",
        Scope = "write read",
        AuthorizationServer = issuer
    };

    private sealed class RecordingStore : IMcpAuthorizationStore
    {
        internal McpAuthorizationRecord? Record { get; set; }
        internal int DeleteCount { get; private set; }

        public ValueTask<McpAuthorizationRecord?> LoadAsync(McpResourceRegistrationId resource, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Record);

        public ValueTask SaveAsync(McpResourceRegistrationId resource, McpAuthorizationRecord record, CancellationToken cancellationToken)
        {
            Record = record;
            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteAsync(McpResourceRegistrationId resource, CancellationToken cancellationToken)
        {
            DeleteCount++;
            Record = null;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MultiRecordStore : IMcpAuthorizationStore
    {
        internal Dictionary<McpResourceRegistrationId, McpAuthorizationRecord> Records { get; } = [];

        public ValueTask<McpAuthorizationRecord?> LoadAsync(McpResourceRegistrationId resource, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Records.GetValueOrDefault(resource));

        public ValueTask SaveAsync(McpResourceRegistrationId resource, McpAuthorizationRecord record, CancellationToken cancellationToken)
        {
            Records[resource] = record;
            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteAsync(McpResourceRegistrationId resource, CancellationToken cancellationToken)
        {
            Records.Remove(resource);
            return ValueTask.CompletedTask;
        }
    }
}
