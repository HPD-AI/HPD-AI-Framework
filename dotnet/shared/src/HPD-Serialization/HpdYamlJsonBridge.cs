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
    public static JsonNode? ParseYaml(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));

        return stream.Documents.Count == 0
            ? null
            : ConvertNode(stream.Documents[0].RootNode);
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

    private static JsonNode? ConvertNode(YamlNode node) => node switch
    {
        YamlMappingNode mapping => ConvertMapping(mapping),
        YamlSequenceNode sequence => ConvertSequence(sequence),
        YamlScalarNode scalar => ConvertScalar(scalar),
        _ => null,
    };

    private static JsonObject ConvertMapping(YamlMappingNode mapping)
    {
        var json = new JsonObject();
        foreach (var (keyNode, valueNode) in mapping.Children)
        {
            if (keyNode is not YamlScalarNode key || string.IsNullOrWhiteSpace(key.Value))
                continue;

            json[key.Value] = ConvertNode(valueNode);
        }

        return json;
    }

    private static JsonArray ConvertSequence(YamlSequenceNode sequence)
    {
        var json = new JsonArray();
        foreach (var child in sequence.Children)
            json.Add(ConvertNode(child));

        return json;
    }

    private static JsonNode? ConvertScalar(YamlScalarNode scalar)
    {
        var value = scalar.Value;
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
                emitter.Emit(new Scalar(null, null, ConvertJsonValue(value), ScalarStyle.Any, true, false));
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
}
