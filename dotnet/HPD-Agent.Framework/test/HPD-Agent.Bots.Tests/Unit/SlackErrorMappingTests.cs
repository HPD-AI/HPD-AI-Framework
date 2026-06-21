using FluentAssertions;
using HPD.Agent.Bots;
using HPD.Agent.Bots.Contracts;
using HPD.Agent.Bots.Slack;

namespace HPD.Agent.Bots.Tests.Unit;

/// <summary>
/// Tests for <see cref="SlackErrorHandler"/> — verifies that Slack API error codes
/// are correctly mapped to HPD adapter exceptions.
/// Only tests error codes explicitly mapped in SlackErrorHandler.
/// </summary>
public class SlackErrorMappingTests
{
    // ── Permission Errors ─────────────────────────────────────────────────

    [Fact]
    public void ErrorHandler_NotInChannel_ThrowsPermissionException()
    {
        var action = () => SlackErrorHandler.ThrowMapped(
            "not_in_channel",
            new HttpRequestException("Slack API error: not_in_channel"));

        action.Should().Throw<BotPermissionException>();
    }

    [Fact]
    public void ErrorHandler_IsArchived_ThrowsPermissionException()
    {
        var action = () => SlackErrorHandler.ThrowMapped(
            "is_archived",
            new HttpRequestException("Slack API error: is_archived"));

        action.Should().Throw<BotPermissionException>();
    }

    [Fact]
    public void ErrorHandler_MissingScope_ThrowsPermissionException()
    {
        var action = () => SlackErrorHandler.ThrowMapped(
            "missing_scope",
            new HttpRequestException("Slack API error: missing_scope"));

        action.Should().Throw<BotPermissionException>();
    }

    // ── Not Found Errors ──────────────────────────────────────────────────

    [Fact]
    public void ErrorHandler_ChannelNotFound_ThrowsNotFoundException()
    {
        var action = () => SlackErrorHandler.ThrowMapped(
            "channel_not_found",
            new HttpRequestException("Slack API error: channel_not_found"));

        action.Should().Throw<BotNotFoundException>();
    }

    // ── Rate Limit Errors ─────────────────────────────────────────────────

    [Fact]
    public void ErrorHandler_Ratelimited_ThrowsRateLimitException()
    {
        var action = () => SlackErrorHandler.ThrowMapped(
            "ratelimited",
            new HttpRequestException("Slack API error: ratelimited"));

        action.Should().Throw<BotRateLimitException>();
    }

    // ── Authentication Errors ────────────────────────────────────────────

    [Fact]
    public void ErrorHandler_InvalidAuth_ThrowsAuthenticationException()
    {
        var action = () => SlackErrorHandler.ThrowMapped(
            "invalid_auth",
            new HttpRequestException("Slack API error: invalid_auth"));

        action.Should().Throw<BotAuthenticationException>();
    }

    [Fact]
    public void ErrorHandler_TokenRevoked_ThrowsAuthenticationException()
    {
        var action = () => SlackErrorHandler.ThrowMapped(
            "token_revoked",
            new HttpRequestException("Slack API error: token_revoked"));

        action.Should().Throw<BotAuthenticationException>();
    }

    [Fact]
    public void ErrorHandler_AccountInactive_ThrowsAuthenticationException()
    {
        var action = () => SlackErrorHandler.ThrowMapped(
            "account_inactive",
            new HttpRequestException("Slack API error: account_inactive"));

        action.Should().Throw<BotAuthenticationException>();
    }

    // ── Theory: Permission Errors ────────────────────────────────────────

    [Theory]
    [InlineData("not_in_channel")]
    [InlineData("is_archived")]
    [InlineData("missing_scope")]
    public void ErrorHandler_AllPermissionErrors_ThrowPermissionException(string errorCode)
    {
        var action = () => SlackErrorHandler.ThrowMapped(
            errorCode,
            new HttpRequestException($"Slack API error: {errorCode}"));

        action.Should().Throw<BotPermissionException>();
    }

    // ── Theory: Authentication Errors ────────────────────────────────────

    [Theory]
    [InlineData("invalid_auth")]
    [InlineData("token_revoked")]
    [InlineData("account_inactive")]
    public void ErrorHandler_AllAuthErrors_ThrowAuthenticationException(string errorCode)
    {
        var action = () => SlackErrorHandler.ThrowMapped(
            errorCode,
            new HttpRequestException($"Slack API error: {errorCode}"));

        action.Should().Throw<BotAuthenticationException>();
    }

    // ── Unmapped Error Handling ───────────────────────────────────────────

    [Fact]
    public void ErrorHandler_UnmappedErrorCode_RethrowsInnerException()
    {
        var innerException = new HttpRequestException("Slack API error: unknown_code");
        var action = () => SlackErrorHandler.ThrowMapped(
            "unknown_code",
            innerException);

        action.Should().Throw<HttpRequestException>().Which.Should().Be(innerException);
    }
}
