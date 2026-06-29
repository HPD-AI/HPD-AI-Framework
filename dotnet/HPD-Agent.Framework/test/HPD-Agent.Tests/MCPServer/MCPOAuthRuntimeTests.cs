using System.Reflection;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.MCP;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Authentication;
using System.Net;
using System.Net.Sockets;

namespace HPD.Agent.Tests.MCPServer;

public sealed class MCPOAuthRuntimeTests
{
    [Fact]
    public void CreateOAuthOptions_AppliesRuntimeHooks()
    {
        var runtime = new RecordingOAuthRuntime();
        using var manager = new MCPClientManager(
            NullLogger.Instance,
            new MCPOptions
            {
                OAuthRuntime = runtime
            });
        var config = new MCPServerConfig
        {
            Name = "remote",
            Transport = "http",
            Endpoint = "https://mcp.example.com/mcp",
            OAuth = new MCPOAuthConfig
            {
                RedirectUri = "http://localhost:8787/callback",
                ClientId = "client-id",
                Scopes = ["read"]
            }
        };

        var options = InvokeCreateOAuthOptions(manager, config);

        options.Should().NotBeNull();
        options!.AuthorizationRedirectDelegate.Should().BeSameAs(runtime.RedirectDelegate);
        options.TokenCache.Should().BeSameAs(runtime.TokenCache);
        options.AuthServerSelector.Should().BeSameAs(runtime.AuthServerSelector);
        options.ScopeSelector.Should().BeSameAs(runtime.ScopeSelector);
        options.ClientId.Should().Be("client-id");
        options.DynamicClientRegistration.Should().NotBeNull();
        options.DynamicClientRegistration!.ResponseDelegate.Should().BeSameAs(runtime.RegistrationResponseDelegate);
    }

    [Fact]
    public async Task InMemoryRuntime_PersistsTokensAndDynamicClientRegistrationInMemory()
    {
        var runtime = new InMemoryMcpOAuthRuntime();
        var config = CreateOAuthServerConfig();

        var tokenCache = runtime.CreateTokenCache(config);
        tokenCache.Should().NotBeNull();

        var tokens = new TokenContainer
        {
            TokenType = "Bearer",
            AccessToken = "access-token",
            RefreshToken = "refresh-token",
            ExpiresIn = 3600,
            Scope = "read",
            ObtainedAt = DateTimeOffset.UtcNow
        };
        await tokenCache!.StoreTokensAsync(tokens, CancellationToken.None);

        var secondCache = runtime.CreateTokenCache(config);
        var cachedTokens = await secondCache!.GetTokensAsync(CancellationToken.None);
        cachedTokens.Should().NotBeNull();
        cachedTokens!.AccessToken.Should().Be("access-token");

        var responseDelegate = runtime.CreateDynamicClientRegistrationResponseDelegate(config);
        responseDelegate.Should().NotBeNull();
        await responseDelegate!(
            new DynamicClientRegistrationResponse
            {
                ClientId = "generated-client",
                ClientSecret = "generated-secret"
            },
            CancellationToken.None);

        var registration = runtime.GetClientRegistration(config);
        registration.Should().NotBeNull();
        registration!.ClientId.Should().Be("generated-client");
        registration.ClientSecret.Should().Be("generated-secret");
    }

    [Fact]
    public async Task JsonRuntime_PersistsTokensAndDynamicClientRegistrationToDisk()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hpd-mcp-oauth-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = CreateOAuthServerConfig();
            var runtime = new JsonMcpOAuthRuntime(directory);

            var tokenCache = runtime.CreateTokenCache(config);
            tokenCache.Should().NotBeNull();

            await tokenCache!.StoreTokensAsync(
                new TokenContainer
                {
                    TokenType = "Bearer",
                    AccessToken = "disk-access-token",
                    RefreshToken = "disk-refresh-token",
                    ExpiresIn = 3600,
                    Scope = "read write",
                    ObtainedAt = DateTimeOffset.UtcNow
                },
                CancellationToken.None);

