using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Serialization;
using Microsoft.Extensions.AI;

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

    private static string CreateInputJson(string text, AgentRunConfig? runConfig = null, string? clientInputId = null) =>
        AgentEventSerializer.ToJson(new UserMessagesInputEvent { Messages = [
            new ChatMessage(ChatRole.User, text)
        ],
            RunConfig = runConfig,
            ClientInputId = clientInputId
        });

    private Task<HttpResponseMessage> PostInputAsync(
        string sessionId,
        string threadId,
        string json,
        CancellationToken cancellationToken = default) =>
        _client.PostAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads/{threadId}/inputs",
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
    public async Task SubmitInput_PersistsDurableMessageThroughRuntime()
    {
        var sessionId = await CreateTestSession();

        var response = await PostInputAsync(
            sessionId,
            "main",
            CreateInputJson("admit this text", clientInputId: "client-input-1"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var submission = await response.Content.ReadFromJsonAsync<InputSubmissionDto>();

        submission.Should().NotBeNull();
        submission!.ThreadExecutionId.Should().NotBeNullOrWhiteSpace();

        var threadEvents = await ObserveUntilAsync(
            sessionId,
            events => events.OfType<TextMessageStartEvent>().Any(e => e.ClientInputId == "client-input-1") &&
                      events.OfType<TextDeltaEvent>().Any());

        threadEvents.Should().NotContain(e => e is UserMessagesInputEvent);
        threadEvents.OfType<TextMessageStartEvent>()
            .Should().Contain(e => e.ClientInputId == "client-input-1");
        threadEvents.Should().Contain(e => e is TextDeltaEvent);
    }

    [Fact]
    public async Task SubmitInput_UsesRunConfig_WhenProvided()
    {
        var sessionId = await CreateTestSession();
        _factory.FakeChatClient.Clear();

        var response = await PostInputAsync(sessionId, "main", CreateInputJson("Test with config", new AgentRunConfig
        {
            Clients = new AgentClientsConfig { Chat = new ChatClientConfig
            {
                Temperature = 0.7,
                MaxOutputTokens = 1000
            } },
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
    public async Task SubmitInput_AppendsMessagesToThread()
    {
        var sessionId = await CreateTestSession();

        var response = await PostInputAsync(sessionId, "main", CreateInputJson("Save this message"));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var threadEvents = await ObserveUntilAsync(
            sessionId,
            events => events.Any(e => e is TextDeltaEvent));
        threadEvents.Should().Contain(e => e is TextDeltaEvent);
    }

    [Fact]
    public async Task InterruptionInput_ReturnsStructuredNoActiveExecution()
    {
        var sessionId = await CreateTestSession();

        var interruption = new InterruptionRequestEvent(null, "stop from test", InterruptionSource.User)
        {
            AgentId = "test-agent",
            SessionId = sessionId,
            ThreadId = "main"
        };
        using var content = new StringContent(
            AgentEventSerializer.ToJson(interruption),
            System.Text.Encoding.UTF8,
            "application/json");
        var response = await _client.PostAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads/main/inputs",
            content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var result = await response.Content.ReadFromJsonAsync<InputSubmissionDto>();
        result.Should().BeEquivalentTo(new InputSubmissionDto("no_active_execution"));
    }

    [Fact]
    public async Task ThreadState_ReturnsControlStateWithoutHistoryPayload()
    {
        var sessionId = await CreateTestSession();
        var response = await PostInputAsync(
            sessionId,
            "main",
            CreateInputJson("state snapshot", clientInputId: "state-input"));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        await WaitUntilAsync(async () =>
        {
            var stateResponse = await _client.GetAsync(
                $"/agents/test-agent/sessions/{sessionId}/threads/main/state");
            var state = await stateResponse.Content.ReadFromJsonAsync<ThreadRuntimeStateDto>();
            return state is { ObservedCursor.SequenceNumber: > 0 };
        });

        var finalResponse = await _client.GetAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads/main/state");
        var json = await finalResponse.Content.ReadAsStringAsync();
        var finalState = JsonSerializer.Deserialize<ThreadRuntimeStateDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        finalState.Should().NotBeNull();
        finalState!.ObservedCursor.SequenceNumber.Should().BeGreaterThan(0);
        json.Should().NotContain("\"events\"");
    }

    [Fact]
    public async Task SubmitInput_Returns400_ForInvalidEnvelope()
    {
        var sessionId = await CreateTestSession();

        var response = await _client.PostAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads/main/inputs",
            new StringContent("{\"type\":\"NOT_AN_AGENT_INPUT\",\"text\":\"Hello\"}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SubmitInput_Returns404_WhenSessionNotFound()
    {
        var response = await _client.PostAsync(
            "/agents/test-agent/sessions/nonexistent/threads/main/inputs",
            new StringContent(CreateInputJson("Test"), Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SubmitInput_Returns404_WhenThreadNotFound()
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

    private async Task<IReadOnlyList<AgentEvent>> ObserveUntilAsync(
        string sessionId,
        Func<IReadOnlyList<AgentEvent>, bool> completed)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/agents/test-agent/sessions/{sessionId}/threads/main/events?after=1:0");
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);
        var observed = new List<AgentEvent>();

        while (!completed(observed))
        {
            var line = await reader.ReadLineAsync(timeout.Token);
            if (line is null)
                throw new EndOfStreamException("The committed event observation stream ended before the expected event arrived.");
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            observed.Add(AgentEventSerializer.DeserializeEventJson(line[6..]));
        }

        return observed;
    }
}
