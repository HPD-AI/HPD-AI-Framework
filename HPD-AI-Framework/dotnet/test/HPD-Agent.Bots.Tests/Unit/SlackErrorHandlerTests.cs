using FluentAssertions;
using HPD.Agent.Bots.Contracts;
using HPD.Agent.Bots.Slack;

namespace HPD.Agent.Bots.Tests.Unit;

/// <summary>
/// Tests for <see cref="SlackErrorHandler"/> — verifies that every Slack Web API error code
/// declared via <c>[ErrorCode]</c> is mapped to the correct <see cref="BotException"/> subtype.
/// The <c>ThrowMapped</c> method is emitted by the source generator.
/// </summary>
public class SlackErrorHandlerTests
{
    private static readonly Exception Inner = new InvalidOperationException("slack api error");

    // ── Permission errors ─────────────────────────────────────────────

    [Fact]
    public void ThrowMapped_NotInChannel_ThrowsBotPermissionException()
    {
        var act = () => SlackErrorHandler.ThrowMapped("not_in_channel", Inner);

        act.Should().Throw<BotPermissionException>();
    }

    [Fact]
    public void ThrowMapped_IsArchived_ThrowsBotPermissionException()
    {
        var act = () => SlackErrorHandler.ThrowMapped("is_archived", Inner);

        act.Should().Throw<BotPermissionException>();
    }

    [Fact]
    public void ThrowMapped_MissingScope_ThrowsBotPermissionException()
    {
        var act = () => SlackErrorHandler.ThrowMapped("missing_scope", Inner);

        act.Should().Throw<BotPermissionException>();
    }

    // ── Not-found errors ──────────────────────────────────────────────

    [Fact]
    public void ThrowMapped_ChannelNotFound_ThrowsBotNotFoundException()
    {
        var act = () => SlackErrorHandler.ThrowMapped("channel_not_found", Inner);

        act.Should().Throw<BotNotFoundException>();
    }

    // ── Rate-limit errors ─────────────────────────────────────────────

    [Fact]
    public void ThrowMapped_Ratelimited_ThrowsBotRateLimitException()
    {
        var act = () => SlackErrorHandler.ThrowMapped("ratelimited", Inner);

        act.Should().Throw<BotRateLimitException>();
    }

    // ── Authentication errors ─────────────────────────────────────────

    [Fact]
    public void ThrowMapped_InvalidAuth_ThrowsBotAuthenticationException()
    {
        var act = () => SlackErrorHandler.ThrowMapped("invalid_auth", Inner);

        act.Should().Throw<BotAuthenticationException>();
    }

    [Fact]
    public void ThrowMapped_TokenRevoked_ThrowsBotAuthenticationException()
    {
        var act = () => SlackErrorHandler.ThrowMapped("token_revoked", Inner);

        act.Should().Throw<BotAuthenticationException>();
    }

    [Fact]
    public void ThrowMapped_AccountInactive_ThrowsBotAuthenticationException()
    {
        var act = () => SlackErrorHandler.ThrowMapped("account_inactive", Inner);

        act.Should().Throw<BotAuthenticationException>();
    }

    // ── All mapped exceptions are BotException ────────────────────

    [Theory]
    [InlineData("not_in_channel")]
    [InlineData("is_archived")]
    [InlineData("missing_scope")]
    [InlineData("channel_not_found")]
    [InlineData("ratelimited")]
    [InlineData("invalid_auth")]
    [InlineData("token_revoked")]
    [InlineData("account_inactive")]
    public void ThrowMapped_AllKnownCodes_ThrowBotException(string errorCode)
    {
        var act = () => SlackErrorHandler.ThrowMapped(errorCode, Inner);

        act.Should().Throw<BotException>();
    }
}
