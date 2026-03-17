namespace HPD.Agent.Adapters.Slack;

/// <summary>
/// Platform key codec for Slack threads.
/// <c>Format</c> and <c>Parse</c> will eventually be emitted by the <c>[ThreadId]</c>
/// source generator; implemented here manually until the generator is ready.
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

    // ── Codec (to be replaced by [ThreadId] source generator output) ─────────

    /// <summary>
    /// Formats a channel ID and thread timestamp into a Slack platform key.
    /// Example: <c>Format("C1234567", "1234567890.000100")</c> → <c>"slack:C1234567:1234567890.000100"</c>
    /// </summary>
    public static string Format(string channel, string threadTs)
        => $"slack:{channel}:{threadTs}";

    /// <summary>
    /// Parses a Slack platform key back into a <see cref="SlackThreadId"/>.
    /// Expects the format produced by <see cref="Format"/>.
    /// </summary>
    public static SlackThreadId Parse(string key)
    {
        // Format: "slack:{channel}:{threadTs}"
        // threadTs may be empty (DM top-level) or contain dots — split on first two colons only.
        const string prefix = "slack:";
        if (!key.StartsWith(prefix))
            throw new FormatException($"SlackThreadId key must start with '{prefix}': {key}");

        var rest       = key[prefix.Length..];        // "{channel}:{threadTs}"
        var colonIndex = rest.IndexOf(':');
        if (colonIndex < 0)
            throw new FormatException($"SlackThreadId key is missing the thread-ts segment: {key}");

        return new SlackThreadId(
            Channel:  rest[..colonIndex],
            ThreadTs: rest[(colonIndex + 1)..]);
    }
}
