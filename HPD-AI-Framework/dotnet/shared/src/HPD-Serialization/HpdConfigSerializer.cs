// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Serialization;

/// <summary>AOT-safe JSON/YAML config serialization helpers.</summary>
public static class HpdConfigSerializer
{
    /// <summary>Read a JSON or YAML config file using the extension to choose the format.</summary>
    public static T? ReadFile<T>(string path, JsonTypeInfo<T> jsonTypeInfo)
        => Deserialize(File.ReadAllText(path), jsonTypeInfo, InferFormat(path));

    /// <summary>Read a JSON or YAML config file using the extension to choose the format.</summary>
    public static async ValueTask<T?> ReadFileAsync<T>(
        string path,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default)
        => Deserialize(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), jsonTypeInfo, InferFormat(path));

    /// <summary>Write a JSON or YAML config file using the extension to choose the format.</summary>
    public static void WriteFile<T>(string path, T value, JsonTypeInfo<T> jsonTypeInfo)
        => File.WriteAllText(path, Serialize(value, jsonTypeInfo, InferFormat(path)));

    /// <summary>Write a JSON or YAML config file using the extension to choose the format.</summary>
    public static async ValueTask WriteFileAsync<T>(
        string path,
        T value,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default)
        => await File.WriteAllTextAsync(path, Serialize(value, jsonTypeInfo, InferFormat(path)), cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Deserialize JSON text with source-generated metadata.</summary>
    public static T? DeserializeJson<T>(string json, JsonTypeInfo<T> jsonTypeInfo)
        => JsonSerializer.Deserialize(json, jsonTypeInfo);

    /// <summary>Serialize a value to JSON text with source-generated metadata.</summary>
    public static string SerializeJson<T>(T value, JsonTypeInfo<T> jsonTypeInfo)
        => JsonSerializer.Serialize(value, jsonTypeInfo);

    /// <summary>Deserialize YAML text by converting it to JSON DOM first, then using source-generated metadata.</summary>
    public static T? DeserializeYaml<T>(string yaml, JsonTypeInfo<T> jsonTypeInfo)
    {
        var node = NormalizeYamlNode(ParseYamlToJsonNode(yaml), jsonTypeInfo, jsonTypeInfo.Options);
        return DeserializeJson(ToJsonText(node), jsonTypeInfo);
    }

    /// <summary>Serialize a value to YAML text with source-generated metadata.</summary>
    public static string SerializeYaml<T>(T value, JsonTypeInfo<T> jsonTypeInfo)
        => WriteYaml(JsonSerializer.SerializeToNode(value, jsonTypeInfo));

    /// <summary>Deserialize config text in the specified format.</summary>
    public static T? Deserialize<T>(string text, JsonTypeInfo<T> jsonTypeInfo, HpdConfigFormat format)
        => format switch
        {
            HpdConfigFormat.Json => DeserializeJson(text, jsonTypeInfo),
            HpdConfigFormat.Yaml => DeserializeYaml(text, jsonTypeInfo),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };

    /// <summary>Serialize a value to config text in the specified format.</summary>
    public static string Serialize<T>(T value, JsonTypeInfo<T> jsonTypeInfo, HpdConfigFormat format)
        => format switch
        {
            HpdConfigFormat.Json => SerializeJson(value, jsonTypeInfo),
            HpdConfigFormat.Yaml => SerializeYaml(value, jsonTypeInfo),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };

    /// <summary>Parse YAML into a JSON DOM.</summary>
    public static JsonNode? ParseYamlToJsonNode(string yaml)
        => HpdYamlJsonBridge.ParseYaml(yaml);

    /// <summary>Write a JSON DOM as YAML.</summary>
    public static string WriteYaml(JsonNode? node)
        => HpdYamlJsonBridge.WriteYaml(node);

    /// <summary>Infer config format from a file path.</summary>
    public static HpdConfigFormat InferFormat(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".yml", StringComparison.OrdinalIgnoreCase)
            ? HpdConfigFormat.Yaml
            : HpdConfigFormat.Json;
    }

    private static string ToJsonText(JsonNode? node)
        => (node ?? JsonValue.Create((string?)null))!.ToJsonString();

    private static JsonNode? NormalizeYamlNode(JsonNode? node, JsonTypeInfo? jsonTypeInfo, JsonSerializerOptions options)
    {
        if (node is null || jsonTypeInfo is null)
            return node;

        var targetType = Nullable.GetUnderlyingType(jsonTypeInfo.Type) ?? jsonTypeInfo.Type;
        if (targetType == typeof(string))
            return ConvertScalarToString(node);

        if (targetType == typeof(JsonElement) ||
            typeof(JsonNode).IsAssignableFrom(targetType))
            return node;

        if (node is JsonObject obj)
        {
            if (TryGetDictionaryValueType(targetType, out var valueType))
            {
                var valueTypeInfo = GetTypeInfo(options, valueType);
                foreach (var key in obj.Select(static kvp => kvp.Key).ToArray())
                {
                    var current = obj[key];
                    var normalized = NormalizeYamlNode(current, valueTypeInfo, options);
                    if (!ReferenceEquals(current, normalized))
                        obj[key] = normalized;
                }

                return obj;
            }

            if (jsonTypeInfo.Kind == JsonTypeInfoKind.Object)
            {
                foreach (var property in jsonTypeInfo.Properties)
                {
                    if (!obj.TryGetPropertyValue(property.Name, out var value))
                        continue;

                    var normalized = NormalizeYamlNode(
                        value,
                        GetTypeInfo(options, property.PropertyType),
                        options);
                    if (!ReferenceEquals(value, normalized))
                        obj[property.Name] = normalized;
                }
            }

            return obj;
        }

        if (node is JsonArray array && TryGetEnumerableElementType(targetType, out var elementType))
        {
            var elementTypeInfo = GetTypeInfo(options, elementType);
            for (var i = 0; i < array.Count; i++)
            {
                var current = array[i];
                var normalized = NormalizeYamlNode(current, elementTypeInfo, options);
                if (!ReferenceEquals(current, normalized))
                    array[i] = normalized;
            }
        }

        return node;
    }

    private static JsonNode? ConvertScalarToString(JsonNode node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text))
                return JsonValue.Create(text);

            if (value.TryGetValue<JsonElement>(out var element))
            {
                return JsonValue.Create(element.ValueKind == JsonValueKind.String
                    ? element.GetString()
                    : element.GetRawText());
            }
        }

        return JsonValue.Create(node.ToString());
    }

    private static JsonTypeInfo? GetTypeInfo(JsonSerializerOptions options, Type type)
    {
        try
        {
            return options.GetTypeInfo(type);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static bool TryGetDictionaryValueType(Type type, out Type valueType)
    {
        foreach (var candidate in GetSelfAndInterfaces(type))
        {
            if (!candidate.IsGenericType)
                continue;

            var definition = candidate.GetGenericTypeDefinition();
            if (definition != typeof(IDictionary<,>) &&
                definition != typeof(IReadOnlyDictionary<,>) &&
                definition != typeof(Dictionary<,>))
                continue;

            var arguments = candidate.GetGenericArguments();
            if (arguments[0] == typeof(string))
            {
                valueType = arguments[1];
                return true;
            }
        }

        valueType = typeof(object);
        return false;
    }

    private static bool TryGetEnumerableElementType(Type type, out Type elementType)
    {
        if (type == typeof(string))
        {
            elementType = typeof(object);
            return false;
        }

        if (type.IsArray)
        {
            elementType = type.GetElementType() ?? typeof(object);
            return true;
        }

        foreach (var candidate in GetSelfAndInterfaces(type))
        {
            if (!candidate.IsGenericType)
                continue;

            var definition = candidate.GetGenericTypeDefinition();
            if (definition != typeof(IEnumerable<>) &&
                definition != typeof(IReadOnlyList<>) &&
                definition != typeof(IList<>) &&
                definition != typeof(List<>))
                continue;

            elementType = candidate.GetGenericArguments()[0];
            return true;
        }

        elementType = typeof(object);
        return false;
    }

    private static IEnumerable<Type> GetSelfAndInterfaces(Type type)
    {
        yield return type;
        foreach (var candidate in type.GetInterfaces())
            yield return candidate;
    }
}
