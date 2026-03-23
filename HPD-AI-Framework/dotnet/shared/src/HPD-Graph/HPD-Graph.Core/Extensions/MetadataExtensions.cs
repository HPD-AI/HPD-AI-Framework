using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.Graph;

namespace HPDAgent.Graph.Core.Extensions;

/// <summary>
/// LINQ-style extensions for querying nodes by metadata.
/// </summary>
public static class MetadataExtensions
{
    /// <summary>
    /// Filter nodes that have a specific metadata key.
    /// </summary>
    public static IReadOnlyList<Node> WithMetadata(
        this IEnumerable<Node> nodes,
        string key)
    {
        return nodes.Where(n => n.Metadata.ContainsKey(key)).ToList();
    }

    /// <summary>
    /// Filter nodes where metadata key equals specific value.
    /// </summary>
    public static IReadOnlyList<Node> WithMetadata(
        this IEnumerable<Node> nodes,
        string key,
        string value)
    {
        return nodes.Where(n =>
            n.Metadata.TryGetValue(key, out var v) && v == value).ToList();
    }

    /// <summary>
    /// Filter nodes where metadata key matches predicate.
    /// </summary>
    public static IReadOnlyList<Node> WithMetadataMatching(
        this IEnumerable<Node> nodes,
        string key,
        Func<string, bool> predicate)
    {
        return nodes.Where(n =>
            n.Metadata.TryGetValue(key, out var v) && predicate(v)).ToList();
    }

    /// <summary>
    /// Get metadata value for a node (null if not present).
    /// </summary>
    public static string? GetMetadata(this Node node, string key)
    {
        return node.Metadata.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Get metadata value with type conversion.
    /// </summary>
    public static T? GetMetadata<T>(
        this Node node,
        string key,
        Func<string, T> parser)
    {
        if (node.Metadata.TryGetValue(key, out var value))
            return parser(value);
        return default;
    }

    /// <summary>
    /// Get all distinct values for a metadata key across nodes.
    /// </summary>
    public static IReadOnlyList<string> GetMetadataValues(
        this IEnumerable<Node> nodes,
        string key)
    {
        return nodes
            .Select(n => n.GetMetadata(key))
            .Where(v => v != null)
            .Cast<string>()
            .Distinct()
            .ToList();
    }
}

/// <summary>
/// LINQ-style extensions for context-level tags.
/// </summary>
public static class TagExtensions
{
    /// <summary>
    /// Add multiple tags at once.
    /// </summary>
    public static void AddTags(
        this IGraphContext context,
        params (string key, string value)[] tags)
    {
        foreach (var (key, value) in tags)
            context.AddTag(key, value);
    }

    /// <summary>
    /// Check if context has a specific tag value.
    /// </summary>
    public static bool HasTag(
        this IGraphContext context,
        string key,
        string value)
    {
        return context.Tags.TryGetValue(key, out var values) &&
               values.Contains(value);
    }

    /// <summary>
    /// Get all values for a tag key.
    /// </summary>
    public static IReadOnlyList<string> GetTagValues(
        this IGraphContext context,
        string key)
    {
        return context.Tags.TryGetValue(key, out var values)
            ? values
            : Array.Empty<string>();
    }
}
