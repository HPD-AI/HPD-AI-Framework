using System.ComponentModel.DataAnnotations;

namespace HPD.Agent.Adapters.Slack.OAuth;

/// <summary>
/// Configuration for the Slack OAuth 2.0 installation flow.
/// Bind via <c>builder.Services.Configure&lt;SlackOAuthConfig&gt;(config.GetSection("Slack:OAuth"))</c>,
/// or pass inline to <c>AddSlackOAuth()</c>.
/// </summary>
public record SlackOAuthConfig
{
    /// <summary>
    /// Slack App Client ID. Found in your Slack App dashboard → Basic Information → App Credentials.
    /// </summary>
    [Required]
    public string ClientId
    {
        get;
        set => field = value?.Trim() ?? throw new ArgumentNullException(nameof(value));
    } = string.Empty;

    /// <summary>
    /// Slack App Client Secret. Found in your Slack App dashboard → Basic Information → App Credentials.
    /// </summary>
    [Required]
    public string ClientSecret
    {
        get;
        set => field = value?.Trim() ?? throw new ArgumentNullException(nameof(value));
    } = string.Empty;

    /// <summary>
    /// The OAuth redirect URI registered in your Slack App dashboard → OAuth &amp; Permissions.
    /// Must exactly match what you entered there (e.g. "https://yourapp.com/slack/oauth/callback").
    /// </summary>
    [Required]
    public string RedirectUri
    {
        get;
        set => field = value?.Trim() ?? throw new ArgumentNullException(nameof(value));
    } = string.Empty;

    /// <summary>
    /// Bot token OAuth scopes to request. Defaults to the minimum set needed by the adapter.
    /// </summary>
    public IReadOnlyList<string> Scopes { get; init; } =
    [
        "chat:write",
        "channels:history",
        "app_mentions:read",
        "im:history",
        "im:write",
    ];

    /// <summary>
    /// Where to redirect the browser after a successful installation.
    /// Defaults to "/" if not set.
    /// </summary>
    public string? PostInstallRedirectUri { get; init; }
}
