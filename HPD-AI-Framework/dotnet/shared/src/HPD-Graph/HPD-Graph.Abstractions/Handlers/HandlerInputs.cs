using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace HPDAgent.Graph.Abstractions.Handlers;

/// <summary>
/// Typed inputs provided to a handler from upstream nodes.
/// The orchestrator creates this from previous node outputs.
/// Handlers consume this without knowing about the orchestrator's internal storage.
/// </summary>
public sealed class HandlerInputs
{
    private readonly Dictionary<string, object> _inputs = new();

    /// <summary>
    /// Get a required input value.
    /// </summary>
    /// <typeparam name="T">Expected type of the value</typeparam>
    /// <param name="inputName">Input name</param>
    /// <returns>Typed value</returns>
    /// <exception cref="InvalidOperationException">Thrown if input not found or type mismatch</exception>
    public T Get<T>(string inputName)
    {
        if (string.IsNullOrWhiteSpace(inputName))
        {
            throw new ArgumentException("Input name cannot be null or whitespace.", nameof(inputName));
        }

        if (!_inputs.TryGetValue(inputName, out var value))
        {
            throw new InvalidOperationException($"Required input '{inputName}' not found.");
        }

        if (value == null)
        {
            if (default(T) == null)
            {
                return default!;
            }

            throw new InvalidOperationException(
                $"Input '{inputName}' is null, but type {typeof(T).Name} is not nullable.");
        }

        if (value is T typedValue)
        {
            return typedValue;
        }

        throw new InvalidOperationException(
            $"Input '{inputName}' has type {value.GetType().Name}, but {typeof(T).Name} was requested.");
    }

    /// <summary>
    /// Get an optional input value with a default.
    /// </summary>
    /// <typeparam name="T">Expected type of the value</typeparam>
    /// <param name="inputName">Input name</param>
    /// <param name="defaultValue">Default value if not found</param>
    /// <returns>Typed value or default</returns>
    public T GetOrDefault<T>(string inputName, T defaultValue)
    {
        if (string.IsNullOrWhiteSpace(inputName))
        {
            return defaultValue;
        }

        if (!_inputs.TryGetValue(inputName, out var value))
        {
            return defaultValue;
        }

        if (value == null)
        {
            return defaultValue;
        }

        if (value is T typedValue)
        {
            return typedValue;
        }

        return defaultValue;
    }

