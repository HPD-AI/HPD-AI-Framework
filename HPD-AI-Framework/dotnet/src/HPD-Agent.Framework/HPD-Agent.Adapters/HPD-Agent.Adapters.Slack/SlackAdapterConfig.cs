using System.ComponentModel.DataAnnotations;

namespace HPD.Agent.Adapters.Slack;

/// <summary>
/// Configuration for <see cref="SlackAdapter"/>.
/// Bind via <c>builder.Services.Configure&lt;SlackAdapterConfig&gt;(config.GetSection("Slack"))</c>.
/// </summary>
public class SlackAdapterConfig
{
    /// <summary>
    /// Slack signing secret used to verify inbound webhook signatures.
    /// Found in your Slack App dashboard → Basic Information → App Credentials.
    /// </summary>
    [Required]
    public string SigningSecret
    {
        get;
        set => field = value?.Trim()
            ?? throw new ArgumentNullException(nameof(value));
    } = string.Empty;

    /// <summary>
    /// Bot User OAuth Token (xoxb-...).
    /// For multi-workspace deployments, leave empty and resolve per-request
    /// via <c>ISecretResolver</c> under the key <c>"slack:BotToken:{teamId}"</c>.
    /// </summary>
    [Required]
    public string BotToken
    {
        get;
        set => field = value?.Trim()
            ?? throw new ArgumentNullException(nameof(value));
    } = string.Empty;

    /// <summary>
    /// Bot user ID (U_xxx). If omitted, fetched via <c>auth.test</c> on first request
    /// and cached for the process lifetime. Used to suppress echo loops on self-messages.
    /// </summary>
    public string? BotUserId { get; set; }

    /// <summary>
    /// Which HPD agent to route inbound messages to.
    /// Defaults to the agent registered as <c>DefaultName</c> in <c>AddHPDAgent()</c>.
    /// </summary>
    public string? AgentName { get; set; }

    /// <summary>
    /// Minimum milliseconds between consecutive <c>chat.update</c> calls during streaming.
    /// Prevents hitting Slack's ~1 update/second rate limit.
    /// Default: 500ms.
    /// </summary>
    public int StreamingDebounceMs { get; set; } = 500;

    /// <summary>
    /// Maximum time to wait for a user to click Approve/Deny on a permission request.
    /// After this, the agent loop receives a denial and continues.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan PermissionTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Use Slack's native <c>chat.startStream</c> / <c>chat.appendStream</c> / <c>chat.stopStream</c>
    /// API when the request comes from an Assistants thread (recipientUserId is known).
    /// Falls back to PostAndEdit for channel messages regardless of this setting.
    /// Requires the Assistants feature flag and <c>assistants:write</c> OAuth scope.
    /// Default: false — PostAndEdit works everywhere with no feature flag.
    /// </summary>
    public bool UseNativeStreaming { get; set; } = false;

    /// <summary>
    /// App-level token (xapp-...) with <c>connections:write</c> scope.
    /// When set, <c>SlackSocketModeService</c> is registered as a BackgroundService and
    /// events are delivered over a persistent outbound WebSocket — no public URL required.
    /// When null (default), HTTP Events API mode is used (<c>MapSlackWebhook()</c> required).
    /// </summary>
    public string? AppToken { get; set; }
}
