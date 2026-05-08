using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace HPDAgent.Graph.Abstractions.Serialization;

/// <summary>
/// Writes runtime graph values to JSON without reflection-based serialization.
/// </summary>
public static class GraphJsonValue
{
    public static JsonElement ToJsonElement(object? value, string? valueName = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            Write(writer, value, valueName);
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    public static string ToJsonString(object? value, string? valueName = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            Write(writer, value, valueName);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    public static void Write(Utf8JsonWriter writer, object? value, string? valueName = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Write(writer, value, valueName, new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    private static void Write(
        Utf8JsonWriter writer,
        object? value,
        string? valueName,
        HashSet<object> visited)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                return;
            case JsonElement element:
                element.WriteTo(writer);
                return;
            case string stringValue:
                writer.WriteStringValue(stringValue);
                return;
            case bool boolValue:
                writer.WriteBooleanValue(boolValue);
                return;
            case byte byteValue:
                writer.WriteNumberValue(byteValue);
                return;
            case sbyte sbyteValue:
                writer.WriteNumberValue(sbyteValue);
                return;
            case short shortValue:
                writer.WriteNumberValue(shortValue);
                return;
            case ushort ushortValue:
                writer.WriteNumberValue(ushortValue);
                return;
            case int intValue:
                writer.WriteNumberValue(intValue);
                return;
            case uint uintValue:
                writer.WriteNumberValue(uintValue);
                return;
            case long longValue:
                writer.WriteNumberValue(longValue);
                return;
            case ulong ulongValue:
                writer.WriteNumberValue(ulongValue);
                return;
            case float floatValue:
                writer.WriteNumberValue(floatValue);
                return;
            case double doubleValue:
                writer.WriteNumberValue(doubleValue);
                return;
            case decimal decimalValue:
                writer.WriteNumberValue(decimalValue);
                return;
            case Guid guidValue:
                writer.WriteStringValue(guidValue);
                return;
            case DateTime dateTimeValue:
                writer.WriteStringValue(dateTimeValue);
                return;
            case DateTimeOffset dateTimeOffsetValue:
                writer.WriteStringValue(dateTimeOffsetValue);
                return;
            case TimeSpan timeSpanValue:
                writer.WriteStringValue(timeSpanValue.ToString("c", CultureInfo.InvariantCulture));
                return;
            case Enum enumValue:
                writer.WriteStringValue(enumValue.ToString());
                return;
            case IReadOnlyDictionary<string, object?> readOnlyDictionary:
                WriteDictionary(writer, readOnlyDictionary, valueName, visited);
                return;
            case IDictionary<string, object?> dictionary:
                WriteDictionary(writer, dictionary, valueName, visited);
                return;
            case IDictionary nonGenericDictionary:
                WriteDictionary(writer, nonGenericDictionary, valueName, visited);
                return;
            case IEnumerable enumerable when value is not string:
                WriteArray(writer, enumerable, valueName, visited);
                return;
            default:
                throw UnsupportedValue(value, valueName);
        }
    }

    public static object? ToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt32(out var intValue) ? intValue :
                element.TryGetInt64(out var longValue) ? longValue :
                element.TryGetDecimal(out var decimalValue) ? decimalValue :
                element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray()
                .Select(item => ToObject(item)!)
                .ToList(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(property => property.Name, property => ToObject(property.Value)!),
            _ => element.Clone()
        };
    }

    private static void WriteDictionary<TValue>(
        Utf8JsonWriter writer,
        IEnumerable<KeyValuePair<string, TValue>> dictionary,
        string? valueName,
        HashSet<object> visited)
    {
        var reference = (object)dictionary;
        if (!visited.Add(reference))
        {
            throw CircularValue(valueName);
        }

        writer.WriteStartObject();
        try
        {
            foreach (var (key, nestedValue) in dictionary)
            {
                writer.WritePropertyName(key);
                Write(writer, nestedValue, valueName is null ? key : $"{valueName}.{key}", visited);
            }
        }
        finally
        {
            visited.Remove(reference);
        }

        writer.WriteEndObject();
    }

    private static void WriteDictionary(
        Utf8JsonWriter writer,
        IDictionary dictionary,
        string? valueName,
        HashSet<object> visited)
    {
        if (!visited.Add(dictionary))
        {
            throw CircularValue(valueName);
        }

        writer.WriteStartObject();
        try
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is not string key)
                {
                    throw new InvalidOperationException(
                        $"Graph JSON value '{valueName ?? "value"}' contains a dictionary key of type '{entry.Key?.GetType().FullName ?? "null"}'. " +
                        "Only string dictionary keys are supported in Native AOT-safe graph JSON values.");
                }

                writer.WritePropertyName(key);
                Write(writer, entry.Value, valueName is null ? key : $"{valueName}.{key}", visited);
            }
        }
        finally
        {
            visited.Remove(dictionary);
        }

        writer.WriteEndObject();
    }

    private static void WriteArray(
        Utf8JsonWriter writer,
        IEnumerable enumerable,
        string? valueName,
        HashSet<object> visited)
    {
        if (!visited.Add(enumerable))
        {
            throw CircularValue(valueName);
        }

        writer.WriteStartArray();
        try
        {
            var index = 0;
            foreach (var item in enumerable)
            {
                Write(writer, item, $"{valueName ?? "value"}[{index}]", visited);
                index++;
            }
        }
        finally
        {
            visited.Remove(enumerable);
        }

        writer.WriteEndArray();
    }

    private static InvalidOperationException UnsupportedValue(object value, string? valueName)
        => new(
            $"Graph JSON value '{valueName ?? "value"}' has unsupported type '{value.GetType().FullName}'. " +
            "Native AOT-safe graph JSON values must be primitives, enums, JsonElement, arrays, or dictionaries with string keys. " +
            "Convert custom objects to a supported graph value or provide source-generated JsonTypeInfo at the API boundary.");

    private static InvalidOperationException CircularValue(string? valueName)
        => new(
            $"Graph JSON value '{valueName ?? "value"}' contains a circular reference. " +
            "Native AOT-safe graph JSON values must be acyclic.");
}
