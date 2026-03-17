using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent.Adapters.Slack.OAuth;
using HPD.Agent.Secrets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Adapters.Tests.Unit.SlackOAuth;

/// <summary>
/// Integration tests for <c>GET /slack/install</c> and <c>GET /slack/oauth/callback</c>.
/// Uses an in-process <see cref="TestServer"/> and a fake <see cref="HttpMessageHandler"/>
/// to intercept outbound calls to <c>oauth.v2.access</c> without hitting Slack.
/// </summary>
public class SlackOAuthEndpointTests : IDisposable
{
    private const string ClientId     = "test-client-id";
    private const string ClientSecret = "test-client-secret";
    private const string RedirectUri  = "https://example.com/callback";
    private const string InstallPath  = "/slack/install";
    private const string CallbackPath = "/slack/oauth/callback";

    private readonly FakeSlackOAuthHandler _fakeSlack = new();
    private readonly InMemorySlackTokenStore _tokenStore = new();
    private readonly TestServer _server;
    private readonly HttpClient _client;

    public SlackOAuthEndpointTests()
    {
        var builder = new WebHostBuilder()
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddLogging();
                services.AddSingleton<ISlackTokenStore>(_tokenStore);
                services.AddHttpClient("slack")
                        .ConfigurePrimaryHttpMessageHandler(() => _fakeSlack);
                services.AddSlackOAuth(c =>
                {
                    c.ClientId     = ClientId;
                    c.ClientSecret = ClientSecret;
                    c.RedirectUri  = RedirectUri;
                });
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(e => e.MapSlackOAuth(InstallPath, CallbackPath));
            });

        _server = new TestServer(builder);
        _client = new HttpClient(_server.CreateHandler()) { BaseAddress = new Uri("http://localhost") };
        // Don't auto-follow redirects — tests assert on the 302 Location header directly
        _client = new HttpClient(new NoRedirectHandler(_server.CreateHandler()))
        {
            BaseAddress = new Uri("http://localhost")
        };
    }

    // ── /slack/install ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Install_Returns302()
    {
        var response = await _client.GetAsync(InstallPath);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Install_LocationHeader_PointsToSlackAuthorizeUrl()
    {
        var response = await _client.GetAsync(InstallPath);
        response.Headers.Location!.ToString()
            .Should().StartWith("https://slack.com/oauth/v2/authorize");
    }

    [Fact]
    public async Task Install_LocationHeader_ContainsClientId()
    {
        var response = await _client.GetAsync(InstallPath);
        response.Headers.Location!.Query.Should().Contain("client_id=test-client-id");
    }

    [Fact]
    public async Task Install_LocationHeader_ContainsScopeParam()
    {
        var response = await _client.GetAsync(InstallPath);
        response.Headers.Location!.Query.Should().Contain("scope=");
    }

    [Fact]
    public async Task Install_LocationHeader_ContainsRedirectUri()
    {
        var response = await _client.GetAsync(InstallPath);
        response.Headers.Location!.Query.Should().Contain("redirect_uri=");
    }

    [Fact]
    public async Task Install_ScopesAreJoinedWithComma()
    {
        var response = await _client.GetAsync(InstallPath);
        var query = Uri.UnescapeDataString(response.Headers.Location!.Query);
        // comma-separated, not space- or %20-separated
        query.Should().Contain(",");
    }

    // ── /slack/oauth/callback ─────────────────────────────────────────────────

    [Fact]
    public async Task Callback_MissingCode_Returns400()
    {
        var response = await _client.GetAsync(CallbackPath);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Callback_SlackErrorQueryParam_Returns400()
    {
        var response = await _client.GetAsync($"{CallbackPath}?error=access_denied");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Callback_ValidCode_CallsOAuthV2Access()
    {
        _fakeSlack.SetResponse(ok: true, teamId: "T100", botToken: "xoxb-100");
        await _client.GetAsync($"{CallbackPath}?code=valid-code");
        _fakeSlack.LastRequestUri.Should().NotBeNull();
        _fakeSlack.LastRequestUri!.AbsolutePath.Should().Contain("oauth.v2.access");
    }

    [Fact]
    public async Task Callback_ValidCode_PostsCorrectFormFields()
    {
        _fakeSlack.SetResponse(ok: true, teamId: "T101", botToken: "xoxb-101");
        await _client.GetAsync($"{CallbackPath}?code=my-code");

        var body = _fakeSlack.LastRequestBody!;
        body.Should().Contain("client_id=test-client-id");
        body.Should().Contain("client_secret=test-client-secret");
        body.Should().Contain("code=my-code");
        body.Should().Contain("redirect_uri=");
    }

    [Fact]
    public async Task Callback_SlackReturnsOkFalse_Returns500()
    {
        _fakeSlack.SetResponse(ok: false, error: "invalid_code");
        var response = await _client.GetAsync($"{CallbackPath}?code=bad-code");
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Callback_EmptyTeamId_Returns500()
    {
        _fakeSlack.SetResponse(ok: true, teamId: "", botToken: "xoxb-x");
        var response = await _client.GetAsync($"{CallbackPath}?code=c");
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Callback_EmptyAccessToken_Returns500()
    {
        _fakeSlack.SetResponse(ok: true, teamId: "T200", botToken: "");
        var response = await _client.GetAsync($"{CallbackPath}?code=c");
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Callback_Success_SavesTokenToStore()
    {
        _fakeSlack.SetResponse(ok: true, teamId: "T300", botToken: "xoxb-stored");
        await _client.GetAsync($"{CallbackPath}?code=abc");
        (await _tokenStore.GetAsync("T300")).Should().Be("xoxb-stored");
    }

    [Fact]
    public async Task Callback_Success_RedirectsToPostInstallUri()
    {
        // Rebuild server with PostInstallRedirectUri set
        using var server2 = BuildServerWithPostInstallUri("https://example.com/success");
        using var client2 = new HttpClient(new NoRedirectHandler(server2.CreateHandler()))
            { BaseAddress = new Uri("http://localhost") };

        _fakeSlack.SetResponse(ok: true, teamId: "T400", botToken: "xoxb-400");
        var response = await client2.GetAsync($"{CallbackPath}?code=abc");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("https://example.com/success");
    }

    [Fact]
    public async Task Callback_Success_DefaultRedirect_IsSlash()
    {
        _fakeSlack.SetResponse(ok: true, teamId: "T500", botToken: "xoxb-500");
        var response = await _client.GetAsync($"{CallbackPath}?code=abc");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");
    }

    [Fact]
    public async Task Callback_Success_TokenResolvableViaSecretResolver()
    {
        _fakeSlack.SetResponse(ok: true, teamId: "T600", botToken: "xoxb-600");
        await _client.GetAsync($"{CallbackPath}?code=abc");

        // The resolver registered in DI should now find the token
        var resolver = new TokenStoreSecretResolver(_tokenStore);
        var result   = await resolver.ResolveAsync("slack:BotToken:T600");
        result.Should().NotBeNull();
        result!.Value.Value.Should().Be("xoxb-600");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private TestServer BuildServerWithPostInstallUri(string postInstallUri)
    {
        var builder = new WebHostBuilder()
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddLogging();
                services.AddSingleton<ISlackTokenStore>(_tokenStore);
                services.AddHttpClient("slack")
                        .ConfigurePrimaryHttpMessageHandler(() => _fakeSlack);
                // PostInstallRedirectUri is init-only — supply the whole record via IOptions<T>
                services.AddSingleton(
                    Microsoft.Extensions.Options.Options.Create(
                        new SlackOAuthConfig
                        {
                            ClientId              = ClientId,
                            ClientSecret          = ClientSecret,
                            RedirectUri           = RedirectUri,
                            PostInstallRedirectUri = postInstallUri,
                        }));
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(e => e.MapSlackOAuth(InstallPath, CallbackPath));
            });
        return new TestServer(builder);
    }

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
    }
}

// ── Fake HTTP handler ──────────────────────────────────────────────────────────

/// <summary>
/// Intercepts outbound HTTP calls to <c>oauth.v2.access</c> and returns a configured response.
/// </summary>
internal sealed class FakeSlackOAuthHandler : HttpMessageHandler
{
    private bool   _ok        = true;
    private string _teamId    = "T000";
    private string _botToken  = "xoxb-fake";
    private string _error     = "";

    public Uri?    LastRequestUri  { get; private set; }
    public string? LastRequestBody { get; private set; }

    public void SetResponse(bool ok, string teamId = "", string botToken = "", string error = "")
    {
        _ok       = ok;
        _teamId   = teamId;
        _botToken = botToken;
        _error    = error;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        LastRequestUri  = request.RequestUri;
        LastRequestBody = request.Content is not null
            ? await request.Content.ReadAsStringAsync(ct)
            : null;

        var payload = _ok
            ? JsonSerializer.Serialize(new
            {
                ok           = true,
                access_token = _botToken,
                team         = new { id = _teamId, name = "Test Team" }
            })
            : JsonSerializer.Serialize(new
            {
                ok    = false,
                error = _error
            });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
    }
}

/// <summary>Handler that does not follow redirects — lets tests assert on 302 Location.</summary>
internal sealed class NoRedirectHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
        => base.SendAsync(request, ct);
}
