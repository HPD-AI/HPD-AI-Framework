// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using HPD.Agent.Evaluations.Evaluators.Deterministic;
using HPD.Agent.Evaluations.Evaluators.LlmJudge;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations.Batch;

internal static class DatasetEvaluatorFactory
{
    public static IReadOnlyList<IEvaluator> CreateMany(IEnumerable<JsonElement>? definitions)
        => definitions?.Select(Create).ToList() ?? [];

    private static IEvaluator Create(JsonElement definition)
    {
        if (definition.ValueKind == JsonValueKind.String)
            return CreateByName(definition.GetString() ?? string.Empty);

        if (definition.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Evaluator definitions must be strings or single-property objects.");

        var properties = definition.EnumerateObject().ToList();
        if (properties.Count != 1)
            throw new InvalidOperationException("Object evaluator definitions must contain exactly one evaluator name.");

        var property = properties[0];
        return CreateParameterized(property.Name, property.Value);
    }

    private static IEvaluator CreateByName(string name) => NormalizeName(name) switch
    {
        "equalsgroundtruth" => new EqualsGroundTruthEvaluator(),
        "notoolscalled" => new NoToolsCalledEvaluator(),
        _ => throw new InvalidOperationException($"Unknown evaluator '{name}'."),
    };

    private static IEvaluator CreateParameterized(string name, JsonElement value) => NormalizeName(name) switch
    {
        "outputcontains" => new OutputContainsEvaluator(GetString(value, name)),
        "outputmatchesregex" => new OutputMatchesRegexEvaluator(GetString(value, name)),
        "outputequals" => new OutputEqualsEvaluator(GetString(value, name)),
        "contentsimilarity" => new ContentSimilarityEvaluator(GetString(value, name)),
        "keywordcoverage" => new KeywordCoverageEvaluator(GetStringArray(value, name)),
        "maxduration" => new MaxDurationEvaluator(GetDouble(value, name)),
        "maxiterations" => new MaxIterationsEvaluator(GetInt(value, name)),
        "maxtokens" => new MaxTokensEvaluator(GetInt(value, name)),
        "maxinputtokens" => new MaxInputTokensEvaluator(GetInt(value, name)),
        "maxoutputtokens" => new MaxOutputTokensEvaluator(GetInt(value, name)),
        "toolwascalled" => new ToolWasCalledEvaluator(GetString(value, name)),
        "toolcallcount" => CreateToolCallCount(value),
        "toolargumentmatches" => CreateToolArgumentMatches(value),
        "toolcallorder" => new ToolCallOrderEvaluator(GetStringArray(value, name)),
        "aspectcritic" => new AspectCriticEvaluator(GetRubric(value, name)),
        _ => throw new InvalidOperationException($"Unknown evaluator '{name}'."),
    };

    private static ToolCallCountEvaluator CreateToolCallCount(JsonElement value)
    {
        var tool = GetPropertyString(value, "tool");
        var count = GetPropertyInt(value, "count");
        return new ToolCallCountEvaluator(tool, count);
    }

    private static ToolArgumentMatchesEvaluator CreateToolArgumentMatches(JsonElement value)
    {
        var tool = GetPropertyString(value, "tool");
        var argument = GetPropertyString(value, "argument");
        var expected = GetPropertyString(value, "expected");
        return new ToolArgumentMatchesEvaluator(tool, argument, expected);
    }

    private static string GetRubric(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? string.Empty;

        return GetPropertyString(value, "rubric", name);
    }

    private static string GetString(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? string.Empty;

        throw new InvalidOperationException($"Evaluator '{name}' expects a string value.");
    }

    private static string[] GetStringArray(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Evaluator '{name}' expects an array of strings.");

        return value.EnumerateArray()
            .Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : v.ToString())
            .ToArray();
    }

    private static int GetInt(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result))
            return result;

        throw new InvalidOperationException($"Evaluator '{name}' expects an integer value.");
    }

    private static double GetDouble(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var result))
            return result;

        throw new InvalidOperationException($"Evaluator '{name}' expects a numeric value.");
    }

    private static string GetPropertyString(JsonElement value, string propertyName, string? evaluatorName = null)
    {
        if (TryGetProperty(value, propertyName, out var property) && property.ValueKind == JsonValueKind.String)
            return property.GetString() ?? string.Empty;

        throw new InvalidOperationException(
            evaluatorName is null
                ? $"Expected string property '{propertyName}'."
                : $"Evaluator '{evaluatorName}' expects string property '{propertyName}'.");
    }

    private static int GetPropertyInt(JsonElement value, string propertyName)
    {
        if (TryGetProperty(value, propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out var result))
            return result;

        throw new InvalidOperationException($"Expected integer property '{propertyName}'.");
    }

    private static bool TryGetProperty(JsonElement value, string propertyName, out JsonElement property)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var candidate in value.EnumerateObject())
            {
                if (NormalizeName(candidate.Name) == NormalizeName(propertyName))
                {
                    property = candidate.Value;
                    return true;
                }
            }
        }

        property = default;
        return false;
    }

    private static string NormalizeName(string value)
        => value.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("Evaluator", string.Empty, StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();
}
