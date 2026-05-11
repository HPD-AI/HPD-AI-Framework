namespace HPD.Agent.Bots.WhatsApp;

/// <summary>
/// Configuration for the WhatsApp Cloud API bot.
/// </summary>
/// <remarks>
/// WhatsApp Cloud API only allows free-form outbound messages inside the
/// 24-hour customer-service window, unless the message is an approved template.
/// This bot does not enforce that policy locally; Meta rejects out-of-window
/// sends at the Graph API boundary.
/// </remarks>
public sealed class WhatsAppBotConfig
{
    /// <summary>
    /// Meta access token used for WhatsApp Cloud API calls. Falls back to
    /// <c>WHATSAPP_ACCESS_TOKEN</c>.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// Meta app secret used to verify <c>X-Hub-Signature-256</c> webhook signatures.
    /// Falls back to <c>WHATSAPP_APP_SECRET</c>.
    /// </summary>
    public string? AppSecret { get; set; }

    /// <summary>
    /// WhatsApp Business phone number ID, not the display phone number. Falls back to
    /// <c>WHATSAPP_PHONE_NUMBER_ID</c>.
    /// </summary>
    public string? PhoneNumberId { get; set; }

    /// <summary>
    /// Verification token expected during Meta's webhook challenge handshake. Falls
    /// back to <c>WHATSAPP_VERIFY_TOKEN</c>.
    /// </summary>
    public string? VerifyToken { get; set; }

    /// <summary>
    /// Meta Graph API version. Pin this to the version validated in production if
    /// Meta releases a newer Cloud API version.
    /// </summary>
    public string ApiVersion { get; set; } = "v25.0";

    /// <summary>
    /// Meta Graph API base URL. Falls back to <c>WHATSAPP_API_URL</c> and defaults to
    /// <c>https://graph.facebook.com</c>. Override this only for Meta-compatible
    /// test gateways or self-hosted proxies.
    /// </summary>
    public string? ApiUrl { get; set; }

    /// <summary>
    /// Bot display name used in metadata and diagnostics. Falls back to
    /// <c>WHATSAPP_BOT_USERNAME</c>.
    /// </summary>
    public string UserName { get; set; } = "whatsapp-bot";

    /// <summary>
    /// HPD agent ID for inbound WhatsApp messages.
    /// </summary>
    public string? AgentId { get; set; }

    internal string ResolveAccessToken()
        => FirstNonWhiteSpace(AccessToken, Environment.GetEnvironmentVariable("WHATSAPP_ACCESS_TOKEN"))
            ?? throw new InvalidOperationException(
                "WhatsAppBotConfig.AccessToken is required. Set WHATSAPP_ACCESS_TOKEN or configure it explicitly.");

    internal string ResolveAppSecret()
        => FirstNonWhiteSpace(AppSecret, Environment.GetEnvironmentVariable("WHATSAPP_APP_SECRET"))
            ?? throw new InvalidOperationException(
                "WhatsAppBotConfig.AppSecret is required. Set WHATSAPP_APP_SECRET or configure it explicitly.");

    internal string ResolvePhoneNumberId()
        => FirstNonWhiteSpace(PhoneNumberId, Environment.GetEnvironmentVariable("WHATSAPP_PHONE_NUMBER_ID"))
            ?? throw new InvalidOperationException(
                "WhatsAppBotConfig.PhoneNumberId is required. Set WHATSAPP_PHONE_NUMBER_ID or configure it explicitly.");

    internal string ResolveVerifyToken()
        => FirstNonWhiteSpace(VerifyToken, Environment.GetEnvironmentVariable("WHATSAPP_VERIFY_TOKEN"))
            ?? throw new InvalidOperationException(
                "WhatsAppBotConfig.VerifyToken is required. Set WHATSAPP_VERIFY_TOKEN or configure it explicitly.");

    internal string ResolveUserName()
        => FirstNonWhiteSpace(UserName, Environment.GetEnvironmentVariable("WHATSAPP_BOT_USERNAME"))
            ?? "whatsapp-bot";

    internal string ResolveAgentId()
        => FirstNonWhiteSpace(AgentId)
            ?? throw new InvalidOperationException("WhatsAppBotConfig.AgentId is required.");

    internal string ResolveApiUrl()
        => (FirstNonWhiteSpace(ApiUrl, Environment.GetEnvironmentVariable("WHATSAPP_API_URL"))
                ?? "https://graph.facebook.com")
            .TrimEnd('/');

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }
}
