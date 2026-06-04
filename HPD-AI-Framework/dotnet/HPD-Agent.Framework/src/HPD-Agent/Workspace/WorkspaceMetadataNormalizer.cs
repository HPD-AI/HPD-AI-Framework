using System.Text.Json;

namespace HPD.Agent;

internal static class WorkspaceMetadataNormalizer
{
    public static void Normalize(Dictionary<string, object>? metadata)
    {
        if (metadata is null)
            return;

        foreach (var key in metadata.Keys.ToArray())
            metadata[key] = NormalizeValue(metadata[key])!;
    }

    private static object? NormalizeValue(object? value)
    {
        return value switch
        {
            JsonElement element => NormalizeJsonElement(element),
            Dictionary<string, object> dictionary => NormalizeDictionary(dictionary),
            IEnumerable<object?> values when value is not string => values.Select(NormalizeValue).ToList(),
            _ => value
        };
    }

    private static Dictionary<string, object> NormalizeDictionary(Dictionary<string, object> dictionary)
    {
        foreach (var key in dictionary.Keys.ToArray())
            dictionary[key] = NormalizeValue(dictionary[key])!;

        return dictionary;
    }

    private static object? NormalizeJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => NormalizeJsonArray(element),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                property => property.Name,
                property => NormalizeJsonElement(property.Value)!,
                StringComparer.Ordinal),
            JsonValueKind.Null => null,
            _ => element.Clone()
        };
    }

    private static object NormalizeJsonArray(JsonElement element)
    {
        var values = element.EnumerateArray().Select(NormalizeJsonElement).ToList();
        return values.All(value => value is string)
            ? values.Cast<string>().ToList()
            : values;
    }
}
