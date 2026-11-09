using System.Net;
using FluentAssertions;
using HPD.Agent.Bots;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Bots.Tests.Unit.EndpointMapping;

public sealed class GeneratedWebhookEndpointTests : IDisposable
{
    private readonly TestServer _server;
    private readonly HttpClient _client;

    public GeneratedWebhookEndpointTests()
    {
        var builder = new WebHostBuilder()
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<SmokeBot>();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapSmokeWebhook());
            });

        _server = new TestServer(builder);
        _client = _server.CreateClient();
    }

    [Fact]
    public async Task MappedWebhook_WritesGeneratedStatusBodyAndHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/smoke")
        {
            Content = new StringContent("{}"),
        };
        request.Headers.Add("x-smoke-result", "ok");

        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.GetValues("x-smoke-result").Should().ContainSingle().Which.Should().Be("ok");
        (await response.Content.ReadAsStringAsync()).Should().Be("webhook-ok");
    }

    [Fact]
    public async Task MappedWebhook_PropagatesGeneratedFailureStatus()
    {
        using var response = await _client.PostAsync("/webhooks/smoke", new StringContent("{}"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
    }
}

[HpdBot("smoke")]
[HpdHttpMethods("GET", "POST")]
public partial class SmokeBot
{
    [HpdBotPreDispatch]
    private static Task<BotAdapterResponse?> HandlePreDispatchAsync(
        BotRequestContext context,
        byte[] bodyBytes)
    {
        _ = bodyBytes;

        if (!string.Equals(context.Header("x-smoke-result"), "ok", StringComparison.Ordinal))
            return Task.FromResult<BotAdapterResponse?>(BotAdapterResponse.Status(401));

        return Task.FromResult<BotAdapterResponse?>(new BotAdapterResponse
        {
            StatusCode = 202,
            ContentType = "text/plain",
            Body = "webhook-ok"u8.ToArray(),
            Headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["x-smoke-result"] = ["ok"],
            },
        });
    }
}

public sealed class SmokeBotConfig
{
    public string? AgentId { get; set; }
}
