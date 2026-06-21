namespace HPD.Agent.Bots.AspNetCore.Verification;

/// <summary>
/// HMAC signing formats supported by <see cref="WebhookSignatureVerifier"/>.
/// </summary>
public enum HmacFormat
{
    /// <summary>
    /// Slack-style V0 format: <c>HMAC-SHA256("v0:{timestamp}:{body}")</c>.
    /// Expected signature header value: <c>v0={hex}</c>.
    /// </summary>
    V0TimestampBody,
}
