using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.AspNetCore.Tests.Integration;

/// <summary>
/// Re-homed deterministic integration tests for <see cref="AgentStreamingService.SubmitInputAsync"/>
/// admission/dispatch semantics. These scenarios were originally unit tests in
/// <c>AgentStreamingServiceTests</c> built through a direct (non-DI) <c>AgentBuilder</c>, which made the
/// registered agent-event composition order/load dependent and caused the flaky
/// "configured session store does not own the selected application event codec" failure.
///
/// The ASP.NET Core harness registers the composition deterministically through DI
/// (<c>AddTestApplicationCompositions().AddHPDAgent()</c>), so each scenario here drives the real
/// HTTP input surface (<c>POST /agents/{agentId}/sessions/{sid}/threads/{bid}/inputs</c>) against the
/// same DI-resolved <see cref="AgentStreamingService"/>.
///
/// Scenarios that need a reserved-but-not-yet-running execution (steering/conflict) create that exact
/// hosting-slot state through the DI-resolved <see cref="SessionManager"/> (the same instance the
/// streaming service shares), mirroring how the original unit tests reserved an active execution
/// before submitting. The actual input dispatch under test still runs over HTTP.
/// </summary>
public sealed class AgentStreamingServiceLifecycleTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Thread = "main";
    private static readonly AgentInputCodec InputCodec = new(ProviderComposition.Create([]));

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly SessionManager _sessionManager;
    private readonly AgentManager _agentManager;
    private readonly ISessionStore _store;

    public AgentStreamingServiceLifecycleTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _sessionManager = factory.Server.Services.GetRequiredService<SessionManager>();
        _agentManager = factory.Server.Services.GetRequiredService<AgentManager>();
        _store = _sessionManager.Store;
    }

    [Fact]
    public async Task SubmitInputAsync_AllowsSelectedAgentDifferentFromThreadDefault_AndRecordsExecutingAgent()
    {
        _factory.FakeChatClient.Clear();
        _factory.FakeChatClient.EnqueueTextResponse("done");
        const string threadDefaultAgent = "default-owner";
        const string selectedAgent = "selected-executor";
        var sessionId = await CreateSessionAsync(threadDefaultAgent, "sc1-session");

        var inputJson = InputCodec.Serialize(new UserMessagesInputEvent
        {
            Messages = [new ChatMessage(ChatRole.User, "hello")]
        });
        var submitted = await SubmitAgentInputAsync(selectedAgent, sessionId, inputJson);
        submitted.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var submission = await submitted.Content.ReadFromJsonAsync<InputSubmissionDto>();
        submission!.ThreadExecutionId.Should().NotBeNullOrWhiteSpace();
        var threadExecutionId = submission.ThreadExecutionId!;

        var finished = await WaitUntilAsync(
            sessionId,
            events => events.OfType<ThreadExecutionFinishedEvent>()
                .Any(evt => evt.ThreadExecutionId == threadExecutionId),
            TimeSpan.FromSeconds(15));

        var descriptor = await _store.GetThreadAsync(new ThreadKey(sessionId, Thread));
        descriptor!.DefaultAgent.AgentId.Should().Be(threadDefaultAgent);
        descriptor.DefaultAgent.AgentId.Should().NotBe(selectedAgent);

        var started = finished.OfType<ThreadExecutionStartedEvent>()
            .Single(evt => evt.ThreadExecutionId == threadExecutionId);
        started.AgentId.Should().Be(selectedAgent, "the selected (route) agent is recorded as the executing agent");

        var terminal = finished.OfType<ThreadExecutionFinishedEvent>()
            .Single(evt => evt.ThreadExecutionId == threadExecutionId);
        terminal.Outcome.Should().Be(ThreadExecutionOutcome.Succeeded);
        terminal.Error.Should().BeNull();

        (await WaitForInactiveExecutionAsync(sessionId)).Should().BeTrue();
    }

    [Fact]
    public async Task SubmitInputAsync_RoutesSteeringBeforeNewWorkReservation()
    {
        _factory.FakeChatClient.Clear();
        const string agentId = "test-agent";
        var sessionId = await CreateSessionAsync(agentId, "sc2-session");

        // Mirror the original unit test: cache a runtime for the scope, then reserve + activate a
        // hosting execution slot that is active but has no running (steerable) turn yet.
        await _agentManager.GetOrBuildAgentRuntimeAsync(agentId, sessionId, Thread);
        _sessionManager.TryReserveThreadExecution(agentId, sessionId, Thread, out var execution)
            .Should().BeTrue();
        _sessionManager.ActivateThreadExecution(sessionId, Thread, execution.ThreadExecutionId)
            .Should().BeTrue();

        var inputJson = InputCodec.Serialize(new UserMessagesInputEvent
        {
            Delivery = AgentInputDelivery.Steer,
            ThreadExecutionId = execution.ThreadExecutionId,
            Messages = [new ChatMessage(ChatRole.User, "steer")]
        });
        var submitted = await SubmitAgentInputAsync(agentId, sessionId, inputJson);

        submitted.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var result = await submitted.Content.ReadFromJsonAsync<InputSubmissionDto>();
        result!.Disposition.Should().Be("no_active_execution");
        result.ActiveExecution!.ThreadExecutionId.Should().Be(execution.ThreadExecutionId);
    }

    [Fact]
    public async Task SubmitInputAsync_ReturnsMismatchForStaleSteeringExecutionId()
    {
        _factory.FakeChatClient.Clear();
        const string agentId = "test-agent";
        var sessionId = await CreateSessionAsync(agentId, "sc3-session");

        _sessionManager.TryReserveThreadExecution(agentId, sessionId, Thread, out var execution)
            .Should().BeTrue();
        _sessionManager.ActivateThreadExecution(sessionId, Thread, execution.ThreadExecutionId)
            .Should().BeTrue();

        var inputJson = InputCodec.Serialize(new UserMessagesInputEvent
        {
            Delivery = AgentInputDelivery.Steer,
            ThreadExecutionId = "stale-execution",
            Messages = [new ChatMessage(ChatRole.User, "steer")]
        });
        var submitted = await SubmitAgentInputAsync(agentId, sessionId, inputJson);

        submitted.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var result = await submitted.Content.ReadFromJsonAsync<InputSubmissionDto>();
        result!.Disposition.Should().Be("active_execution_mismatch");
        result.ActiveExecution!.ThreadExecutionId.Should().Be(execution.ThreadExecutionId);
    }

    [Fact]
    public async Task SubmitInputAsync_QueuedWorkStillConflictsWithActiveExecution()
    {
        _factory.FakeChatClient.Clear();
        const string agentId = "test-agent";
        var sessionId = await CreateSessionAsync(agentId, "sc4-session");

        _sessionManager.TryReserveThreadExecution(agentId, sessionId, Thread, out var execution)
            .Should().BeTrue();
        _sessionManager.ActivateThreadExecution(sessionId, Thread, execution.ThreadExecutionId)
            .Should().BeTrue();

        var inputJson = InputCodec.Serialize(new UserMessagesInputEvent
        {
            Delivery = AgentInputDelivery.Queue,
            Messages = [new ChatMessage(ChatRole.User, "queue")]
        });
        var submitted = await SubmitAgentInputAsync(agentId, sessionId, inputJson);

        submitted.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await submitted.Content.ReadAsStringAsync();
        body.Should().Contain("ThreadExecutionActive");
    }

    [Fact]
    public async Task SubmitInputAsync_ExecutesSessionControlDirectlyWithoutReservingWorkSlot()
    {
        _factory.FakeChatClient.Clear();
        const string agentId = "test-agent";
        var sessionId = await CreateSessionAsync(agentId, "sc5-session");

        var input = new AudioSessionInputEvent
        {
            ClientInputId = "audio-start-1",
            Command = new AudioSessionCommand.Start()
        };
        var inputJson = InputCodec.Serialize(input);
        var submitted = await SubmitAgentInputAsync(agentId, sessionId, inputJson);

        submitted.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var submission = await submitted.Content.ReadFromJsonAsync<InputSubmissionDto>();

        submission!.Disposition.Should().Be("completed");
        submission.ThreadExecutionId.Should().BeNull();
        var audio = submission.Result.Should().BeOfType<AgentInputResult.AudioSession>().Subject;
        var rejected = audio.Result.Should().BeOfType<AudioSessionInputResult.Rejected>().Subject;
        rejected.Disposition.Should().Be(AudioSessionInputDisposition.CapabilityNotInstalled);

        _sessionManager.GetActiveThreadExecution(sessionId, Thread).Should().BeNull(
            "a session-control input must execute directly without reserving an execution slot");
    }

    [Fact]
    public async Task ObserveThreadEvents_SubscribesBeforeRuntimeConstruction_AndReceivesLiveEventsOverSse()
    {
        _factory.FakeChatClient.Clear();
        const string agentId = "test-agent";
        const string marker = "probe-live-42";
        _factory.FakeChatClient.EnqueueStreamingResponse(marker);
        var sessionId = await CreateSessionAsync(agentId, "live-pre-runtime-session");

        // Precondition: no runtime agent exists for this scope yet. The session/thread exist but the
        // DI AgentManager has not been asked to build (GetOrBuildAgentRuntimeAsync) any runtime.
        _agentManager.GetRuntimeAgent(agentId, sessionId, Thread).Should().BeNull();

        // Establish the live subscription BEFORE the runtime is constructed. Opening the SSE stream
        // installs the observed-event inbox (ObserveThreadEventsAsync -> CreateRuntimeEventInbox) but
        // never builds a runtime; the endpoint then drains the finite journal and parks on the inbox.
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observe = ReadThreadEventsUntilMarkerAsync(_client, sessionId, Thread, marker, connected);

        // Wait until the observation is live (inbox installed, journal replayed, stream attached).
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Now build the runtime by submitting input. Dispatch calls GetOrBuildAgentRuntimeAsync, which
        // bridges the runtime EventCoordinator to the persistent hub the pre-opened inbox is subscribed
        // to; the turn's live TextDelta is produced and forwarded to the pre-existing subscription.
        var inputJson = InputCodec.Serialize(new UserMessagesInputEvent
        {
            Messages = [new ChatMessage(ChatRole.User, "produce a live delta")]
        });
        var submitted = await SubmitAgentInputAsync(agentId, sessionId, inputJson);
        submitted.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var observed = await observe;
        observed.OfType<TextDeltaEvent>()
            .Should().Contain(evt => evt.Text.Contains(marker, StringComparison.Ordinal),
                "a TextDelta produced by the turn must be delivered to the SSE subscription opened before the runtime existed");
    }

    private Task<HttpResponseMessage> SubmitAgentInputAsync(string agentId, string sessionId, string json) =>
        _client.PostAsync(
            $"/agents/{Uri.EscapeDataString(agentId)}/sessions/{sessionId}/threads/{Thread}/inputs",
            new StringContent(json, Encoding.UTF8, "application/json"));

    private static async Task<IReadOnlyList<AgentEvent>> ReadThreadEventsUntilMarkerAsync(
        HttpClient client,
        string sessionId,
        string threadId,
        string marker,
        TaskCompletionSource connected)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/agents/test-agent/sessions/{sessionId}/threads/{threadId}/events?after=1:0");
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellation.Token);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellation.Token);
        using var reader = new StreamReader(stream);
        var observed = new List<AgentEvent>();
        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync(cancellation.Token);
                if (line is null)
                    break;
                if (line.Equals(": connected", StringComparison.Ordinal))
                {
                    connected.TrySetResult();
                    continue;
                }
                if (!line.StartsWith("data: ", StringComparison.Ordinal))
                    continue;
                var evt = SseTestEventReader.DeserializeDeliveryEvent(line[6..]);
                observed.Add(evt);
                if (evt is TextDeltaEvent { Text: var text } && text.Contains(marker, StringComparison.Ordinal))
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A short timeout is also used to inspect the finite replay currently available.
        }

        return observed;
    }

    private async Task<string> CreateSessionAsync(string defaultAgentId, string sessionId)
    {
        var response = await _client.PostAsJsonAsync("/sessions", new CreateSessionRequest(defaultAgentId, sessionId));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return sessionId;
    }

    private async Task<IReadOnlyList<AgentEvent>> WaitUntilAsync(
        string sessionId,
        Func<IReadOnlyList<AgentEvent>, bool> completed,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            var events = await _store.CollectThreadEventsAsync(new ThreadKey(sessionId, Thread));
            if (events is not null && completed(events))
                return events;
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("Condition was not satisfied before the test timeout.");
            await Task.Delay(20);
        }
    }

    private async Task<bool> WaitForInactiveExecutionAsync(string sessionId)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (_sessionManager.GetActiveThreadExecution(sessionId, Thread) is not null)
        {
            if (DateTimeOffset.UtcNow >= deadline)
                return false;
            await Task.Delay(20);
        }
        return true;
    }
}
