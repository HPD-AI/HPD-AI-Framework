using FluentAssertions;
using HPD.Agent.Adapters.Contracts;
using HPD.Agent.Adapters.Slack;

namespace HPD.Agent.Adapters.Tests.Unit;

/// <summary>
/// Tests for <see cref="SlackErrorHandler"/> — verifies that every Slack Web API error code
/// declared via <c>[ErrorCode]</c> is mapped to the correct <see cref="AdapterException"/> subtype.
/// The <c>ThrowMapped</c> method is emitted by the source generator.
/// </summary>
public class SlackErrorHandlerTests
{
    private static readonly Exception Inner = new InvalidOperationException("slack api error");

    // ── Permission errors ─────────────────────────────────────────────

    [Fact]
    public void ThrowMapped_NotInChannel_ThrowsAdapterPermissionException()
    {
        var act = () => SlackErrorHandler.ThrowMapped("not_in_channel", Inner);

        act.Should().Throw<AdapterPermissionException>();
    }

    [Fact]
    public void ThrowMapped_IsArchived_ThrowsAdapterPermissionException()
    {
        var act = () => SlackErrorHandler.ThrowMapped("is_archived", Inner);

        act.Should().Throw<AdapterPermissionException>();
    }

    [Fact]
    public void ThrowMapped_MissingScope_ThrowsAdapterPermissionException()
    {
        var act = () => SlackErrorHandler.ThrowMapped("missing_scope", Inner);

        act.Should().Throw<AdapterPermissionException>();
    }

    // ── Not-found errors ──────────────────────────────────────────────

    [Fact]
    public void ThrowMapped_ChannelNotFound_ThrowsAdapterNotFoundException()
    {
        var act = () => SlackErrorHandler.ThrowMapped("channel_not_found", Inner);

        act.Should().Throw<AdapterNotFoundException>();
    }

    // ── Rate-limit errors ─────────────────────────────────────────────

    [Fact]
    public void ThrowMapped_Ratelimited_ThrowsAdapterRateLimitException()
    {
        var act = () => SlackErrorHandler.ThrowMapped("ratelimited", Inner);

        act.Should().Throw<AdapterRateLimitException>();
    }

    // ── Authentication errors ─────────────────────────────────────────

    [Fact]
    public void ThrowMapped_InvalidAuth_ThrowsAdapterAuthenticationException()
    {
        var act = () => SlackErrorHandler.ThrowMapped("invalid_auth", Inner);

        act.Should().Throw<AdapterAuthenticationException>();
    }

    [Fact]
    public void ThrowMapped_TokenRevoked_ThrowsAdapterAuthenticationException()
    {
        var act = () => SlackErrorHandler.ThrowMapped("token_revoked", Inner);

        act.Should().Throw<AdapterAuthenticationException>();
    }

    [Fact]
    public void ThrowMapped_AccountInactive_ThrowsAdapterAuthenticationException()
    {
        var act = () => SlackErrorHandler.ThrowMapped("account_inactive", Inner);

        act.Should().Throw<AdapterAuthenticationException>();
    }

    // ── All mapped exceptions are AdapterException ────────────────────

    [Theory]
    [InlineData("not_in_channel")]
    [InlineData("is_archived")]
    [InlineData("missing_scope")]
    [InlineData("channel_not_found")]
    [InlineData("ratelimited")]
    [InlineData("invalid_auth")]
    [InlineData("token_revoked")]
    [InlineData("account_inactive")]
    public void ThrowMapped_AllKnownCodes_ThrowAdapterException(string errorCode)
    {
        var act = () => SlackErrorHandler.ThrowMapped(errorCode, Inner);

        act.Should().Throw<AdapterException>();
    }
}
