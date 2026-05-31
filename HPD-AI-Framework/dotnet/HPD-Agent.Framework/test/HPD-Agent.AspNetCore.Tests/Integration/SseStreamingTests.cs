using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Data;

namespace HPD.Agent.AspNetCore.Tests.Integration;

/// <summary>
/// Integration tests for runtime-owned input submission and observer-only SSE.
/// </summary>
public class SseStreamingTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;

    public SseStreamingTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> CreateTestSession()
    {
        var response = await _client.PostAsync("/sessions", null);
        var session = await response.Content.ReadFromJsonAsync<SessionDto>();
        return session!.Id;
    }

    private static string CreateInputJson(string text, AgentRunConfig? runConfig = null) =>
        JsonSerializer.Serialize(new StreamTextRequest(text, runConfig));

    private Task<HttpResponseMessage> PostInputAsync(
        string sessionId,
        string branchId,
        string json,
        CancellationToken cancellationToken = default) =>
        _client.PostAsync(
            $"/agents/test-agent/sessions/{sessionId}/branches/{branchId}/inputs",
            new StringContent(json, Encoding.UTF8, "application/json"),
            cancellationToken);

    [Fact]
    public async Task SubmitInput_ReturnsAccepted()
    {
        var sessionId = await CreateTestSession();

        var response = await PostInputAsync(sessionId, "main", CreateInputJson("Hello"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task SubmitInput_UsesRunConfig_WhenProvided()
    {
        var sessionId = await CreateTestSession();
        _factory.FakeChatClient.Clear();

        var response = await PostInputAsync(sessionId, "main", CreateInputJson("Test with config", new AgentRunConfig
        {
            Chat = new ChatRunConfig
            {
                Temperature = 0.7,
                MaxOutputTokens = 1000
            },
            AdditionalSystemInstructions = "Be concise",
            CoalesceDeltas = true,
            SkipTools = false
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        await WaitUntilAsync(() => _factory.FakeChatClient.CapturedOptions.Any(
            options => options?.Temperature == 0.7f && options.MaxOutputTokens == 1000));
        var options = _factory.FakeChatClient.CapturedOptions.Single(
            options => options?.Temperature == 0.7f && options.MaxOutputTokens == 1000);
        options.Should().NotBeNull();
        options!.Temperature.Should().Be(0.7f);
        options.MaxOutputTokens.Should().Be(1000);
        options.Instructions.Should().Contain("Be concise");
    }

    [Fact]
    public async Task SubmitInput_AppendsMessagesToBranch()
    {
        var sessionId = await CreateTestSession();

        var response = await PostInputAsync(sessionId, "main", CreateInputJson("Save this message"));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        await WaitUntilAsync(async () =>
        {
            var eventsResponse = await _client.GetAsync($"/sessions/{sessionId}/branches/main/events");
            using var events = JsonDocument.Parse(await eventsResponse.Content.ReadAsStringAsync());
            return events.RootElement.EnumerateArray()
                .Any(e => e.GetProperty("type").GetString() == "TEXT_DELTA");
        });
    }

    [Fact]
    public async Task Interrupt_ReturnsConflict_WhenBranchHasNoActiveRun()
    {
        var sessionId = await CreateTestSession();

        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/branches/main/interrupt",
            new { reason = "stop from test" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SubmitInput_Returns400_ForInvalidEnvelope()
    {
        var sessionId = await CreateTestSession();

        var response = await _client.PostAsync(
            $"/agents/test-agent/sessions/{sessionId}/branches/main/inputs",
            new StringContent("{\"type\":\"NOT_AN_AGENT_INPUT\",\"text\":\"Hello\"}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SubmitInput_Returns404_WhenSessionNotFound()
    {
        var response = await _client.PostAsync(
            "/agents/test-agent/sessions/nonexistent/branches/main/inputs",
            new StringContent(CreateInputJson("Test"), Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SubmitInput_Returns404_WhenBranchNotFound()
    {
        var sessionId = await CreateTestSession();

        var response = await PostInputAsync(sessionId, "nonexistent", CreateInputJson("Test"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 50; i++)
        {
            if (condition())
                return;

            await Task.Delay(50);
        }

        condition().Should().BeTrue();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 50; i++)
        {
            if (await condition())
                return;

            await Task.Delay(50);
        }

        (await condition()).Should().BeTrue();
    }
}
