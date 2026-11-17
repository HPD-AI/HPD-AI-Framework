using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HPD.Agent.Studio.Tests;

public sealed class AgentStudioObservabilityTests
{
    [Fact]
    public async Task Trace_and_observability_routes_project_the_canonical_thread_journal()
    {
        var agentStore = new InMemoryAgentStore();
        Assert.True(AgentEventCompositionHost.TryComposeRegisteredModules(out AgentEventComposition composition));
        var sessionStore = new InMemorySessionStore(composition.Codec);
        Agent agent = await new AgentBuilder(new AgentConfig { Name = "observability" })
            .WithEventComposition(composition)
            .WithChatClient(new StaticChatClient())
            .WithSessionStore(sessionStore, persistAfterTurn: true)
            .WithAgentId("agent")
            .BuildAsync();
        Session session = await sessionStore.CreateSessionAsync("session");
        Thread thread = await sessionStore.CreateThreadAsync(
            session.Id, "agent", threadId: "main", name: "main");
        await agent.RunAsync("hello", session.Id, thread.Id);

        var telemetryOptions = Options.Create(new AgentStudioTelemetryOptions());
        var telemetryStore = new InMemoryAgentStudioTelemetryStore(telemetryOptions);
        var services = new ServiceCollection();
        services.AddSingleton(agent);
        services.AddSingleton<IAgentStudioTelemetryStore>(telemetryStore);
        services.AddSingleton<IAgentStudioEventPayloadAuthority, AllowPayloadAuthority>();
        services.AddSingleton<AgentManager>(provider =>
            new PrebuiltAgentManager(agentStore, provider.GetRequiredService<Agent>()));
        services.AddSingleton<SessionManager>(_ => new PrebuiltSessionManager(sessionStore));
        services.AddSingleton<IAgentDefinitionService>(provider =>
            new AgentDefinitionService(provider.GetRequiredService<AgentManager>()));
        services.AddSingleton<IAgentSessionService>(provider =>
            new AgentSessionService(
                provider.GetRequiredService<SessionManager>(),
                provider.GetRequiredService<AgentManager>(),
                "agent"));
        services.AddSingleton<IAgentThreadService>(provider =>
            new AgentThreadService(
                provider.GetRequiredService<SessionManager>(),
                provider.GetRequiredService<AgentManager>()));
        services.AddSingleton<IAgentThreadExecutionService>(provider =>
            new AgentThreadExecutionService(
                provider.GetRequiredService<SessionManager>(),
                provider.GetRequiredService<AgentManager>()));
        await using ServiceProvider provider = services.BuildServiceProvider();
        AgentServiceResult<StoredAgentDto> created =
            await provider.GetRequiredService<IAgentDefinitionService>().CreateAgentAsync(
                new CreateAgentRequest("agent", new AgentConfig { Name = "observability" }));
        Assert.Equal(AgentServiceStatus.Success, created.Status);

        var authority = new DefaultAgentStudioInspectionAuthority(provider);
        JsonElement list = await ObserveAsync(authority, "trace.list", "traces");
        JsonElement trace = Assert.Single(list.GetProperty("items").EnumerateArray()).Clone();
        string traceId = trace.GetProperty("id").GetString()!;
        Assert.Equal("completed", trace.GetProperty("state").GetString());
        Assert.Equal("session", trace.GetProperty("sessionId").GetString());
        Assert.Equal("main", trace.GetProperty("threadId").GetString());
        Assert.True(trace.GetProperty("durationMs").GetDouble() > 0);
        JsonElement scoped = await ObserveAsync(
            authority, "trace.list", "traces", "?agentId=agent&state=completed&limit=1");
        Assert.Equal(1, scoped.GetProperty("total").GetInt32());
        Assert.Single(scoped.GetProperty("items").EnumerateArray());
        JsonElement empty = await ObserveAsync(
            authority, "trace.list", "traces", "?agentId=missing");
        Assert.Equal(0, empty.GetProperty("total").GetInt32());
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        telemetryStore.Record(new AgentStudioTelemetrySpan(
            traceId,
            "1111111111111111",
            null,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "1111111111111111",
            "otel.agent.turn",
            "Internal",
            "Unset",
            DateTimeOffset.UtcNow.AddSeconds(-1),
            1_000,
            "HPD.Agent",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["agent.id"] = "agent"
            },
            []));
        JsonElement telemetryList = await ObserveAsync(
            authority, "trace.list", "traces", "?agentId=agent&limit=1");
        Assert.Equal(1_000,
            Assert.Single(telemetryList.GetProperty("items").EnumerateArray())
                .GetProperty("durationMs").GetDouble());
        telemetryStore.Record(new AgentStudioTelemetrySpan(
            "telemetry-only",
            "2222222222222222",
            null,
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            "2222222222222222",
            "agent.turn",
            "Internal",
            "completed",
            DateTimeOffset.UtcNow,
            250,
            "HPD.Agent",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["agent.id"] = "agent",
                ["agent.name"] = "observability",
                ["hpd.session_id"] = "session",
                ["hpd.thread_id"] = "main"
            },
            []));
        JsonElement telemetryOnlyList = await ObserveAsync(authority, "trace.list", "traces");
        JsonElement telemetryOnlySummary = Assert.Single(
            telemetryOnlyList.GetProperty("items").EnumerateArray().Where(item =>
                item.GetProperty("id").GetString() == "telemetry-only"));
        Assert.Equal(1, telemetryOnlySummary.GetProperty("spanCount").GetInt32());
        Assert.Equal(0, telemetryOnlySummary.GetProperty("eventCount").GetInt32());
        JsonElement telemetryOnly = await ObserveAsync(
            authority, "trace.get", "traces/telemetry-only");
        Assert.Equal("otel-activity-projection",
            telemetryOnly.GetProperty("spanSource").GetString());
        Assert.Empty(telemetryOnly.GetProperty("events").EnumerateArray());
        Assert.True(await authority.ExistsAsync(
            new HPD.AI.Platform.Studio.StudioAgentTraceResource("studio.tests", "telemetry-only"),
            CancellationToken.None));

        JsonElement detail = await ObserveAsync(
            authority, "trace.get", $"traces/{Uri.EscapeDataString(traceId)}");
        Assert.Equal("otel-activity-projection", detail.GetProperty("spanSource").GetString());
        Assert.Equal("otel.agent.turn",
            Assert.Single(detail.GetProperty("spans").EnumerateArray()).GetProperty("name").GetString());
        Assert.Contains(detail.GetProperty("events").EnumerateArray(), evt =>
            evt.GetProperty("type").GetString() == nameof(MessageTurnFinishedEvent));

        JsonElement summary = await ObserveAsync(
            authority, "observability.summary", "observability/summary");
        Assert.Equal("available", summary.GetProperty("traceQuery").GetString());
        Assert.Equal("canonical-thread-journal", summary.GetProperty("metricsSource").GetString());
        Assert.True(summary.GetProperty("traces").GetInt32() >= 1);

        JsonElement metrics = await ObserveAsync(
            authority, "metrics.query", "observability/metrics");
        Assert.Equal("canonical-thread-journal", metrics.GetProperty("source").GetString());
        Assert.Contains(metrics.GetProperty("measures").EnumerateArray(), measure =>
            measure.GetProperty("id").GetString() == "traces" &&
            measure.GetProperty("value").GetInt32() >= 1);
        Assert.True(metrics.GetProperty("capabilities").GetProperty("serverFiltering").GetBoolean());
        JsonElement filteredMetrics = await ObserveAsync(
            authority, "metrics.query", "observability/metrics",
            "?agentId=agent&state=completed");
        Assert.Contains(filteredMetrics.GetProperty("measures").EnumerateArray(), measure =>
            measure.GetProperty("id").GetString() == "traces" &&
            measure.GetProperty("value").GetInt32() >= 1);
        JsonElement emptyMetrics = await ObserveAsync(
            authority, "metrics.query", "observability/metrics", "?agentId=missing");
        Assert.Contains(emptyMetrics.GetProperty("measures").EnumerateArray(), measure =>
            measure.GetProperty("id").GetString() == "traces" &&
            measure.GetProperty("value").GetInt32() == 0);

        JsonElement events = await ObserveAsync(authority, "events.list", "events");
        Assert.Equal("canonical-thread-journal", events.GetProperty("source").GetString());
        Assert.Contains(events.GetProperty("items").EnumerateArray(), evt =>
            evt.GetProperty("type").GetString() == nameof(MessageTurnFinishedEvent));
        Assert.True(events.GetProperty("capabilities").GetProperty("serverFiltering").GetBoolean());
        JsonElement filteredEvents = await ObserveAsync(
            authority, "events.list", "events",
            $"?agentId=agent&limit=2&traceId={traceId}");
        Assert.True(filteredEvents.GetProperty("total").GetInt32() >= 1);
        Assert.True(filteredEvents.GetProperty("count").GetInt32() <= 2);
        Assert.All(filteredEvents.GetProperty("items").EnumerateArray(), item =>
        {
            Assert.Equal(traceId, item.GetProperty("traceId").GetString());
            Assert.Equal("agent", item.GetProperty("agentId").GetString());
        });
        string textDeltaId = Assert.Single(events.GetProperty("items").EnumerateArray().Where(item =>
            item.GetProperty("type").GetString() == nameof(TextDeltaEvent)))
            .GetProperty("id").GetString()!;
        JsonElement payload = await ObserveAsync(
            authority, "event.get", $"events/{Uri.EscapeDataString(textDeltaId)}");
        Assert.Equal("authorized", payload.GetProperty("state").GetString());
        Assert.Contains("Hello from the test runtime.", payload.GetProperty("payload").GetString());
    }

    private static async Task<JsonElement> ObserveAsync(
        DefaultAgentStudioInspectionAuthority authority,
        string operationId,
        string path,
        string query = "")
    {
        var response = await authority.ObserveAsync(
            operationId, path, query, "studio.tests", Principal(), CancellationToken.None);
        Assert.NotNull(response);
        Assert.Equal(200, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(response.GetBody());
        return document.RootElement.Clone();
    }

    private static ClaimsPrincipal Principal() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "studio.tests")], "test"));

    private sealed class AllowPayloadAuthority : IAgentStudioEventPayloadAuthority
    {
        public ValueTask<bool> CanInspectAsync(
            ClaimsPrincipal principal, AgentEvent agentEvent, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);
    }

    private sealed class PrebuiltAgentManager(IAgentStore store, Agent agent) : AgentManager(store)
    {
        protected override Task<Agent> BuildAgentAsync(string agentId, CancellationToken ct) =>
            Task.FromResult(agent);

        protected override TimeSpan GetIdleTimeout() => TimeSpan.FromHours(1);
    }

    private sealed class PrebuiltSessionManager(ISessionStore store) : SessionManager(store);

    private sealed class StaticChatClient : IChatClient
    {
        public ChatClientMetadata Metadata { get; } = new(
            providerName: "Studio tests",
            providerUri: null,
            defaultModelId: "static");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([
                new ChatMessage(ChatRole.Assistant, "Hello from the test runtime.")
            ])
            {
                Usage = new UsageDetails
                {
                    InputTokenCount = 7,
                    OutputTokenCount = 5,
                    TotalTokenCount = 12
                }
            });

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate
            {
                Contents = [new TextContent("Hello from the test runtime.")],
                FinishReason = ChatFinishReason.Stop
            };
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
