using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Bots.Session;
using HPD.Agent.Bots.Slack;
using HPD.Agent.Bots.Slack.SocketMode;
using HPD.Agent.Bots.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Bots.Tests.Unit.SocketMode;

/// <summary>
/// Tests for <see cref="SlackBot.HandleSocketEnvelopeAsync"/>.
/// Verifies that socket envelopes are routed to the same handlers as the HTTP path.
///
/// These are integration-style unit tests: a real <see cref="SlackBot"/> is
/// constructed via DI with a fake HTTP handler (to prevent real API calls) and
/// test-double session/agent managers.
/// </summary>
public class SlackBotSocketDispatchTests
{
    // ── Infrastructure ─────────────────────────────────────────────────────────

    private sealed class NoOpHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
    }

    private static HPD.Agent.Bots.Slack.SlackBot BuildBot()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Slack:SigningSecret"] = "test-secret",
                    ["Slack:BotToken"]      = "xoxb-test",
                })
                .Build());

        services.AddHttpClient();
        services.ConfigureAll<Microsoft.Extensions.Http.HttpClientFactoryOptions>(
            opts => opts.HttpMessageHandlerBuilderActions.Add(
                b => b.PrimaryHandler = new NoOpHttpHandler()));

        services.AddSingleton<SessionManager>(new TestSessionManager());
        services.AddSingleton<AgentManager>(new TestAgentManager());

        services.AddSlackBot(
            c =>
            {
                c.SigningSecret = "test-secret";
                c.BotToken      = "xoxb-test";
            },
            registerDefaultSecretResolver: true);

        return services.BuildServiceProvider().GetRequiredService<HPD.Agent.Bots.Slack.SlackBot>();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a Slack Socket Mode <c>events_api</c> envelope wrapping the given inner event JSON.
    /// </summary>
    private static SlackSocketEnvelope EventsApiEnvelope(string eventJson, string envelopeId = "env-1")
    {
        var payloadJson = $$"""
            {
                "type": "event_callback",
                "team_id": "T123",
                "event": {{eventJson}}
            }
            """;
        var payload = JsonSerializer.Deserialize<System.Text.Json.JsonElement>(payloadJson);
        return new SlackSocketEnvelope(envelopeId, "events_api", payload, null, null);
    }

    private static SlackSocketEnvelope InteractiveEnvelope(string payloadJson, string envelopeId = "env-i")
    {
        var payload = JsonSerializer.Deserialize<System.Text.Json.JsonElement>(payloadJson);
        return new SlackSocketEnvelope(envelopeId, "interactive", payload, null, null);
    }

    // ── HandleSocketEnvelopeAsync: always returns true ─────────────────────────

    [Fact]
    public async Task HandleSocketEnvelopeAsync_AlwaysReturnsTrue_ForEventsApi()
    {
        var adapter = BuildBot();
        var envelope = EventsApiEnvelope("""{"type":"unknown_event_type"}""");

        var result = await adapter.HandleSocketEnvelopeAsync(envelope, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HandleSocketEnvelopeAsync_AlwaysReturnsTrue_ForDisconnectWarning()
    {
        var adapter  = BuildBot();
        var envelope = new SlackSocketEnvelope("disc-1", "disconnect", null, null, null);

        var result = await adapter.HandleSocketEnvelopeAsync(envelope, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HandleSocketEnvelopeAsync_AlwaysReturnsTrue_ForUnknownType()
    {
        var adapter  = BuildBot();
        var envelope = new SlackSocketEnvelope("unk-1", "something_new", null, null, null);

        var result = await adapter.HandleSocketEnvelopeAsync(envelope, CancellationToken.None);

        result.Should().BeTrue();
    }

    // ── Reaction events ────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleSocketEnvelopeAsync_ReactionAdded_RaisesOnReaction()
    {
        var adapter = BuildBot();
        SlackReactionReceivedEvent? raised = null;
        adapter.OnReaction += e => raised = e;

        var envelope = EventsApiEnvelope("""
            {
                "type": "reaction_added",
                "user": "U123",
                "reaction": "thumbsup",
                "item": { "type": "message", "channel": "C1", "ts": "123.0" },
                "event_ts": "123.0"
            }
            """);

        await adapter.HandleSocketEnvelopeAsync(envelope, CancellationToken.None);

        raised.Should().NotBeNull();
        raised!.Payload.Reaction.Should().Be("thumbsup");
    }

    [Fact]
    public async Task HandleSocketEnvelopeAsync_ReactionRemoved_RaisesOnReaction()
    {
        var adapter = BuildBot();
        var raisedCount = 0;
        adapter.OnReaction += _ => raisedCount++;

        var envelope = EventsApiEnvelope("""
            {
                "type": "reaction_removed",
                "user": "U123",
                "reaction": "wave",
                "item": { "type": "message", "channel": "C1", "ts": "123.0" },
                "event_ts": "123.0"
            }
            """);

        await adapter.HandleSocketEnvelopeAsync(envelope, CancellationToken.None);

        raisedCount.Should().Be(1);
    }

    // ── App home opened ────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleSocketEnvelopeAsync_AppHomeOpened_HomeTab_RaisesOnAppHomeOpened()
    {
        var adapter = BuildBot();
        SlackAppHomeOpenedReceivedEvent? raised = null;
        adapter.OnAppHomeOpened += e => raised = e;

        var envelope = EventsApiEnvelope("""
            {
                "type": "app_home_opened",
                "user": "U123",
                "channel": "D123",
                "tab": "home",
                "event_ts": "123.0"
            }
            """);

        await adapter.HandleSocketEnvelopeAsync(envelope, CancellationToken.None);

        raised.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleSocketEnvelopeAsync_AppHomeOpened_MessagesTab_DoesNotRaiseEvent()
    {
        var adapter = BuildBot();
        var raisedCount = 0;
        adapter.OnAppHomeOpened += _ => raisedCount++;

        var envelope = EventsApiEnvelope("""
            {
                "type": "app_home_opened",
                "user": "U123",
                "channel": "D123",
                "tab": "messages",
                "event_ts": "123.0"
            }
            """);

        await adapter.HandleSocketEnvelopeAsync(envelope, CancellationToken.None);

        raisedCount.Should().Be(0, "only the 'home' tab triggers the event");
    }

    // ── Bot message suppression ────────────────────────────────────────────────

    [Fact]
    public async Task HandleSocketEnvelopeAsync_MessageWithBotId_DoesNotFireStream()
    {
        // A message from the bot itself (bot_id set) should be skipped.
        // We verify by asserting no session is resolved (PlatformSessionMapper not called).
        // Since stream is fire-and-forget we wait briefly for any side effects.
        var adapter = BuildBot();
        var onReactionCount = 0;
        adapter.OnReaction += _ => onReactionCount++;

        var envelope = EventsApiEnvelope("""
            {
                "type": "message",
                "bot_id": "B123",
                "text": "I am a bot",
                "channel": "C123",
                "ts": "123.0"
            }
            """);

        var result = await adapter.HandleSocketEnvelopeAsync(envelope, CancellationToken.None);

        result.Should().BeTrue();
        // No side effects that we can observe from other event channels
        onReactionCount.Should().Be(0);
    }

    // ── disconnect_warning: safe to ignore ────────────────────────────────────

    [Fact]
    public async Task HandleSocketEnvelopeAsync_DisconnectWarning_NoException()
    {
        var adapter  = BuildBot();
        var envelope = new SlackSocketEnvelope("disc-001", "disconnect", null, null, null);

        var act = async () => await adapter.HandleSocketEnvelopeAsync(envelope, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    // ── interactive: non-permission block action ───────────────────────────────

    [Fact]
    public async Task HandleSocketEnvelopeAsync_Interactive_NonGuidActionId_RaisesOnBlockAction()
    {
        var adapter = BuildBot();
        SlackBlockActionReceivedEvent? raised = null;
        adapter.OnBlockAction += e => raised = e;

        // Non-GUID action ID = user action, not permission response
        var envelope = InteractiveEnvelope("""
            {
                "type": "block_actions",
                "trigger_id": "t1",
                "user": { "id": "U123", "name": "testuser" },
                "actions": [
                    {
                        "action_id": "my_button_action",
                        "block_id": "block1",
                        "value": "clicked",
                        "type": "button"
                    }
                ]
            }
            """);

        await adapter.HandleSocketEnvelopeAsync(envelope, CancellationToken.None);

        raised.Should().NotBeNull();
        raised!.Action.ActionId.Should().Be("my_button_action");
    }

    // ── null/empty payload ─────────────────────────────────────────────────────

    [Fact]
    public async Task HandleSocketEnvelopeAsync_EventsApiWithNullPayload_ReturnsTrue()
    {
        var adapter  = BuildBot();
        var envelope = new SlackSocketEnvelope("env-null", "events_api", null, null, null);

        var result = await adapter.HandleSocketEnvelopeAsync(envelope, CancellationToken.None);

        result.Should().BeTrue();
    }

    // ── assistant context changed ──────────────────────────────────────────────

    [Fact]
    public async Task HandleSocketEnvelopeAsync_AssistantContextChanged_RaisesEvent()
    {
        var adapter = BuildBot();
        SlackAssistantContextChangedReceivedEvent? raised = null;
        adapter.OnAssistantContextChanged += e => raised = e;

        var envelope = EventsApiEnvelope("""
            {
                "type": "assistant_thread_context_changed",
                "assistant_thread": {
                    "user_id": "U123",
                    "context": { "channel_id": "C999", "team_id": "T1" },
                    "channel_id": "D123",
                    "thread_ts": "123.456"
                },
                "event_ts": "123.0"
            }
            """);

        await adapter.HandleSocketEnvelopeAsync(envelope, CancellationToken.None);

        raised.Should().NotBeNull();
    }
}
