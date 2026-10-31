// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace HPD.Serialization;

internal static class HpdYamlJsonBridge
{
    public static JsonNode? ParseYaml(string yaml) => ParseDocument(yaml).Root;

    public static HpdConfigDocument ParseDocument(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count > 1) throw new JsonException("Expected one YAML document.");
        var locations = new Dictionary<string, HpdConfigSourceLocation>(StringComparer.Ordinal);
        var root = stream.Documents.Count == 0 ? null : ConvertNode(stream.Documents[0].RootNode, "", locations);
        return new(root, new System.Collections.ObjectModel.ReadOnlyDictionary<string, HpdConfigSourceLocation>(locations));
    }

    public static string WriteYaml(JsonNode? node)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var emitter = new Emitter(writer);

        emitter.Emit(new StreamStart());
        emitter.Emit(new DocumentStart());
        EmitJsonNode(emitter, node);
        emitter.Emit(new DocumentEnd(false));
        emitter.Emit(new StreamEnd());

        return writer.ToString();
    }

    private static JsonNode? ConvertNode(YamlNode node, string path, Dictionary<string, HpdConfigSourceLocation> locations)
    {
        locations.TryAdd(path, new(node.Start.Line + 1, node.Start.Column + 1));
        if (node is YamlScalarNode scalar) return ConvertScalar(scalar);
        if (node is YamlSequenceNode sequence)
        {
            var array = new JsonArray(); var index = 0;
            foreach (var child in sequence.Children) array.Add(ConvertNode(child, path + "[" + index++ + "]", locations));
            return array;
        }
        if (node is YamlMappingNode mapping)
        {
            var json = new JsonObject();
            foreach (var (keyNode, valueNode) in mapping.Children)
            {
                if (keyNode is not YamlScalarNode key || string.IsNullOrEmpty(key.Value))
                    throw new JsonException("Configuration mapping keys must be nonempty strings.");
                var child = path.Length == 0 ? key.Value : path + "." + key.Value;
                if (json.ContainsKey(key.Value)) throw new JsonException($"Duplicate configuration key '{child}'.");
                locations[child] = new(key.Start.Line + 1, key.Start.Column + 1);
                json[key.Value] = ConvertNode(valueNode, child, locations);
            }
            return json;
        }
        throw new JsonException("Unsupported YAML configuration node.");
    }

    private static JsonNode? ConvertScalar(YamlScalarNode scalar)
    {
        var value = scalar.Value;
        if (scalar.Style is ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted or ScalarStyle.Literal or ScalarStyle.Folded)
            return JsonValue.Create(value ?? "");
        if (value is null || value == "~" || value.Equals("null", StringComparison.OrdinalIgnoreCase))
            return null;

        if (bool.TryParse(value, out var boolValue))
            return JsonValue.Create(boolValue);

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
            return JsonValue.Create(longValue);

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
            return JsonValue.Create(doubleValue);

        return JsonValue.Create(value);
    }

    private static void EmitJsonNode(IEmitter emitter, JsonNode? node)
    {
        switch (node)
        {
            case null:
                emitter.Emit(new Scalar(null, null, "null", ScalarStyle.Plain, true, false));
                break;
            case JsonObject obj:
                emitter.Emit(new MappingStart(null, null, false, MappingStyle.Block));
                foreach (var (key, value) in obj)
                {
                    emitter.Emit(new Scalar(null, null, key, ScalarStyle.Plain, true, false));
                    EmitJsonNode(emitter, value);
                }
                emitter.Emit(new MappingEnd());
                break;
            case JsonArray array:
                emitter.Emit(new SequenceStart(null, null, false, SequenceStyle.Block));
                foreach (var value in array)
                    EmitJsonNode(emitter, value);
                emitter.Emit(new SequenceEnd());
                break;
            case JsonValue value:
                var isString = value.TryGetValue<string>(out _) ||
                    value.TryGetValue<JsonElement>(out var element) && element.ValueKind == JsonValueKind.String;
                var scalar = ConvertJsonValue(value);
                var requiresQuoting = isString && RequiresQuoting(scalar);
                emitter.Emit(new Scalar(
                    null,
                    null,
                    scalar,
                    requiresQuoting ? ScalarStyle.DoubleQuoted : ScalarStyle.Any,
                    true,
                    !isString));
                break;
        }
    }

    private static string ConvertJsonValue(JsonValue value)
    {
        if (value.TryGetValue<JsonElement>(out var element))
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "null",
                _ => element.ToString(),
            };
        }

        if (value.TryGetValue<string>(out var text))
            return text;

        if (value.TryGetValue<bool>(out var boolean))
            return boolean ? "true" : "false";

        if (value.TryGetValue<int>(out var intValue))
            return intValue.ToString(CultureInfo.InvariantCulture);

        if (value.TryGetValue<long>(out var longValue))
            return longValue.ToString(CultureInfo.InvariantCulture);

        if (value.TryGetValue<double>(out var doubleValue))
            return doubleValue.ToString(CultureInfo.InvariantCulture);

        if (value.TryGetValue<float>(out var floatValue))
            return floatValue.ToString(CultureInfo.InvariantCulture);

        if (value.TryGetValue<decimal>(out var decimalValue))
            return decimalValue.ToString(CultureInfo.InvariantCulture);

        return value.ToString();
    }

    private static bool RequiresQuoting(string value)
        => bool.TryParse(value, out _) ||
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ||
            value.Equals("null", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("~", StringComparison.Ordinal) ||
            value.Length == 0;
}