    /// <summary>
    /// Try to get an input value.
    /// </summary>
    /// <typeparam name="T">Expected type of the value</typeparam>
    /// <param name="inputName">Input name</param>
    /// <param name="value">Output value if found</param>
    /// <returns>True if value was found and has correct type, false otherwise</returns>
    public bool TryGet<T>(string inputName, out T? value)
    {
        value = default;

        if (string.IsNullOrWhiteSpace(inputName))
        {
            return false;
        }

        if (!_inputs.TryGetValue(inputName, out var rawValue))
        {
            return false;
        }

        if (rawValue == null)
        {
            value = default;
            return true;
        }

        if (rawValue is T typedValue)
        {
            value = typedValue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Add an input value (used by orchestrator).
    /// </summary>
    public void Add(string inputName, object value)
    {
        if (string.IsNullOrWhiteSpace(inputName))
        {
            throw new ArgumentException("Input name cannot be null or whitespace.", nameof(inputName));
        }

        _inputs[inputName] = value;
    }

    /// <summary>
    /// Get all inputs as a dictionary (for debugging/logging).
    /// </summary>
    public IReadOnlyDictionary<string, object> GetAll() => _inputs;

    /// <summary>
    /// Check if an input exists.
    /// </summary>
    public bool Contains(string inputName) =>
        !string.IsNullOrWhiteSpace(inputName) && _inputs.ContainsKey(inputName);

    #region Pattern Matching Methods (Phase 3)

    // Cache for compiled regex patterns (thread-safe, static for reuse across instances)
    private static readonly ConcurrentDictionary<string, Regex> _regexCache = new();

    /// <summary>
    /// Get all values matching a wildcard pattern.
    /// Supports: "*.answer", "solver*", "solver*.answer", "*"
    /// </summary>
    /// <example>
    /// var answers = inputs.GetAllMatching&lt;string&gt;("*.answer");
    /// // Returns all values where key ends with ".answer"
    /// </example>
    public List<T> GetAllMatching<T>(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var regex = GetOrCreateRegex(pattern);
        return _inputs
            .Where(kvp => regex.IsMatch(kvp.Key))
            .Select(kvp => kvp.Value)
            .OfType<T>()
            .ToList();
    }

    /// <summary>
    /// Get all matching values with their keys preserved.
    /// Useful for source attribution in fan-in scenarios.
    /// </summary>
    /// <example>
    /// var answersWithSource = inputs.GetAllMatchingWithKeys&lt;string&gt;("solver*.answer");
    /// // Returns: { "solver1.answer": "42", "solver2.answer": "42", "solver3.answer": "41" }
    /// </example>
    public Dictionary<string, T> GetAllMatchingWithKeys<T>(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var regex = GetOrCreateRegex(pattern);
        return _inputs
            .Where(kvp => regex.IsMatch(kvp.Key) && kvp.Value is T)
            .ToDictionary(kvp => kvp.Key, kvp => (T)kvp.Value);
    }

    /// <summary>
    /// Get all values from a specific source node's namespace.
    /// </summary>
    /// <example>
    /// var solverOutputs = inputs.GetNamespace&lt;object&gt;("solver1");
    /// // Returns: { "answer": "42", "confidence": 0.95 }
    /// </example>
    public Dictionary<string, T> GetNamespace<T>(string sourceNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNodeId);

        var prefix = $"{sourceNodeId}.";
        return _inputs
            .Where(kvp => kvp.Key.StartsWith(prefix) && kvp.Value is T)
            .ToDictionary(
                kvp => kvp.Key.Substring(prefix.Length),
                kvp => (T)kvp.Value
            );
    }

    /// <summary>
    /// Safely attempt to get all values from a specific source node's namespace.
    /// </summary>
    /// <returns>True if the source namespace exists and has at least one value; otherwise false.</returns>
    /// <example>
    /// if (inputs.TryGetNamespace&lt;object&gt;("solver1", out var outputs))
    /// {
    ///     var answer = outputs["answer"];
    /// }
    /// </example>
    public bool TryGetNamespace<T>(string sourceNodeId, out Dictionary<string, T> result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNodeId);

        var prefix = $"{sourceNodeId}.";
        result = _inputs
            .Where(kvp => kvp.Key.StartsWith(prefix) && kvp.Value is T)
            .ToDictionary(
                kvp => kvp.Key.Substring(prefix.Length),
                kvp => (T)kvp.Value
            );

        return result.Count > 0;
    }

    /// <summary>
    /// Get all values from multiple explicit source nodes.
    /// More explicit than pattern matching when you know the sources.
    /// </summary>
    /// <example>
    /// var answers = inputs.GetAllFromSources&lt;string&gt;("answer", "solver1", "solver2", "solver3");
    /// // Returns: ["42", "42", "41"]
    /// </example>
    public List<T> GetAllFromSources<T>(string key, params string[] sourceNodeIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var results = new List<T>();
        foreach (var sourceId in sourceNodeIds)
        {
            var namespacedKey = $"{sourceId}.{key}";
            if (_inputs.TryGetValue(namespacedKey, out var value) && value is T typedValue)
            {
                results.Add(typedValue);
            }
        }
        return results;
    }

    /// <summary>
    /// Get all source node IDs that contributed inputs.
    /// </summary>
    /// <remarks>
    /// This method assumes namespaced keys follow the pattern "sourceNodeId.keyName".
    /// Keys containing dots that are not namespaced (e.g., "config.setting") may produce
    /// incorrect results. Use namespaced access patterns consistently for reliable behavior.
    /// </remarks>
    public IReadOnlyList<string> GetSourceNodeIds()
    {
        return _inputs.Keys
            .Where(k => k.Contains('.'))
            .Select(k => k.Substring(0, k.IndexOf('.')))
            .Distinct()
            .ToList();
    }

    private static Regex GetOrCreateRegex(string pattern)
    {
        return _regexCache.GetOrAdd(pattern, p =>
        {
            var escaped = Regex.Escape(p).Replace("\\*", ".*");
            return new Regex($"^{escaped}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        });
    }

    #endregion
}
