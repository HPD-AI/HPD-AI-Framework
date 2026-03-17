using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent.Adapters.Slack;
using HPD.Agent.Adapters.Slack.SocketMode;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Adapters.Tests.Unit.SocketMode;

/// <summary>
/// Tests for <see cref="SlackSocketModeClient"/>.
/// HTTP calls are intercepted via a <see cref="FakeHttpMessageHandler"/> — no real network.
/// </summary>
public class SlackSocketModeClientTests
{
    // ── Fake HTTP handler ──────────────────────────────────────────────────────

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public HttpRequestMessage? LastRequest { get; private set; }

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_handler(request));
        }
    }

    private static SlackSocketModeClient BuildClient(
        FakeHttpMessageHandler handler,
        string? appToken = "xapp-test-token")
    {
        var services = new ServiceCollection();
        services.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        var config = new SlackAdapterConfig
        {
            SigningSecret = "secret",
            BotToken      = "xoxb-x",
            AppToken      = appToken,
        };

        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        return new SlackSocketModeClient(Options.Create(config), factory);
    }

    private static HttpResponseMessage OkResponse(string wssUrl) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { ok = true, url = wssUrl }),
                Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage ErrorResponse(string error) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { ok = false, error }),
                Encoding.UTF8, "application/json"),
        };

    // ── OpenConnectionUrlAsync: success ────────────────────────────────────────

    [Fact]
    public async Task OpenConnectionUrlAsync_SuccessResponse_ReturnsWssUri()
    {
        var handler = new FakeHttpMessageHandler(_ => OkResponse("wss://example.slack.com/socket"));
        var client  = BuildClient(handler);

        var uri = await client.OpenConnectionUrlAsync(CancellationToken.None);

        uri.Scheme.Should().Be("wss");
    }

    [Fact]
    public async Task OpenConnectionUrlAsync_SuccessResponse_ReturnsCorrectHost()
    {
        var handler = new FakeHttpMessageHandler(_ => OkResponse("wss://wss-primary.slack.com/socket"));
        var client  = BuildClient(handler);

        var uri = await client.OpenConnectionUrlAsync(CancellationToken.None);

        uri.Host.Should().Contain("slack.com");
    }

    [Fact]
    public async Task OpenConnectionUrlAsync_SendsPostToCorrectEndpoint()
    {
        var handler = new FakeHttpMessageHandler(_ => OkResponse("wss://localhost"));
        var client  = BuildClient(handler);

        await client.OpenConnectionUrlAsync(CancellationToken.None);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.ToString()
            .Should().Be("https://slack.com/api/apps.connections.open");
    }

    [Fact]
    public async Task OpenConnectionUrlAsync_SendsBearerAuthorizationHeader()
    {
        var handler = new FakeHttpMessageHandler(_ => OkResponse("wss://localhost"));
        var client  = BuildClient(handler, appToken: "xapp-my-token");

        await client.OpenConnectionUrlAsync(CancellationToken.None);

        handler.LastRequest!.Headers.Authorization.Should().NotBeNull();
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be("xapp-my-token");
    }

    // ── OpenConnectionUrlAsync: failure modes ──────────────────────────────────

    [Fact]
    public async Task OpenConnectionUrlAsync_SlackErrorResponse_ThrowsInvalidOperation()
    {
        var handler = new FakeHttpMessageHandler(_ => ErrorResponse("invalid_auth"));
        var client  = BuildClient(handler);

        var act = async () => await client.OpenConnectionUrlAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task OpenConnectionUrlAsync_SlackErrorResponse_MessageContainsErrorCode()
    {
        var handler = new FakeHttpMessageHandler(_ => ErrorResponse("token_revoked"));
        var client  = BuildClient(handler);

        var act = async () => await client.OpenConnectionUrlAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*token_revoked*");
    }

    [Fact]
    public async Task OpenConnectionUrlAsync_NullAppToken_ThrowsWithoutCallingHttp()
    {
        var callCount = 0;
        var handler   = new FakeHttpMessageHandler(_ => { callCount++; return OkResponse("wss://localhost"); });
        var client    = BuildClient(handler, appToken: null);

        var act = async () => await client.OpenConnectionUrlAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*AppToken*");
        callCount.Should().Be(0, "HTTP must not be called when AppToken is null");
    }

    [Fact]
    public async Task OpenConnectionUrlAsync_HttpServerError_ThrowsHttpRequestException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client  = BuildClient(handler);

        var act = async () => await client.OpenConnectionUrlAsync(CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task OpenConnectionUrlAsync_OkTrueButMissingUrl_Throws()
    {
        // {"ok":true} with no "url" field
        var handler = new FakeHttpMessageHandler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json"),
        });
        var client = BuildClient(handler);

        var act = async () => await client.OpenConnectionUrlAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── BuildAckPayload ────────────────────────────────────────────────────────

    [Fact]
    public void BuildAckPayload_ProducesEnvelopeIdJson()
    {
        var payload = SlackSocketModeClient.BuildAckPayload("abc-123");

        var json = Encoding.UTF8.GetString(payload.Span);
        json.Should().Be("{\"envelope_id\":\"abc-123\"}");
    }

    [Fact]
    public void BuildAckPayload_DifferentIds_ProduceDifferentPayloads()
    {
        var a = SlackSocketModeClient.BuildAckPayload("id-1");
        var b = SlackSocketModeClient.BuildAckPayload("id-2");

        a.Span.SequenceEqual(b.Span).Should().BeFalse();
    }

    [Fact]
    public void BuildAckPayload_Result_IsValidJson()
    {
        var payload = SlackSocketModeClient.BuildAckPayload("test-envelope");
        var json    = Encoding.UTF8.GetString(payload.Span);

        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow();
    }

    [Fact]
    public void BuildAckPayload_Result_ContainsEnvelopeIdProperty()
    {
        var payload = SlackSocketModeClient.BuildAckPayload("my-env");
        var json    = Encoding.UTF8.GetString(payload.Span);
        var doc     = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("envelope_id").GetString().Should().Be("my-env");
    }
}
