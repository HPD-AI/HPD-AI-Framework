using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Agent.Providers.HuggingFace;

namespace HPD.Agent.Providers.Tests;

public sealed class HuggingFaceOAuthStrategyTests
{
    [Fact]
    public async Task DisconnectedOAuthConfiguration_BuildsWithoutAuthorizationOrNetworkAccess()
    {
        var handler = new TokenHandler((_, _) => throw new Xunit.Sdk.XunitException("OAuth network access is forbidden during build."));
        var builder = new AgentBuilder(new AgentConfig())
            .WithHuggingFaceOAuth(
                "mistralai/Mistral-7B-Instruct-v0.2",
                "personal",
                "client-one",
                new HttpClient(handler));

        var agent = await builder.BuildAsync(CancellationToken.None);

        Assert.NotNull(agent);
    }

    [Fact]
    public async Task AuthorizationCodeFlow_UsesPkceAndCreatesBoundSession()
    {
        var handler = new TokenHandler((body, call) =>
        {
            Assert.Equal(1, call);
            Assert.Contains("grant_type=authorization_code", body, StringComparison.Ordinal);
            Assert.Contains("code=approved", body, StringComparison.Ordinal);
            Assert.Contains("code_verifier=", body, StringComparison.Ordinal);
            return """{"access_token":"access-one","refresh_token":"refresh-one","token_type":"Bearer","expires_in":3600,"scope":"inference-api profile"}""";
        });
        var now = new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
        var strategy = new HuggingFaceOAuthStrategy("client-one", new HttpClient(handler), new FixedTimeProvider(now));
        var normalized = await strategy.NormalizeAsync(Request(["profile", "inference-api", "profile"]));
        var start = await strategy.BeginAuthorizationAsync(new BrowserProviderAuthorizationBeginContext
        {
            Request = normalized,
            RedirectUri = new Uri("http://127.0.0.1:58421/callback"),
            TimeProvider = new FixedTimeProvider(now)
        });

        var challenge = Assert.IsType<BrowserAuthorizationChallenge>(start.Challenge);
        var query = Query(challenge.AuthorizationUri.Query);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrWhiteSpace(query["code_challenge"]));
        Assert.Equal("inference-api profile", query["scope"]);
        Assert.DoesNotContain("code_verifier", query.Keys);
        using (var payload = JsonDocument.Parse(start.TransactionState.ProviderState.Value))
        {
            var verifier = payload.RootElement.GetProperty("verifier").GetString()!;
            var expectedChallenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            Assert.Equal(expectedChallenge, query["code_challenge"]);
        }

        var response = new BrowserAuthorizationResponse
        {
            TransactionId = challenge.TransactionId,
            CallbackUri = new Uri($"{challenge.RedirectUri}?code=approved&state={Uri.EscapeDataString(query["state"])}")
        };
        await strategy.ValidateBrowserAuthorizationResponseAsync(start.TransactionState, response);
        await using var session = await strategy.CompleteBrowserAuthorizationAsync(start.TransactionState, response);