            var reloadedRuntime = new JsonMcpOAuthRuntime(directory);
            var cachedTokens = await reloadedRuntime.CreateTokenCache(config)!.GetTokensAsync(CancellationToken.None);
            cachedTokens.Should().NotBeNull();
            cachedTokens!.AccessToken.Should().Be("disk-access-token");
            cachedTokens.RefreshToken.Should().Be("disk-refresh-token");

            var responseDelegate = runtime.CreateDynamicClientRegistrationResponseDelegate(config);
            await responseDelegate!(
                new DynamicClientRegistrationResponse
                {
                    ClientId = "disk-client",
                    ClientSecret = "disk-secret"
                },
                CancellationToken.None);

            var registration = reloadedRuntime.GetClientRegistration(config);
            registration.Should().NotBeNull();
            registration!.ClientId.Should().Be("disk-client");
            registration.ClientSecret.Should().Be("disk-secret");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateOAuthOptions_UsesRuntimeClientRegistrationWhenManifestDoesNotSetClientId()
    {
        var runtime = new InMemoryMcpOAuthRuntime();
        var config = CreateOAuthServerConfig();
        var responseDelegate = runtime.CreateDynamicClientRegistrationResponseDelegate(config);
        responseDelegate!(
            new DynamicClientRegistrationResponse
            {
                ClientId = "runtime-client",
                ClientSecret = "runtime-secret"
            },
            CancellationToken.None).GetAwaiter().GetResult();

        using var manager = new MCPClientManager(
            NullLogger.Instance,
            new MCPOptions
            {
                OAuthRuntime = runtime
            });

        var options = InvokeCreateOAuthOptions(manager, config);

        options.Should().NotBeNull();
        options!.ClientId.Should().Be("runtime-client");
        options.ClientSecret.Should().Be("runtime-secret");
    }

