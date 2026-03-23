using HPD.Agent.Bots.Contracts;

namespace HPD.Agent.Bots.Slack;

/// <summary>
/// Maps Slack Web API error codes to typed <see cref="BotException"/> subclasses.
/// <c>ThrowMapped</c> will eventually be emitted by the <c>[BotErrors]</c> source generator;
/// implemented here manually until the generator is ready.
/// </summary>
[BotErrors("slack")]
[ErrorCode("not_in_channel",    typeof(BotPermissionException))]
[ErrorCode("channel_not_found", typeof(BotNotFoundException))]
[ErrorCode("is_archived",       typeof(BotPermissionException))]
[ErrorCode("ratelimited",       typeof(BotRateLimitException))]
[ErrorCode("invalid_auth",      typeof(BotAuthenticationException))]
[ErrorCode("token_revoked",     typeof(BotAuthenticationException))]
[ErrorCode("missing_scope",     typeof(BotPermissionException))]
[ErrorCode("account_inactive",  typeof(BotAuthenticationException))]
public partial class SlackErrorHandler
{
    // ── Error mapping (to be replaced by [BotErrors] source generator output) ──

    /// <summary>
    /// Throws the <see cref="BotException"/> subtype that corresponds to
    /// <paramref name="slackErrorCode"/>, wrapping <paramref name="inner"/> as the cause.
    /// Unmapped codes re-throw <paramref name="inner"/> directly.
    /// </summary>
    public static void ThrowMapped(string slackErrorCode, Exception inner)
    {
        throw slackErrorCode switch
        {
            "not_in_channel"    => new BotPermissionException(slackErrorCode, inner),
            "channel_not_found" => new BotNotFoundException(slackErrorCode, inner),
            "is_archived"       => new BotPermissionException(slackErrorCode, inner),
            "ratelimited"       => new BotRateLimitException(slackErrorCode, inner),
            "invalid_auth"      => new BotAuthenticationException(slackErrorCode, inner),
            "token_revoked"     => new BotAuthenticationException(slackErrorCode, inner),
            "missing_scope"     => new BotPermissionException(slackErrorCode, inner),
            "account_inactive"  => new BotAuthenticationException(slackErrorCode, inner),
            _                   => inner,
        };
    }
}
