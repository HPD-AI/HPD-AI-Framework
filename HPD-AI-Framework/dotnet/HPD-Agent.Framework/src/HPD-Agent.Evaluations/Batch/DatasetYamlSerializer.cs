// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using HPD.Serialization;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations.Batch;

internal static class DatasetYamlSerializer
{
    public static Dataset<TInput> FromJsonNode<TInput>(JsonNode? node, Func<JsonNode?, TInput> parseInput)
        => ConvertDataset(node, parseInput);

    public static Dataset<TInput> FromYaml<TInput>(string yaml, Func<JsonNode?, TInput> parseInput)
        => ConvertDataset(HpdConfigSerializer.ParseYamlToJsonNode(yaml), parseInput);

    public static JsonNode ToJsonNode<TInput>(Dataset<TInput> dataset, Func<TInput, JsonNode?> serializeInput)
        => ConvertDataset(dataset, serializeInput);

    public static string ToYaml<TInput>(Dataset<TInput> dataset, Func<TInput, JsonNode?> serializeInput)
        => HpdConfigSerializer.WriteYaml(ConvertDataset(dataset, serializeInput));

    private static Dataset<TInput> ConvertDataset<TInput>(JsonNode? root, Func<JsonNode?, TInput> parseInput)
    {
        if (root is not JsonObject rootObject)
            return new Dataset<TInput>();

        RejectUnknownKeys(rootObject, "dataset", "cases", "dataset_id", "version", "evaluators");

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
            .Select(c =>
            {
                RejectUnknownKeys(c, "case", "case_id", "name", "version", "valid_from", "valid_to", "input", "ground_truth", "metadata", "evaluators");

                return new EvalCase<TInput>
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
                };
            })
            .ToList();
    }

    private static void RejectUnknownKeys(JsonObject obj, string scope, params string[] allowedKeys)
    {
        foreach (var key in obj.Select(static kvp => kvp.Key))
        {
            if (!allowedKeys.Contains(key))
                throw new JsonException($"Unknown {scope} property '{key}'.");
        }
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

}
