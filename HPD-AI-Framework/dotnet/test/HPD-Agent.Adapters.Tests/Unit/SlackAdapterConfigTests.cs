using FluentAssertions;
using HPD.Agent.Adapters.Slack;

namespace HPD.Agent.Adapters.Tests.Unit;

/// <summary>
/// Tests for <see cref="SlackAdapterConfig"/> — verifies required-field validation,
/// whitespace trimming on init, and default values for optional settings.
/// </summary>
public class SlackAdapterConfigTests
{
    // ── Required field validation ─────────────────────────────────────

    [Fact]
    public void SigningSecret_Null_ThrowsArgumentNullException()
    {
        var act = () => new SlackAdapterConfig
        {
            SigningSecret = null!,
            BotToken      = "xoxb-token",
        };

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BotToken_Null_ThrowsArgumentNullException()
    {
        var act = () => new SlackAdapterConfig
        {
            SigningSecret = "signing-secret",
            BotToken      = null!,
        };

        act.Should().Throw<ArgumentNullException>();
    }

    // ── Whitespace trimming ───────────────────────────────────────────

    [Fact]
    public void SigningSecret_WithLeadingTrailingSpaces_IsTrimmed()
    {
        var config = new SlackAdapterConfig
        {
            SigningSecret = "  my-secret  ",
            BotToken      = "xoxb-token",
        };

        config.SigningSecret.Should().Be("my-secret");
    }

    [Fact]
    public void BotToken_WithLeadingTrailingSpaces_IsTrimmed()
    {
        var config = new SlackAdapterConfig
        {
            SigningSecret = "secret",
            BotToken      = "  xoxb-abc-123  ",
        };

        config.BotToken.Should().Be("xoxb-abc-123");
    }

    // ── Default values ────────────────────────────────────────────────

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var config = new SlackAdapterConfig
        {
            SigningSecret = "s",
            BotToken      = "t",
        };

        config.StreamingDebounceMs.Should().Be(500);
        config.PermissionTimeout.Should().Be(TimeSpan.FromMinutes(5));
        config.UseNativeStreaming.Should().BeFalse();
        config.BotUserId.Should().BeNull();
        config.AgentName.Should().BeNull();
    }

    // ── Optional fields ───────────────────────────────────────────────

    [Fact]
    public void OptionalFields_CanBeSetExplicitly()
    {
        var config = new SlackAdapterConfig
        {
            SigningSecret      = "s",
            BotToken           = "t",
            BotUserId          = "U12345",
            AgentName          = "my-agent",
            StreamingDebounceMs = 250,
            PermissionTimeout  = TimeSpan.FromMinutes(2),
            UseNativeStreaming  = true,
        };

        config.BotUserId.Should().Be("U12345");
        config.AgentName.Should().Be("my-agent");
        config.StreamingDebounceMs.Should().Be(250);
        config.PermissionTimeout.Should().Be(TimeSpan.FromMinutes(2));
        config.UseNativeStreaming.Should().BeTrue();
    }

}