        Assert.Equal("client-one", session.ClientId);
        Assert.Equal("none", session.TokenEndpointAuthenticationMethod);
        Assert.Equal("https://huggingface.co", session.AuthorizationServer);
        Assert.Equal(now.AddHours(1), session.ExpiresAt);
        Assert.Equal("access-one", session.Secrets.AccessToken.Value.ToString());
        Assert.Equal("refresh-one", session.Secrets.RefreshToken!.Value.ToString());
        Assert.Equal(["inference-api", "profile"], session.GrantedScopes);
        await start.TransactionState.DisposeAsync();
    }

    [Fact]
    public async Task CallbackStateMismatchAndDuplicateParameters_FailWithRedactedProtocolErrors()
    {
        var strategy = new HuggingFaceOAuthStrategy("client-one", new HttpClient(new TokenHandler((_, _) => "{}")));
        var normalized = await strategy.NormalizeAsync(Request());
        var start = await strategy.BeginAuthorizationAsync(new BrowserProviderAuthorizationBeginContext
        {
            Request = normalized,
            RedirectUri = new Uri("http://127.0.0.1:58421/callback"),
            TimeProvider = TimeProvider.System
        });

        var mismatch = await Assert.ThrowsAsync<ProviderAuthenticationException>(() =>
            strategy.ValidateBrowserAuthorizationResponseAsync(start.TransactionState, new BrowserAuthorizationResponse
            {
                TransactionId = start.TransactionState.TransactionId,
                CallbackUri = new Uri("http://127.0.0.1:58421/callback?code=secret-code&state=wrong")
            }).AsTask());
        Assert.Equal(ProviderAuthenticationFailureKind.ProtocolError, mismatch.FailureKind);
        Assert.DoesNotContain("secret-code", mismatch.ToString(), StringComparison.Ordinal);

        var duplicate = await Assert.ThrowsAsync<ProviderAuthenticationException>(() =>
            strategy.ValidateBrowserAuthorizationResponseAsync(start.TransactionState, new BrowserAuthorizationResponse
            {
                TransactionId = start.TransactionState.TransactionId,
                CallbackUri = new Uri("http://127.0.0.1:58421/callback?code=one&code=two&state=x")
            }).AsTask());
        Assert.Equal("OAuthDuplicateParameter", duplicate.DiagnosticCode);
        await start.TransactionState.DisposeAsync();
    }

    [Fact]
    public async Task CallbackFromAnotherRedirect_IsRejectedBeforeCodeExchange()
    {
        var handler = new TokenHandler((_, _) => throw new Xunit.Sdk.XunitException("Token endpoint must not be called."));
        var strategy = new HuggingFaceOAuthStrategy("client-one", new HttpClient(handler));
        var normalized = await strategy.NormalizeAsync(Request());
        var start = await strategy.BeginAuthorizationAsync(new BrowserProviderAuthorizationBeginContext
        {
            Request = normalized,
            RedirectUri = new Uri("http://127.0.0.1:58421/callback"),
            TimeProvider = TimeProvider.System
        });
        var challenge = Assert.IsType<BrowserAuthorizationChallenge>(start.Challenge);
        var state = Query(challenge.AuthorizationUri.Query)["state"];

        var error = await Assert.ThrowsAsync<ProviderAuthenticationException>(() =>
            strategy.CompleteBrowserAuthorizationAsync(start.TransactionState, new BrowserAuthorizationResponse
            {
                TransactionId = challenge.TransactionId,
                CallbackUri = new Uri($"http://attacker.invalid/callback?code=secret-code&state={Uri.EscapeDataString(state)}")
            }).AsTask());

        Assert.Equal("OAuthRedirectMismatch", error.DiagnosticCode);
        Assert.DoesNotContain("secret-code", error.ToString(), StringComparison.Ordinal);
        await start.TransactionState.DisposeAsync();
    }

    [Theory]
    [InlineData("http://remote.example/callback")]
    [InlineData("https://user@example.com/callback")]
    [InlineData("https://example.com/callback?target=other")]
    [InlineData("https://example.com/callback#fragment")]
    public async Task UnsafeRedirectUri_IsRejectedBeforeCreatingTransaction(string redirectUri)
    {
        var strategy = new HuggingFaceOAuthStrategy("client-one", new HttpClient(new TokenHandler((_, _) => "{}")));
        var normalized = await strategy.NormalizeAsync(Request());

        await Assert.ThrowsAsync<ArgumentException>(() => strategy.BeginAuthorizationAsync(
            new BrowserProviderAuthorizationBeginContext
            {
                Request = normalized,
                RedirectUri = new Uri(redirectUri),
                TimeProvider = TimeProvider.System
            }).AsTask());
    }

    [Fact]
    public async Task RefreshWithoutRotatedToken_RetainsCurrentRefreshToken()
    {
        var handler = new TokenHandler((body, _) =>
        {
            Assert.Contains("grant_type=refresh_token", body, StringComparison.Ordinal);
            Assert.Contains("refresh_token=refresh-one", body, StringComparison.Ordinal);
            return """{"access_token":"access-two","token_type":"Bearer","expires_in":120,"scope":"inference-api"}""";
        });
        var strategy = new HuggingFaceOAuthStrategy("client-one", new HttpClient(handler));
        var normalized = await strategy.NormalizeAsync(Request());
        await using var session = new ProviderAuthorizationSession
        {
            SchemaVersion = "test",
            Secrets = new TestSecrets("access-one", "refresh-one"),
            TokenType = "Bearer",
            GrantedScopes = ["inference-api"],
            ClientId = "client-one",
            TokenEndpointAuthenticationMethod = "none",
            AuthorizationServer = normalized.Identity.AuthorizationServer
        };

        await using var refreshed = await strategy.RefreshAsync(normalized.Identity, session);
        Assert.Equal(ProviderRefreshTokenDisposition.RetainCurrent, refreshed.RefreshTokenDisposition);
        Assert.Null(refreshed.Secrets.ReplacementRefreshToken);
        Assert.Equal("access-two", refreshed.Secrets.AccessToken.Value.ToString());
    }

    private static ProviderCredentialRequest Request(IReadOnlyList<string>? scopes = null) => new()
    {
        ProviderKey = "huggingface",
        BackendKey = "platform",
        Family = ProviderClientFamily.Chat,
        Authentication = new OAuthProviderAuthentication { AccountId = "personal", Scopes = scopes },
        AuthorizationScope = new ProviderAuthorizationScope { TrustDomainId = "test" },
        Audience = new ProviderCredentialAudience { Scopes = scopes }
    };

    private static Dictionary<string, string> Query(string query) => query.TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(value => value.Split('=', 2))
        .ToDictionary(value => Uri.UnescapeDataString(value[0]),
            value => Uri.UnescapeDataString(value.Length == 2 ? value[1].Replace('+', ' ') : string.Empty),
            StringComparer.Ordinal);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TokenHandler(Func<string, int, string> response) : HttpMessageHandler
    {
        private int _calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(new Uri("https://huggingface.co/oauth/token"), request.RequestUri);
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response(body, Interlocked.Increment(ref _calls)))
            };
        }
    }

    private sealed class TestBuffer(string value) : IProviderSecretBuffer
    {
        private char[]? _value = value.ToCharArray();
        public ReadOnlyMemory<char> Value => _value ?? throw new ObjectDisposedException(nameof(TestBuffer));
        public ValueTask DisposeAsync()
        {
            var current = Interlocked.Exchange(ref _value, null);
            current?.AsSpan().Clear();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestSecrets(string access, string refresh) : IProviderAuthorizationSecretSet
    {
        private IProviderSecretBuffer? _access = new TestBuffer(access);
        private IProviderSecretBuffer? _refresh = new TestBuffer(refresh);
        public IProviderSecretBuffer AccessToken => _access ?? throw new ObjectDisposedException(nameof(TestSecrets));
        public IProviderSecretBuffer? RefreshToken => _refresh;
        public IProviderSecretBuffer? ClientSecret => null;
        public async ValueTask DisposeAsync()
        {
            var accessValue = Interlocked.Exchange(ref _access, null);
            var refreshValue = Interlocked.Exchange(ref _refresh, null);
            if (accessValue is not null) await accessValue.DisposeAsync();
            if (refreshValue is not null) await refreshValue.DisposeAsync();
        }
    }
}
