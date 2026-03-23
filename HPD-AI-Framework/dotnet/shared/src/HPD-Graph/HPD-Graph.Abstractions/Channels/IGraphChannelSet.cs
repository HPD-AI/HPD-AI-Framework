namespace HPDAgent.Graph.Abstractions.Channels;

/// <summary>
/// Channel set for managing graph state with explicit update semantics.
/// Thread-safe: All implementations must be thread-safe.
/// </summary>
public interface IGraphChannelSet
{
    /// <summary>
    /// Get or create a channel by name.
    /// If channel doesn't exist, creates a new LastValueChannel.
    /// </summary>
    /// <param name="name">Channel name</param>
    /// <returns>Channel instance</returns>
    IGraphChannel this[string name] { get; }

    /// <summary>
    /// Get all channel names.
    /// </summary>
    IReadOnlyList<string> ChannelNames { get; }

    /// <summary>
    /// Check if a channel exists.
    /// </summary>
    /// <param name="name">Channel name</param>
    /// <returns>True if channel exists, false otherwise</returns>
    bool Contains(string name);

    /// <summary>
    /// Remove a channel by name.
    /// </summary>
    /// <param name="name">Channel name</param>
    /// <returns>True if channel was removed, false if it didn't exist</returns>
    bool Remove(string name);

    /// <summary>
    /// Clear all channels.
    /// </summary>
    void Clear();
}
