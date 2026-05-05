namespace HPD.Agent.Bots.Slack;

/// <summary>
/// Platform key codec for Slack threads.
/// </summary>
/// <remarks>
/// DM threading rules:
/// <list type="bullet">
///   <item>DM top-level (<c>channel_type == "im"</c>, no <c>thread_ts</c>): <c>ThreadTs = ""</c></item>
///   <item>DM thread reply: <c>ThreadTs = thread_ts</c></item>
///   <item>Channel message: <c>ThreadTs = thread_ts ?? ts</c></item>
/// </list>
/// </remarks>
[ThreadId("slack:{Channel}:{ThreadTs}")]
public partial record SlackThreadId(string Channel, string ThreadTs)
{
    /// <summary>
    /// True when the channel ID starts with 'D' — a direct message channel.
    /// </summary>
    public bool IsDM => Channel.StartsWith('D');

    /// <summary>
    /// Channel-scoped key (without thread). Used for channel-level operations
    /// like listing threads or resolving multi-workspace token by channel.
    /// </summary>
    public string ChannelKey => $"slack:{Channel}";
}
