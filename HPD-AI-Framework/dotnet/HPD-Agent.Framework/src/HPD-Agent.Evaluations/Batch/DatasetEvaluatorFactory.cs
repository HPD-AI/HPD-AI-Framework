// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using HPD.Agent.Evaluations.Evaluators.Deterministic;
using HPD.Agent.Evaluations.Evaluators.Composite;
using HPD.Agent.Evaluations.Evaluators.LlmJudge;
using HPD.Agent.Evaluations.Evaluators.Nlp;
using HPD.Agent.Evaluations.Evaluators.Safety;
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
        "jsonvalidity" => new JsonValidityEvaluator(),
        "isjson" => new JsonValidityEvaluator(),
        "xmlvalidity" => new XmlValidityEvaluator(),
        "isxml" => new XmlValidityEvaluator(),
        "htmlshape" => new HtmlShapeEvaluator(),
        "ishtml" => new HtmlShapeEvaluator(),
        "sqlshape" => new SqlShapeEvaluator(),
        "issql" => new SqlShapeEvaluator(),
        "refusal" => new RefusalEvaluator(),
        "isrefusal" => new RefusalEvaluator(),
        "latency" => new LatencyEvaluator(),
        "contentharm" => new ContentHarmEvaluator(),
        "hateharassment" => new HateHarassmentEvaluator(),
        "violencesafety" => new ViolenceSafetyEvaluator(),
        "selfharmsafety" => new SelfHarmSafetyEvaluator(),
        "sexualcontentsafety" => new SexualContentSafetyEvaluator(),
        "promptinjection" => new PromptInjectionEvaluator(),
        "jailbreakattempt" => new JailbreakAttemptEvaluator(),
        "sensitivedataleak" => new SensitiveDataLeakEvaluator(),
        "protectedmaterial" => new ProtectedMaterialEvaluator(),
        "codesecurityrisk" => new CodeSecurityRiskEvaluator(),
        "ungroundedsensitiveattributes" => new UngroundedSensitiveAttributeEvaluator(),
        _ => throw new InvalidOperationException($"Unknown evaluator '{name}'."),
    };

    private static IEvaluator CreateParameterized(string name, JsonElement value) => NormalizeName(name) switch
    {
        "outputcontains" => new OutputContainsEvaluator(GetString(value, name)),
        "containsany" => new ContainsAnyEvaluator(GetStringArray(value, name)),
        "containsall" => new ContainsAllEvaluator(GetStringArray(value, name)),
        "caseinsensitivecontains" => new CaseInsensitiveContainsEvaluator(GetString(value, name)),
        "icontains" => new CaseInsensitiveContainsEvaluator(GetString(value, name)),
        "startswith" => CreateStartsWith(value),
        "wordcount" => CreateWordCount(value),
        "levenshtein" => new LevenshteinEvaluator(GetString(value, name)),
        "htmlshape" => new HtmlShapeEvaluator(GetStringArray(value, name)),
        "outputmatchesregex" => new OutputMatchesRegexEvaluator(GetString(value, name)),
        "outputequals" => new OutputEqualsEvaluator(GetString(value, name)),
        "contentsimilarity" => new ContentSimilarityEvaluator(GetString(value, name)),
        "keywordcoverage" => new KeywordCoverageEvaluator(GetStringArray(value, name)),
        "maxduration" => new MaxDurationEvaluator(GetDouble(value, name)),
        "maxiterations" => new MaxIterationsEvaluator(GetInt(value, name)),
        "maxtokens" => new MaxTokensEvaluator(GetInt(value, name)),
        "maxinputtokens" => new MaxInputTokensEvaluator(GetInt(value, name)),
        "maxoutputtokens" => new MaxOutputTokensEvaluator(GetInt(value, name)),
        "maxcost" => new MaxCostEvaluator(GetDouble(value, name)),
        "toolwascalled" => new ToolWasCalledEvaluator(GetString(value, name)),
        "toolcallcount" => CreateToolCallCount(value),
        "toolargumentmatches" => CreateToolArgumentMatches(value),
        "toolresultcontains" => CreateToolResultContains(value),
        "toolcallorder" => new ToolCallOrderEvaluator(GetStringArray(value, name)),
        "toolcallf1" => new ToolCallF1Evaluator(GetStringArray(value, name)),
        "bleu" => new BleuEvaluator(GetReferences(value, name)),
        "gleu" => new GleuEvaluator(GetReferences(value, name)),
        "textf1" => new TextF1Evaluator(GetGroundTruth(value, name)),
        "f1" => new TextF1Evaluator(GetGroundTruth(value, name)),
        "rouge" => CreateRouge(value, name),
        "meteor" => CreateMeteor(value, name),
        "not" => new NotEvaluator(Create(value)),
        "aspectcritic" => new AspectCriticEvaluator(GetRubric(value, name)),
        "policycompliance" => new PolicyComplianceEvaluator(GetPolicyText(value, name)),
        _ => throw new InvalidOperationException($"Unknown evaluator '{name}'."),
    };

    private static StartsWithEvaluator CreateStartsWith(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
            return new StartsWithEvaluator(value.GetString() ?? string.Empty);

        var prefix = GetPropertyString(value, "value", "StartsWith");
        var ignoreCase = TryGetProperty(value, "ignore_case", out var ignoreCaseElement) &&
                         ignoreCaseElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                         ignoreCaseElement.GetBoolean();
        return new StartsWithEvaluator(prefix, ignoreCase);
    }

    private static WordCountEvaluator CreateWordCount(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var exact))
            return new WordCountEvaluator(exact: exact);

        int? min = TryGetProperty(value, "min", out var minElement) &&
                   minElement.ValueKind == JsonValueKind.Number &&
                   minElement.TryGetInt32(out var minValue)
            ? minValue
            : null;
        int? max = TryGetProperty(value, "max", out var maxElement) &&
                   maxElement.ValueKind == JsonValueKind.Number &&
                   maxElement.TryGetInt32(out var maxValue)
            ? maxValue
            : null;
        int? exactValue = TryGetProperty(value, "exact", out var exactElement) &&
                          exactElement.ValueKind == JsonValueKind.Number &&
                          exactElement.TryGetInt32(out var exactParsed)
            ? exactParsed
            : null;

        return new WordCountEvaluator(min, max, exactValue);
    }

    private static ToolCallCountEvaluator CreateToolCallCount(JsonElement value)
    {
        var tool = GetPropertyString(value, "tool");
        var count = GetPropertyInt(value, "count");
        return new ToolCallCountEvaluator(tool, count);
    }

    private static RougeEvaluator CreateRouge(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.String)
            return new RougeEvaluator(value.GetString() ?? string.Empty);

        var reference = GetReference(value, name);
        var variant = TryGetProperty(value, "variant", out var variantElement) &&
                      variantElement.ValueKind == JsonValueKind.String
            ? ParseRougeVariant(variantElement.GetString())
            : RougeVariant.RougeL;

        return new RougeEvaluator(reference, variant);
    }

    private static RougeVariant ParseRougeVariant(string? value)
        => NormalizeName(value ?? string.Empty) switch
        {
            "rouge1" or "1" => RougeVariant.Rouge1,
            "rouge2" or "2" => RougeVariant.Rouge2,
            "rougel" or "l" => RougeVariant.RougeL,
            "rouges" or "s" => RougeVariant.RougeS,
            _ => RougeVariant.RougeL,
        };

    private static MeteorEvaluator CreateMeteor(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.String || value.ValueKind == JsonValueKind.Array)
            return new MeteorEvaluator(GetReferences(value, name));

        var references = GetReferences(value, name);
        var options = new MeteorEvaluatorOptions
        {
            Alpha = GetOptionalDouble(value, "alpha") ?? 0.9,
            Beta = GetOptionalDouble(value, "beta") ?? 3.0,
            Gamma = GetOptionalDouble(value, "gamma") ?? 0.5,
        };

        return new MeteorEvaluator(options, references);
    }

    private static ToolArgumentMatchesEvaluator CreateToolArgumentMatches(JsonElement value)
    {
        var tool = GetPropertyString(value, "tool");
        var argument = GetPropertyString(value, "argument");
        var expected = GetPropertyString(value, "expected");
        return new ToolArgumentMatchesEvaluator(tool, argument, expected);
    }

    private static ToolResultContainsEvaluator CreateToolResultContains(JsonElement value)
    {
        var tool = GetPropertyString(value, "tool");
        var expected = GetPropertyString(value, "expected");
        return new ToolResultContainsEvaluator(tool, expected);
    }

    private static string GetRubric(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? string.Empty;

        return GetPropertyString(value, "rubric", name);
    }

    private static string GetPolicyText(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? string.Empty;

        return GetPropertyString(value, "policy", name);
    }

    private static string GetGroundTruth(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? string.Empty;

        return GetPropertyString(value, "ground_truth", name);
    }

    private static string GetReference(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? string.Empty;

        return GetPropertyString(value, "reference", name);
    }

    private static string[] GetReferences(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.String)
            return [value.GetString() ?? string.Empty];

        if (value.ValueKind == JsonValueKind.Array)
            return GetStringArray(value, name);

        if (TryGetProperty(value, "references", out var references))
            return GetStringArray(references, name);

        if (TryGetProperty(value, "reference", out var reference) && reference.ValueKind == JsonValueKind.String)
            return [reference.GetString() ?? string.Empty];

        throw new InvalidOperationException($"Evaluator '{name}' expects a string, an array of strings, or references.");
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

    private static double? GetOptionalDouble(JsonElement value, string propertyName)
    {
        if (TryGetProperty(value, propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetDouble(out var result))
        {
            return result;
        }

        return null;
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
