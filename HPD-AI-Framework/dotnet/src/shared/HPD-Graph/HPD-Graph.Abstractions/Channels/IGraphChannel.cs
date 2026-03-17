namespace HPDAgent.Graph.Abstractions.Channels;

/// <summary>
/// A single channel in the graph state.
/// Channels have explicit update semantics and can be versioned.
/// Thread-safe: All implementations must be thread-safe.
/// </summary>
public interface IGraphChannel
{
    /// <summary>
    /// Channel name (unique identifier).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Version number - increments on each update.
    /// Useful for conflict detection and optimistic concurrency.
    /// </summary>
    int Version { get; }

    /// <summary>
    /// Update semantics for this channel.
    /// </summary>
    ChannelUpdateSemantics UpdateSemantics { get; }

    /// <summary>
    /// Get the current value from the channel.
    /// </summary>
    /// <typeparam name="T">Expected type of the value</typeparam>
    /// <returns>Current value, or default if not set</returns>
    /// <exception cref="InvalidOperationException">Thrown if type T doesn't match the channel's type</exception>
    T Get<T>();

    /// <summary>
    /// Set a value in the channel.
    /// Behavior depends on UpdateSemantics:
    /// - LastValue: Overwrites existing value
    /// - Append: Adds to list
    /// - Reducer: Applies reducer function
    /// </summary>
    /// <typeparam name="T">Type of the value</typeparam>
    /// <param name="value">Value to set</param>
    void Set<T>(T value);

    /// <summary>
    /// Update channel with multiple values (for reducers/aggregators).
    /// </summary>
    /// <typeparam name="T">Type of the values</typeparam>
    /// <param name="values">Values to update with</param>
    void Update<T>(IEnumerable<T> values);

    /// <summary>
    /// Clears the channel value, resetting to unset state.
    /// Used during iteration to invalidate stale outputs before re-execution.
    /// </summary>
    void Clear();
}
