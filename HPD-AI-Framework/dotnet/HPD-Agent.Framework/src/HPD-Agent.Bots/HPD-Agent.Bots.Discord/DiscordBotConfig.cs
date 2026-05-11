using System.ComponentModel.DataAnnotations;

namespace HPD.Agent.Bots.Discord;

/// <summary>
/// Configuration for <see cref="DiscordBot"/>.
/// </summary>
public class DiscordBotConfig
{
    /// <summary>
    /// Discord application public key, stored as a 64-character hex string.
    /// </summary>
    [Required]
    public string PublicKey
    {
        get;
        set => field = value?.Trim().ToLowerInvariant()
            ?? throw new ArgumentNullException(nameof(value));
    } = string.Empty;

    /// <summary>
    /// Discord bot token. The value should not include the "Bot " prefix.
    /// </summary>
    [Required]
    public string BotToken
    {
        get;
        set => field = value?.Trim()
            ?? throw new ArgumentNullException(nameof(value));
    } = string.Empty;

    /// <summary>
    /// Discord application ID. Used for interaction webhook follow-ups.
    /// </summary>
    [Required]
    public string ApplicationId
    {
        get;
        set => field = value?.Trim()
            ?? throw new ArgumentNullException(nameof(value));
    } = string.Empty;

    /// <summary>
    /// Role IDs whose mentions trigger the agent in Gateway mode.
    /// </summary>
    public IReadOnlyList<string> MentionRoleIds { get; set; } = [];

    /// <summary>
    /// HPD agent ID to route inbound messages to.
    /// </summary>
    public string? AgentId { get; set; }

    internal string ResolveAgentId()
        => FirstNonWhiteSpace(AgentId)
            ?? throw new InvalidOperationException("DiscordBotConfig.AgentId is required.");

    /// <summary>
    /// Overrides the generated streaming debounce interval when set.
    /// When <c>null</c>, the value from <see cref="HpdStreamingAttribute"/> is used.
    /// </summary>
    public int? StreamingDebounceMs { get; set; }

    /// <summary>
    /// Bot token used by the optional Gateway listener. When null, Gateway mode is disabled.
    /// </summary>
    public string? GatewayToken { get; set; }

    /// <summary>
    /// Absolute URL of this app's Discord webhook endpoint. Gateway mode forwards
    /// MESSAGE_CREATE and reaction events to this URL so they reuse HTTP dispatch.
    /// </summary>
    public string? GatewayForwardUrl { get; set; }

    /// <summary>
    /// How long a Gateway session runs before reconnecting.
    /// </summary>
    public TimeSpan GatewaySessionDuration { get; set; } = TimeSpan.FromMinutes(10);

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
