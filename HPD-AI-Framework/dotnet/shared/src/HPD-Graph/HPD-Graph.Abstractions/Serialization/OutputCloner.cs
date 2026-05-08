using System.Text.Json;

namespace HPDAgent.Graph.Abstractions.Serialization;

/// <summary>
/// Deep clones node outputs using Native AOT-safe graph JSON serialization.
/// </summary>
public static class OutputCloner
{
    // Note: We don't use JsonSerializerOptions to avoid AOT warnings
    // Instead we use the JsonTypeInfo directly from the context

    /// <summary>
    /// Deep clones node outputs. Handles primitives, collections, dictionaries, and JsonElement values.
    /// Performance: ~140 μs for 100KB payloads.
    /// </summary>
    /// <param name="outputs">Original outputs dictionary</param>
    /// <returns>Deep cloned dictionary with independent references</returns>
    public static Dictionary<string, object> DeepClone(Dictionary<string, object> outputs)
    {
        if (outputs == null || outputs.Count == 0)
            return new Dictionary<string, object>();

        var result = new Dictionary<string, object>(outputs.Count);
        foreach (var (key, value) in outputs)
        {
            result[key] = ConvertJsonElement(GraphJsonValue.ToJsonElement(value, $"output '{key}'"));
        }

        return result;
    }

    /// <summary>
    /// Deep clones with the same Native AOT-safe value restrictions as <see cref="DeepClone"/>.
    /// </summary>
    public static Dictionary<string, object> DeepCloneWithCircularRefs(
        Dictionary<string, object> outputs)
    {
        if (outputs == null || outputs.Count == 0)
            return new Dictionary<string, object>();

        return DeepClone(outputs);
    }

    /// <summary>
    /// Validates that outputs are serializable.
    /// Throws InvalidOperationException for non-serializable types.
    /// </summary>
    public static void ValidateSerializable(Dictionary<string, object> outputs)
    {
        if (outputs == null) return;

        foreach (var (key, value) in outputs)
        {
            if (value == null) continue;

            var type = value.GetType();

            // Check for known non-serializable types
            if (type == typeof(Stream) ||
                type.IsSubclassOf(typeof(Stream)) ||
                typeof(System.Data.Common.DbConnection).IsAssignableFrom(type) ||
                typeof(Delegate).IsAssignableFrom(type) ||
                type == typeof(CancellationToken) ||
                type == typeof(Task) ||
                type.IsSubclassOf(typeof(Task)))
            {
                throw new InvalidOperationException(
                    $"Output key '{key}' contains non-serializable type {type.Name}. " +
                    "Streams, database connections, delegates, cancellation tokens, and tasks cannot be cloned. " +
                    "Return value types or DTOs instead.");
            }

            // Recursively validate collections
            if (value is IEnumerable<object> enumerable && value is not string)
            {
                foreach (var item in enumerable)
                {
                    if (item != null)
                    {
                        ValidateValue(item, key);
                    }
                }
            }
        }
    }

    private static void ValidateValue(object value, string parentKey)
    {
        var type = value.GetType();

        if (type == typeof(Stream) ||
            type.IsSubclassOf(typeof(Stream)) ||
            typeof(System.Data.Common.DbConnection).IsAssignableFrom(type) ||
            typeof(Delegate).IsAssignableFrom(type) ||
            type == typeof(CancellationToken) ||
            type == typeof(Task) ||
            type.IsSubclassOf(typeof(Task)))
        {
            throw new InvalidOperationException(
                $"Output '{parentKey}' contains nested non-serializable type {type.Name}.");
        }
    }

    /// <summary>
    /// Converts JsonElement back to concrete .NET types.
    /// Preserves type information where possible:
    /// - Numbers: int if fits, else long, else decimal, else double
    /// - Arrays: List&lt;object&gt;
    /// - Objects: Dictionary&lt;string, object&gt;
    /// - Objects: Dictionary&lt;string, object&gt;
    /// </summary>
    private static object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()!,
            // Enhanced number handling with TryGetDecimal() fix
            JsonValueKind.Number => element.TryGetInt32(out var i) ? (object)i :
                                   element.TryGetInt64(out var l) ? (object)l :
                                   element.TryGetDecimal(out var m) ? (object)m :
                                   element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            JsonValueKind.Array => element.EnumerateArray()
                .Select(ConvertJsonElement)
                .ToList(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
            _ => element  // Keep as JsonElement for unknown types
        };
    }
}
