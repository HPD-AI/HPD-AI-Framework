using HPD.Agent.Adapters.Contracts;

namespace HPD.Agent.Adapters.Slack;

/// <summary>
/// Maps Slack Web API error codes to typed <see cref="AdapterException"/> subclasses.
/// <c>ThrowMapped</c> will eventually be emitted by the <c>[AdapterErrors]</c> source generator;
/// implemented here manually until the generator is ready.
/// </summary>
[AdapterErrors("slack")]
[ErrorCode("not_in_channel",    typeof(AdapterPermissionException))]
[ErrorCode("channel_not_found", typeof(AdapterNotFoundException))]
[ErrorCode("is_archived",       typeof(AdapterPermissionException))]
[ErrorCode("ratelimited",       typeof(AdapterRateLimitException))]
[ErrorCode("invalid_auth",      typeof(AdapterAuthenticationException))]
[ErrorCode("token_revoked",     typeof(AdapterAuthenticationException))]
[ErrorCode("missing_scope",     typeof(AdapterPermissionException))]
[ErrorCode("account_inactive",  typeof(AdapterAuthenticationException))]
public partial class SlackErrorHandler
{
    // ── Error mapping (to be replaced by [AdapterErrors] source generator output) ──

    /// <summary>
    /// Throws the <see cref="AdapterException"/> subtype that corresponds to
    /// <paramref name="slackErrorCode"/>, wrapping <paramref name="inner"/> as the cause.
    /// Unmapped codes re-throw <paramref name="inner"/> directly.
    /// </summary>
    public static void ThrowMapped(string slackErrorCode, Exception inner)
    {
        throw slackErrorCode switch
        {
            "not_in_channel"    => new AdapterPermissionException(slackErrorCode, inner),
            "channel_not_found" => new AdapterNotFoundException(slackErrorCode, inner),
            "is_archived"       => new AdapterPermissionException(slackErrorCode, inner),
            "ratelimited"       => new AdapterRateLimitException(slackErrorCode, inner),
            "invalid_auth"      => new AdapterAuthenticationException(slackErrorCode, inner),
            "token_revoked"     => new AdapterAuthenticationException(slackErrorCode, inner),
            "missing_scope"     => new AdapterPermissionException(slackErrorCode, inner),
            "account_inactive"  => new AdapterAuthenticationException(slackErrorCode, inner),
            _                   => inner,
        };
    }
}
