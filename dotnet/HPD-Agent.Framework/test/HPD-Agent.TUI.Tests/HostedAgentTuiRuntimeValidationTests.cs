using System.Net;
using System.Text;
using FluentAssertions;
using HPD.Agent.TUI.Runtime;

namespace HPD.Agent.TUI.Tests;

public sealed class HostedAgentTuiRuntimeValidationTests
{
    [Theory]
    [InlineData(null, "session", "main")]
    [InlineData("", "session", "main")]
    [InlineData("agent", null, "main")]
    [InlineData("agent", "", "main")]
    [InlineData("agent", "session", null)]
    [InlineData("agent", "session", "")]
    public void RuntimeScope_RejectsMissingIds(string? agentId, string? sessionId, string? threadId)
    {
        var act = () => new AgentTuiRuntimeScope(agentId!, sessionId!, threadId!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ListSessionsAsync_RejectsMalformedSessionPayload()
    {
        using var http = new HttpClient(new JsonHandler("""
            [
              {
                "createdAt": "2026-07-09T00:00:00Z",
                "lastActivity": "2026-07-09T00:00:00Z",
                "metadata": {}
              }
            ]
            """))
        {
            BaseAddress = new Uri("http://127.0.0.1/api/hpd-agent/")
        };
        await using var runtime = new HostedAgentTuiRuntime(
            http,
            new HostedAgentTuiRuntimeOptions
            {
                BaseAddress = new Uri("http://127.0.0.1/api/hpd-agent/")
            });

        var act = () => runtime.ListSessionsAsync();

        await act.Should().ThrowAsync<System.Text.Json.JsonException>()
            .WithMessage("*id*");
    }

    [Fact]
    public async Task ListThreadsAsync_ExplainsUnknownEventPayloadAsStaleBackend()
    {
        using var http = new HttpClient(new ResponseHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""
                {"errors":{"ListThreadsError":["JSON payload is not a known agent event."]}}
                """, Encoding.UTF8, "application/json")
        }))
        {
            BaseAddress = new Uri("http://127.0.0.1/api/hpd-agent/")
        };
        await using var runtime = new HostedAgentTuiRuntime(
            http,
            new HostedAgentTuiRuntimeOptions
            {
                BaseAddress = new Uri("http://127.0.0.1/api/hpd-agent/")
            });

        var act = () => runtime.ListThreadsAsync("session-1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*restart the backend*current agent event registrations*");
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }

    private sealed class ResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(response);
    }
}
