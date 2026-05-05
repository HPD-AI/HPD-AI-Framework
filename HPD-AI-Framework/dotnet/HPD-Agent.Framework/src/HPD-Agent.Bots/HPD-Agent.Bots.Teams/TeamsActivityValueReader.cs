using System.Collections;
using System.Text.Json;

namespace HPD.Agent.Bots.Teams;

internal static class TeamsActivityValueReader
{
    public static IReadOnlyDictionary<string, string> Read(object? value)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        ReadInto(value, result, prefix: null);
        return result;
    }

    private static void ReadInto(object? value, Dictionary<string, string> result, string? prefix)
    {
        switch (value)
        {
            case null:
                return;

            case JsonElement element:
                ReadJsonElement(element, result, prefix);
                return;

            case IReadOnlyDictionary<string, object?> dictionary:
                foreach (var item in dictionary)
                    ReadInto(item.Value, result, Combine(prefix, item.Key));
                return;

            case IDictionary dictionary:
                foreach (DictionaryEntry item in dictionary)
                {
                    if (item.Key is not null)
                        ReadInto(item.Value, result, Combine(prefix, item.Key.ToString()!));
                }
                return;

            default:
                if (prefix is not null)
                    result[prefix] = value.ToString() ?? string.Empty;
                return;
        }
    }

    private static void ReadJsonElement(JsonElement element, Dictionary<string, string> result, string? prefix)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    ReadJsonElement(property.Value, result, Combine(prefix, property.Name));
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    ReadJsonElement(item, result, Combine(prefix, index++.ToString()));
                break;

            case JsonValueKind.String:
                if (prefix is not null)
                    result[prefix] = element.GetString() ?? string.Empty;
                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                if (prefix is not null)
                    result[prefix] = element.GetRawText();
                break;
        }
    }

    private static string Combine(string? prefix, string key)
        => prefix is null ? key : $"{prefix}.{key}";
}
