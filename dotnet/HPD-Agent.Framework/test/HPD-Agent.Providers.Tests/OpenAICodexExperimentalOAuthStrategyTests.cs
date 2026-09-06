using System.Net;
using System.Text;
using HPD.Agent.Providers.OpenAI;

namespace HPD.Agent.Providers.Tests;

public sealed class OpenAICodexExperimentalOAuthStrategyTests
{
    [Fact]
    public void ModelPolicy_RejectsWrongModelAndUnsupportedEffort()
    {
        var policy = new OpenAICodexModelPolicy("fixture", ["low", "medium", "ultra"], "medium");
        Assert.Throws<InvalidOperationException>(() => OpenAICodexModelPolicy.Validate("other", null, policy));
        Assert.Throws<NotSupportedException>(() => OpenAICodexModelPolicy.Validate("fixture", new Microsoft.Extensions.AI.ChatOptions
        { Reasoning = new Microsoft.Extensions.AI.ReasoningOptions { Effort = Microsoft.Extensions.AI.ReasoningEffort.High } }, policy));
        OpenAICodexModelPolicy.Validate("fixture", null, policy);
        Assert.Contains("ultra", policy.SupportedReasoningEfforts);
    }

    [Fact]
    public async Task SignedHeadersRemainOwnedByRequestAfterCredentialDisposal()
    {
        var strategy = new OpenAICodexExperimentalOAuthStrategy(new HttpClient(new RoutingHandler(_ =>
            Task.FromResult(Json(HttpStatusCode.OK, "{}")))));
        var normalized = await strategy.NormalizeAsync(Request());
        await using var session = Session("access-one", "account-one");
        var credential = await strategy.CreateCredentialAsync(normalized.Identity, session);
        var signer = Assert.IsType<ProviderCredential.SignedRequest>(credential).Lease.Signer;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://chatgpt.com/backend-api/codex/responses");
        await signer.SignAsync(request);
        await credential.DisposeAsync();
        Assert.Equal("access-one", request.Headers.Authorization!.Parameter);
        Assert.Equal("account-one", Assert.Single(request.Headers.GetValues("ChatGPT-Account-Id")));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => signer.SignAsync(request).AsTask());
    }

    [Fact]
    public async Task BrowserFlow_UsesObservedParametersAndCreatesAccountBoundSigner()
    {
        var handler = new RoutingHandler(async request =>
        {
            Assert.Equal(new Uri("https://auth.openai.com/oauth/token"), request.RequestUri);
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("grant_type=authorization_code", body, StringComparison.Ordinal);
            Assert.Contains("code=approved", body, StringComparison.Ordinal);
            return Json(HttpStatusCode.OK, Token("account-one", "access-one", "refresh-one"));
        });
        var strategy = new OpenAICodexExperimentalOAuthStrategy(new HttpClient(handler));
        var normalized = await strategy.NormalizeAsync(Request());
        var start = await strategy.BeginAuthorizationAsync(new BrowserProviderAuthorizationBeginContext
        {
            Request = normalized,
            RedirectUri = new Uri("http://127.0.0.1:58421/callback"),
            TimeProvider = TimeProvider.System
        });
        await using var transaction = start.TransactionState;
        var challenge = Assert.IsType<BrowserAuthorizationChallenge>(start.Challenge);
        var query = Query(challenge.AuthorizationUri.Query);
        Assert.Equal("app_EMoamEEZ73f0CkXaXp7hrann", query["client_id"]);
        Assert.Equal("true", query["codex_cli_simplified_flow"]);
        Assert.Equal("hpd-agent", query["originator"]);
        var callback = new BrowserAuthorizationResponse
        {
            TransactionId = transaction.TransactionId,
            CallbackUri = new Uri($"{challenge.RedirectUri}?code=approved&state={Uri.EscapeDataString(query["state"])}")
        };
        await using var session = await strategy.CompleteBrowserAuthorizationAsync(transaction, callback);
        Assert.Equal("account-one", session.ProviderState!["chatgpt_account_id"]);

        await using var credential = await strategy.CreateCredentialAsync(normalized.Identity, session);
        var signed = Assert.IsType<ProviderCredential.SignedRequest>(credential);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://chatgpt.com/backend-api/codex/responses");
        await signed.Lease.Signer.SignAsync(request);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("access-one", request.Headers.Authorization.Parameter);
        Assert.Equal("account-one", Assert.Single(request.Headers.GetValues("ChatGPT-Account-Id")));
        Assert.Equal("hpd-agent", Assert.Single(request.Headers.GetValues("originator")));

        using var models = new HttpRequestMessage(HttpMethod.Get,
            "https://chatgpt.com/backend-api/codex/models?client_version=1.0.0");
        await signed.Lease.Signer.SignAsync(models);
        Assert.Equal("access-one", models.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Signer_RejectsEveryUnpinnedAuthorityOrPath()
    {
        var strategy = new OpenAICodexExperimentalOAuthStrategy(new HttpClient(new RoutingHandler(_ =>
            Task.FromResult(Json(HttpStatusCode.OK, "{}")))));
        var normalized = await strategy.NormalizeAsync(Request());
        await using var session = Session("access-one", "account-one");
        await using var credential = await strategy.CreateCredentialAsync(normalized.Identity, session);
        var signer = Assert.IsType<ProviderCredential.SignedRequest>(credential).Lease.Signer;

        using var wrongHost = new HttpRequestMessage(HttpMethod.Post, "https://example.com/backend-api/codex/responses");
        await Assert.ThrowsAsync<InvalidOperationException>(() => signer.SignAsync(wrongHost).AsTask());
        using var wrongPath = new HttpRequestMessage(HttpMethod.Post, "https://chatgpt.com/backend-api/other");
        await Assert.ThrowsAsync<InvalidOperationException>(() => signer.SignAsync(wrongPath).AsTask());
    }

    [Fact]
    public async Task DeviceFlow_PerformsOneBoundedPollStepThenCompletes()
    {
        var pollCount = 0;
        var handler = new RoutingHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/usercode", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, """{"device_auth_id":"device-one","user_code":"ABCD-EFGH","interval":"1"}""");
            if (request.RequestUri.AbsolutePath.EndsWith("/deviceauth/token", StringComparison.Ordinal))
            {
                await request.Content!.ReadAsStringAsync();
                if (Interlocked.Increment(ref pollCount) == 1) return Json(HttpStatusCode.Forbidden, "{}");
                return Json(HttpStatusCode.OK, """{"authorization_code":"approved","code_verifier":"verifier-one"}""");
            }
            return Json(HttpStatusCode.OK, Token("account-one", "access-one", "refresh-one"));
        });
        var strategy = new OpenAICodexExperimentalOAuthStrategy(new HttpClient(handler));
        var normalized = await strategy.NormalizeAsync(Request());
        var start = await strategy.BeginAuthorizationAsync(new DeviceProviderAuthorizationBeginContext
        {
            Request = normalized,
            TimeProvider = TimeProvider.System
        });
        var challenge = Assert.IsType<DeviceAuthorizationChallenge>(start.Challenge);
        Assert.Equal("ABCD-EFGH", challenge.UserCode);
        Assert.Equal(new Uri("https://auth.openai.com/codex/device"), challenge.VerificationUri);

        await using var first = await strategy.AdvanceDeviceAuthorizationAsync(start.TransactionState);
        var pending = Assert.IsType<ProviderDeviceAuthorizationProgress.Pending>(first);
        await using var second = await strategy.AdvanceDeviceAuthorizationAsync(pending.Transaction);
        var authorized = Assert.IsType<ProviderDeviceAuthorizationProgress.Authorized>(second);
        Assert.Equal("account-one", authorized.Session.ProviderState!["chatgpt_account_id"]);
        await start.TransactionState.DisposeAsync();
    }

    [Fact]
    public async Task RefreshWithoutReplacementToken_RetainsCurrentRefreshAndAccount()
    {
        var handler = new RoutingHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("refresh_token=refresh-one", body, StringComparison.Ordinal);
            return Json(HttpStatusCode.OK, """{"access_token":"access-two","token_type":"Bearer","expires_in":120}""");
        });
        var strategy = new OpenAICodexExperimentalOAuthStrategy(new HttpClient(handler));
        var normalized = await strategy.NormalizeAsync(Request());
        await using var current = Session("access-one", "account-one", "refresh-one");

        await using var refreshed = await strategy.RefreshAsync(normalized.Identity, current);

        Assert.Equal(ProviderRefreshTokenDisposition.RetainCurrent, refreshed.RefreshTokenDisposition);
        Assert.Null(refreshed.Secrets.ReplacementRefreshToken);
        Assert.Equal("account-one", refreshed.ProviderState!["chatgpt_account_id"]);
    }

    private static ProviderCredentialRequest Request() => new()
    {
        ProviderKey = "openai",
        BackendKey = "codex",
        Family = ProviderClientFamily.Chat,
        Authentication = new OAuthProviderAuthentication { AccountId = "personal" },
        AuthorizationScope = new ProviderAuthorizationScope { TrustDomainId = "test" },
        Audience = new ProviderCredentialAudience()
    };

    private static ProviderAuthorizationSession Session(string access, string account, string? refresh = null) => new()
    {
        SchemaVersion = "test",
        Secrets = new TestSecrets(access, refresh),
        TokenType = "Bearer",
        AuthorizationServer = "https://auth.openai.com",
        ProviderState = new Dictionary<string, string> { ["chatgpt_account_id"] = account }
    };

    private static string Token(string account, string access, string refresh)
    {
        var header = Base64Url("{\"alg\":\"none\"}");
        var payload = Base64Url($"{{\"chatgpt_account_id\":\"{account}\"}}");
        return $"{{\"id_token\":\"{header}.{payload}.x\",\"access_token\":\"{access}\",\"refresh_token\":\"{refresh}\",\"token_type\":\"Bearer\",\"expires_in\":3600}}";
    }

    private static string Base64Url(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static HttpResponseMessage Json(HttpStatusCode status, string value) => new(status)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };
    private static Dictionary<string, string> Query(string query) => query.TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries).Select(value => value.Split('=', 2))
        .ToDictionary(value => Uri.UnescapeDataString(value[0]),
            value => Uri.UnescapeDataString(value.Length == 2 ? value[1].Replace('+', ' ') : string.Empty), StringComparer.Ordinal);

    private sealed class RoutingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => route(request);
    }
    private sealed class TestBuffer(string value) : IProviderSecretBuffer
    {
        private char[]? _value = value.ToCharArray();
        public ReadOnlyMemory<char> Value => _value ?? throw new ObjectDisposedException(nameof(TestBuffer));
        public ValueTask DisposeAsync() { var value = Interlocked.Exchange(ref _value, null); value?.AsSpan().Clear(); return ValueTask.CompletedTask; }
    }
    private sealed class TestSecrets(string access, string? refresh) : IProviderAuthorizationSecretSet
    {
        private IProviderSecretBuffer? _access = new TestBuffer(access);
        private IProviderSecretBuffer? _refresh = refresh is null ? null : new TestBuffer(refresh);
        public IProviderSecretBuffer AccessToken => _access ?? throw new ObjectDisposedException(nameof(TestSecrets));
        public IProviderSecretBuffer? RefreshToken => _refresh;
        public IProviderSecretBuffer? ClientSecret => null;
        public async ValueTask DisposeAsync() { var accessValue = Interlocked.Exchange(ref _access, null); var refreshValue = Interlocked.Exchange(ref _refresh, null); if (accessValue is not null) await accessValue.DisposeAsync(); if (refreshValue is not null) await refreshValue.DisposeAsync(); }
    }
}
