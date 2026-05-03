// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI.Evaluation;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace HPD.Agent.Evaluations.Batch;

internal static class DatasetYamlSerializer
{
    public static Dataset<TInput> FromYaml<TInput>(string yaml, Func<JsonNode?, TInput> parseInput)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));

        if (stream.Documents.Count == 0)
            return new Dataset<TInput>();

        return ConvertDataset(ConvertNode(stream.Documents[0].RootNode), parseInput);
    }

    public static string ToYaml<TInput>(Dataset<TInput> dataset, Func<TInput, JsonNode?> serializeInput)
    {
        var json = ConvertDataset(dataset, serializeInput);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var emitter = new Emitter(writer);

        emitter.Emit(new StreamStart());
        emitter.Emit(new DocumentStart());
        EmitJsonNode(emitter, json);
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

    private static Dataset<TInput> ConvertDataset<TInput>(JsonNode? root, Func<JsonNode?, TInput> parseInput)
    {
        if (root is not JsonObject rootObject)
            return new Dataset<TInput>();

        return new Dataset<TInput>
        {
            DatasetId = GetString(rootObject["dataset_id"]),
            Version = GetString(rootObject["version"]),
            Evaluators = ConvertEvaluators(rootObject["evaluators"]),
            Cases = ConvertCases(rootObject["cases"], parseInput),
        };
    }

    private static IReadOnlyList<EvalCase<TInput>> ConvertCases<TInput>(
        JsonNode? casesNode,
        Func<JsonNode?, TInput> parseInput)
    {
        if (casesNode is not JsonArray cases)
            return [];

        return cases.OfType<JsonObject>()
            .Select(c => new EvalCase<TInput>
            {
                CaseId = GetString(c["case_id"]),
                Name = GetString(c["name"]),
                Version = GetString(c["version"]),
                ValidFrom = GetDateTimeOffset(c["valid_from"]),
                ValidTo = GetDateTimeOffset(c["valid_to"]),
                Input = parseInput(c["input"]),
                GroundTruth = GetString(c["ground_truth"]),
                Metadata = ConvertMetadata(c["metadata"]),
                Evaluators = ConvertEvaluators(c["evaluators"]),
            })
            .ToList();
    }

    private static IReadOnlyList<IEvaluator> ConvertEvaluators(JsonNode? evaluatorsNode)
    {
        if (evaluatorsNode is not JsonArray evaluators)
            return [];

        return DatasetEvaluatorFactory.CreateMany(evaluators.Select(ToJsonElement));
    }

    private static IDictionary<string, object>? ConvertMetadata(JsonNode? metadataNode)
    {
        if (metadataNode is not JsonObject metadata)
            return null;

        return metadata.ToDictionary(kvp => kvp.Key, kvp => (object)ToJsonElement(kvp.Value));
    }

    private static string? GetString(JsonNode? node)
        => node is JsonValue value
            ? value.TryGetValue<string>(out var text) ? text : value.ToString()
            : null;

    private static DateTimeOffset? GetDateTimeOffset(JsonNode? node)
    {
        var text = GetString(node);
        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static JsonElement ToJsonElement(JsonNode? node)
    {
        using var document = JsonDocument.Parse((node ?? JsonValue.Create((string?)null))!.ToJsonString());
        return document.RootElement.Clone();
    }

    private static JsonObject ConvertDataset<TInput>(
        Dataset<TInput> dataset,
        Func<TInput, JsonNode?> serializeInput)
    {
        var root = new JsonObject
        {
            ["cases"] = new JsonArray(dataset.Cases.Select(c => ConvertCase(c, serializeInput)).ToArray()),
        };

        if (dataset.DatasetId is not null)
            root["dataset_id"] = JsonValue.Create(dataset.DatasetId);

        if (dataset.Version is not null)
            root["version"] = JsonValue.Create(dataset.Version);

        return root;
    }

    private static JsonObject ConvertCase<TInput>(
        EvalCase<TInput> evalCase,
        Func<TInput, JsonNode?> serializeInput)
    {
        var json = new JsonObject
        {
            ["case_id"] = evalCase.CaseId is null ? null : JsonValue.Create(evalCase.CaseId),
            ["name"] = evalCase.Name is null ? null : JsonValue.Create(evalCase.Name),
            ["version"] = evalCase.Version is null ? null : JsonValue.Create(evalCase.Version),
            ["valid_from"] = evalCase.ValidFrom is null ? null : JsonValue.Create(evalCase.ValidFrom.Value.ToString("O", CultureInfo.InvariantCulture)),
            ["valid_to"] = evalCase.ValidTo is null ? null : JsonValue.Create(evalCase.ValidTo.Value.ToString("O", CultureInfo.InvariantCulture)),
            ["input"] = serializeInput(evalCase.Input),
            ["ground_truth"] = evalCase.GroundTruth is null ? null : JsonValue.Create(evalCase.GroundTruth),
        };

        if (evalCase.Metadata is not null)
            json["metadata"] = ConvertMetadata(evalCase.Metadata);

        return json;
    }

    private static JsonObject ConvertMetadata(IDictionary<string, object> metadata)
    {
        var json = new JsonObject();
        foreach (var (key, value) in metadata)
            json[key] = ConvertMetadataValue(value);
        return json;
    }

    private static JsonNode? ConvertMetadataValue(object? value) => value switch
    {
        null => null,
        JsonElement element => JsonNode.Parse(element.GetRawText()),
        JsonNode node => node.DeepClone(),
        string text => JsonValue.Create(text),
        bool boolean => JsonValue.Create(boolean),
        int number => JsonValue.Create(number),
        long number => JsonValue.Create(number),
        double number => JsonValue.Create(number),
        float number => JsonValue.Create(number),
        decimal number => JsonValue.Create(number),
        _ => JsonValue.Create(value.ToString()),
    };

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