    [Fact]
    public async Task LocalBrowserRedirectHandler_CapturesAuthorizationCode()
    {
        using var httpClient = new HttpClient();
        var redirectUri = new Uri($"http://localhost:{GetFreeTcpPort()}/callback");
        Uri? openedUri = null;

        var handler = McpOAuthRedirectHandlers.LocalBrowser(
            openBrowser: authorizationUri =>
            {
                openedUri = authorizationUri;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100);
                    await httpClient.GetAsync(new Uri(redirectUri, "?code=test-code"));
                });
            },
            timeout: TimeSpan.FromSeconds(10));

        var code = await handler(new Uri("https://auth.example.com/authorize"), redirectUri, CancellationToken.None);

        openedUri.Should().Be(new Uri("https://auth.example.com/authorize"));
        code.Should().Be("test-code");
    }

    [Fact]
    public async Task LocalBrowserRedirectHandler_ReturnsNullForOAuthError()
    {
        using var httpClient = new HttpClient();
        var redirectUri = new Uri($"http://localhost:{GetFreeTcpPort()}/callback");
        var handler = McpOAuthRedirectHandlers.LocalBrowser(
            openBrowser: _authorizationUri =>
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100);
                    await httpClient.GetAsync(new Uri(redirectUri, "?error=access_denied"));
                });
            },
            timeout: TimeSpan.FromSeconds(10));

        var code = await handler(new Uri("https://auth.example.com/authorize"), redirectUri, CancellationToken.None);

        code.Should().BeNull();
    }

    [Fact]
    public async Task LocalBrowserRedirectHandler_RejectsNonLocalRedirectUri()
    {
        var handler = McpOAuthRedirectHandlers.LocalBrowser();

        var act = async () => await handler(
            new Uri("https://auth.example.com/authorize"),
            new Uri("https://example.com/callback"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*localhost*");
    }

    [Fact]
    public void WithMCPOptions_ConfiguresOptionsWithoutCreatingManager()
    {
        var runtime = new RecordingOAuthRuntime();
        var builder = new AgentBuilder()
            .WithMCPOptions(options =>
            {
                options.FailOnServerError = true;
                options.OAuthRuntime = runtime;
            });

        builder.Config.Mcp.Should().NotBeNull();
        builder.Config.Mcp!.ManifestPath.Should().BeEmpty();
        builder.Config.Mcp.Options.Should().BeOfType<MCPOptions>()
            .Which.OAuthRuntime.Should().BeSameAs(runtime);
        builder.McpClientManager.Should().BeNull();
    }

    [Fact]
    public void WithMCP_PreservesOptionsConfiguredEarlier()
    {
        var runtime = new RecordingOAuthRuntime();
        var builder = new AgentBuilder()
            .WithMCPOptions(options => options.OAuthRuntime = runtime)
            .WithMCP("mcp.json");

        builder.Config.Mcp.Should().NotBeNull();
        builder.Config.Mcp!.ManifestPath.Should().Be("mcp.json");
        builder.Config.Mcp.Options.Should().BeOfType<MCPOptions>()
            .Which.OAuthRuntime.Should().BeSameAs(runtime);
        builder.McpClientManager.Should().NotBeNull();
    }

    [Fact]
    public void WithMCPContent_StoresManifestContentSeparatelyFromPath()
    {
        const string manifestContent = """
            {
              "servers": []
            }
            """;

        var builder = new AgentBuilder()
            .WithMCPContent(manifestContent);

        builder.Config.Mcp.Should().NotBeNull();
        builder.Config.Mcp!.ManifestPath.Should().BeEmpty();
        builder.Config.Mcp.ManifestContent.Should().Be(manifestContent);
        builder.McpClientManager.Should().NotBeNull();
    }

    private static ClientOAuthOptions? InvokeCreateOAuthOptions(MCPClientManager manager, MCPServerConfig config)
    {
        var method = typeof(MCPClientManager).GetMethod(
            "CreateOAuthOptions",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (ClientOAuthOptions?)method!.Invoke(manager, [config]);
    }

    private static MCPServerConfig CreateOAuthServerConfig()
    {
        return new MCPServerConfig
        {
            Name = "remote",
            Transport = "http",
            Endpoint = "https://mcp.example.com/mcp",
            OAuth = new MCPOAuthConfig
            {
                RedirectUri = "http://localhost:8787/callback"
            }
        };
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class RecordingOAuthRuntime : IMcpOAuthRuntime
    {
        public AuthorizationRedirectDelegate RedirectDelegate { get; } =
            static (_, _, _) => Task.FromResult<string?>("code");

        public ITokenCache TokenCache { get; } = new RecordingTokenCache();

        public Func<IReadOnlyList<Uri>, Uri?> AuthServerSelector { get; } =
            static servers => servers.FirstOrDefault();

        public ScopeSelectorDelegate ScopeSelector { get; } =
            static scopes => scopes;

        public Func<DynamicClientRegistrationResponse, CancellationToken, Task> RegistrationResponseDelegate { get; } =
            static (_, _) => Task.CompletedTask;

        public McpOAuthClientRegistration? GetClientRegistration(MCPServerConfig server) => null;

        public AuthorizationRedirectDelegate? CreateAuthorizationRedirectDelegate(MCPServerConfig server) => RedirectDelegate;

        public ITokenCache? CreateTokenCache(MCPServerConfig server) => TokenCache;

        public Func<IReadOnlyList<Uri>, Uri?>? CreateAuthServerSelector(MCPServerConfig server) => AuthServerSelector;

        public ScopeSelectorDelegate? CreateScopeSelector(MCPServerConfig server) => ScopeSelector;

        public Func<DynamicClientRegistrationResponse, CancellationToken, Task>? CreateDynamicClientRegistrationResponseDelegate(MCPServerConfig server)
            => RegistrationResponseDelegate;
    }

    private sealed class RecordingTokenCache : ITokenCache
    {
        private TokenContainer? _tokens;

        public ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
        {
            _tokens = tokens;
            return default;
        }

        public ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken)
        {
            return new ValueTask<TokenContainer?>(_tokens);
        }
    }
}
