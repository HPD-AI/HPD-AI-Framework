namespace HPDAgent.Graph.Abstractions.State;

/// <summary>
/// State scope for hierarchical namespace-based state management.
/// Provides isolation, bulk operations, and clear ownership semantics.
/// IMPLEMENTATION: Built ON TOP of Channels (not a separate state store).
/// Internally: scope.Set("field", value) → channel["scope:{scopeName}:field"]
/// </summary>
public interface IGraphStateScope
{
    /// <summary>
    /// Scope name (null for root scope).
    /// </summary>
    string? ScopeName { get; }

    /// <summary>
    /// Get a value from this scope.
    /// </summary>
    /// <typeparam name="T">Expected type of the value</typeparam>
    /// <param name="key">Key within this scope</param>
    /// <returns>Value if found, default(T) otherwise</returns>
    T? Get<T>(string key);

    /// <summary>
    /// Try to get a value from this scope.
    /// </summary>
    /// <typeparam name="T">Expected type of the value</typeparam>
    /// <param name="key">Key within this scope</param>
    /// <param name="value">Output value if found</param>
    /// <returns>True if value was found, false otherwise</returns>
    bool TryGet<T>(string key, out T? value);

    /// <summary>
    /// Set a value in this scope.
    /// </summary>
    /// <typeparam name="T">Type of the value</typeparam>
    /// <param name="key">Key within this scope</param>
    /// <param name="value">Value to set</param>
    void Set<T>(string key, T value);

    /// <summary>
    /// Remove a value from this scope.
    /// </summary>
    /// <param name="key">Key within this scope</param>
    /// <returns>True if value was removed, false if it didn't exist</returns>
    bool Remove(string key);

    /// <summary>
    /// Clear all values in this scope (bulk delete).
    /// </summary>
    void Clear();

    /// <summary>
    /// Get all keys in this scope.
    /// </summary>
    IReadOnlyList<string> Keys { get; }

    /// <summary>
    /// Get all key-value pairs in this scope as a dictionary.
    /// </summary>
    /// <returns>Dictionary snapshot of this scope</returns>
    IReadOnlyDictionary<string, object> AsDictionary();
}
