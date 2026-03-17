using System.Text.Json;
using FluentAssertions;
using HPD.Agent.Adapters.Slack;
using HPD.Agent.Adapters.Slack.SocketMode;

namespace HPD.Agent.Adapters.Tests.Unit.SocketMode;

/// <summary>
/// Tests for <see cref="SlackSocketEnvelope"/> JSON deserialization.
/// All tests use <see cref="SlackAdapterJsonContext.Default.Options"/> to verify
/// the AOT-safe code path — the same context used by <see cref="SlackAdapter.HandleSocketEnvelopeAsync"/>.
/// </summary>
public class SlackSocketEnvelopeTests
{
    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, SlackAdapterJsonContext.Default.Options)!;

    // ── events_api envelope ────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_EventsApiEnvelope_MapsEnvelopeId()
    {
        var json = """
            {
              "envelope_id": "abc-123",
              "type": "events_api",
              "payload": { "type": "event_callback" }
            }
            """;

        var envelope = Deserialize<SlackSocketEnvelope>(json);

        envelope.EnvelopeId.Should().Be("abc-123");
    }

    [Fact]
    public void Deserialize_EventsApiEnvelope_MapsType()
    {
        var json = """
            {
              "envelope_id": "abc-123",
              "type": "events_api",
              "payload": { "type": "event_callback" }
            }
            """;

        var envelope = Deserialize<SlackSocketEnvelope>(json);

        envelope.Type.Should().Be("events_api");
    }

    [Fact]
    public void Deserialize_EventsApiEnvelope_PayloadIsPresent()
    {
        var json = """
            {
              "envelope_id": "abc-123",
              "type": "events_api",
              "payload": { "type": "event_callback", "team_id": "T123" }
            }
            """;

        var envelope = Deserialize<SlackSocketEnvelope>(json);

        envelope.Payload.Should().NotBeNull();
        envelope.Payload!.Value.GetProperty("type").GetString().Should().Be("event_callback");
    }

    // ── disconnect_warning ─────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_DisconnectWarning_TypeIsDisconnect()
    {
        var json = """
            {
              "envelope_id": "disc-001",
              "type": "disconnect",
              "reason": "warning"
            }
            """;

        var envelope = Deserialize<SlackSocketEnvelope>(json);

        envelope.Type.Should().Be("disconnect");
    }

    [Fact]
    public void Deserialize_DisconnectWarning_PayloadIsNull()
    {
        var json = """
            {
              "envelope_id": "disc-001",
              "type": "disconnect",
              "reason": "warning"
            }
            """;

        var envelope = Deserialize<SlackSocketEnvelope>(json);

        envelope.Payload.Should().BeNull();
    }

    // ── retry fields ───────────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_RetryFields_RetryAttemptPopulated()
    {
        var json = """
            {
              "envelope_id": "retry-001",
              "type": "events_api",
              "payload": {},
              "retry_attempt": 1,
              "retry_reason": "timeout"
            }
            """;

        var envelope = Deserialize<SlackSocketEnvelope>(json);

        envelope.RetryAttempt.Should().Be(1);
    }

    [Fact]
    public void Deserialize_RetryFields_RetryReasonPopulated()
    {
        var json = """
            {
              "envelope_id": "retry-001",
              "type": "events_api",
              "payload": {},
              "retry_attempt": 1,
              "retry_reason": "timeout"
            }
            """;

        var envelope = Deserialize<SlackSocketEnvelope>(json);

        envelope.RetryReason.Should().Be("timeout");
    }

    // ── missing optional fields ────────────────────────────────────────────────

    [Fact]
    public void Deserialize_MissingOptionalFields_RetryAttemptIsNull()
    {
        var json = """{"envelope_id":"e1","type":"events_api"}""";

        var envelope = Deserialize<SlackSocketEnvelope>(json);

        envelope.RetryAttempt.Should().BeNull();
    }

    [Fact]
    public void Deserialize_MissingOptionalFields_RetryReasonIsNull()
    {
        var json = """{"envelope_id":"e1","type":"events_api"}""";

        var envelope = Deserialize<SlackSocketEnvelope>(json);

        envelope.RetryReason.Should().BeNull();
    }

    [Fact]
    public void Deserialize_MissingOptionalFields_PayloadIsNull()
    {
        var json = """{"envelope_id":"e1","type":"events_api"}""";

        var envelope = Deserialize<SlackSocketEnvelope>(json);

        envelope.Payload.Should().BeNull();
    }

    // ── interactive envelope ───────────────────────────────────────────────────

    [Fact]
    public void Deserialize_InteractiveEnvelope_TypeIsInteractive()
    {
        var json = """
            {
              "envelope_id": "int-001",
              "type": "interactive",
              "payload": { "type": "block_actions" }
            }
            """;

        var envelope = Deserialize<SlackSocketEnvelope>(json);

        envelope.Type.Should().Be("interactive");
    }

    // ── record equality ────────────────────────────────────────────────────────

    [Fact]
    public void SlackSocketEnvelope_RecordEquality_SameValueAreEqual()
    {
        var a = new SlackSocketEnvelope("id1", "events_api", null, null, null);
        var b = new SlackSocketEnvelope("id1", "events_api", null, null, null);

        a.Should().Be(b);
    }
}
