namespace HPD.Agent.Adapters;

/// <summary>
/// Controls how streamed agent output tokens are delivered to the platform.
/// </summary>
public enum StreamingStrategy
{
    /// <summary>
    /// Post a placeholder message immediately, then edit it in place as tokens arrive.
    /// Debounced to avoid rate limits. Default for most platforms (Slack, Teams, Discord).
    /// </summary>
    PostAndEdit,

    /// <summary>
    /// Buffer the complete response, then post a single message.
    /// Used for platforms that don't support message editing or where editing is unreliable.
    /// </summary>
    BufferAndPost,

    /// <summary>
    /// Use the platform's native streaming API (e.g. Slack's <c>chatStream</c>).
    /// Requires platform-specific setup and scopes; falls back to PostAndEdit if unavailable.
    /// </summary>
    Native,
}
