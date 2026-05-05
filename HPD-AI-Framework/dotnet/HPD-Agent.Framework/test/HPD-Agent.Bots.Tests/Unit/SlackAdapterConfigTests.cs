using FluentAssertions;
using HPD.Agent.Bots.Slack;

namespace HPD.Agent.Bots.Tests.Unit;

/// <summary>
/// Tests for <see cref="SlackBotConfig"/> — verifies required-field validation,
/// whitespace trimming on init, and default values for optional settings.
/// </summary>
public class SlackBotConfigTests
{
    // ── Required field validation ─────────────────────────────────────

    [Fact]
    public void SigningSecret_Null_ThrowsArgumentNullException()
    {
        var act = () => new SlackBotConfig
        {
            SigningSecret = null!,
            BotToken      = "xoxb-token",
        };

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BotToken_Null_ThrowsArgumentNullException()
    {
        var act = () => new SlackBotConfig
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
        var config = new SlackBotConfig
        {
            SigningSecret = "  my-secret  ",
            BotToken      = "xoxb-token",
        };

        config.SigningSecret.Should().Be("my-secret");
    }

    [Fact]
    public void BotToken_WithLeadingTrailingSpaces_IsTrimmed()
    {
        var config = new SlackBotConfig
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
        var config = new SlackBotConfig
        {
            SigningSecret = "s",
            BotToken      = "t",
        };

        config.StreamingDebounceMs.Should().BeNull();
        config.PermissionTimeout.Should().Be(TimeSpan.FromMinutes(5));
        config.UseNativeStreaming.Should().BeFalse();
        config.BotUserId.Should().BeNull();
        config.AgentName.Should().BeNull();
    }

    // ── Optional fields ───────────────────────────────────────────────

    [Fact]
    public void OptionalFields_CanBeSetExplicitly()
    {
        var config = new SlackBotConfig
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
