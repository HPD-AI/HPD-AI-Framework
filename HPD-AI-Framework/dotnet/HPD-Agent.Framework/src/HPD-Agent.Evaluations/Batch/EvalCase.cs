// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations.Batch;

/// <summary>
/// A single evaluation test case with input, optional ground truth, and metadata.
/// </summary>
public sealed class EvalCase<TInput>
{
    /// <summary>Stable identifier for this logical dataset item across revisions.</summary>
    public string? CaseId { get; init; }

    /// <summary>Optional name for reporting. Defaults to "case-N" if null.</summary>
    public string? Name { get; init; }

    /// <summary>Version/revision of this case. Increment when input, ground truth, or metadata changes.</summary>
    public string? Version { get; init; }

    /// <summary>SCD-2 validity start for this case revision.</summary>
    public DateTimeOffset? ValidFrom { get; init; }

    /// <summary>SCD-2 validity end for this case revision. Null means current.</summary>
    public DateTimeOffset? ValidTo { get; init; }

    /// <summary>The input sent to the agent.</summary>
    public required TInput Input { get; init; }

    /// <summary>Expected output text for ground truth evaluators.</summary>
    public string? GroundTruth { get; init; }

    /// <summary>Arbitrary key-value metadata for filtering and reporting.</summary>
    public IDictionary<string, object>? Metadata { get; init; }

    /// <summary>Case-specific evaluators (run in addition to dataset-level evaluators).</summary>
    public IReadOnlyList<IEvaluator>? Evaluators { get; init; }

    /// <summary>Case-specific report evaluators (run in addition to dataset-level report evaluators).</summary>
    public IReadOnlyList<IReportEvaluator>? ReportEvaluators { get; init; }
}

/// <summary>
/// A collection of evaluation test cases with shared evaluators and serialization support.
/// </summary>
public sealed class Dataset<TInput>
{
    /// <summary>Stable identifier for this dataset across versions.</summary>
    public string? DatasetId { get; init; }

    /// <summary>Dataset version used to tie score records back to an immutable benchmark revision.</summary>
    public string? Version { get; init; }

    /// <summary>All test cases in this dataset.</summary>
    public IReadOnlyList<EvalCase<TInput>> Cases { get; init; } = [];

    /// <summary>Evaluators applied to ALL cases in this dataset.</summary>
    public IReadOnlyList<IEvaluator> Evaluators { get; init; } = [];

    /// <summary>Report-level evaluators run once after all cases complete.</summary>
    public IReadOnlyList<IReportEvaluator> ReportEvaluators { get; init; } = [];

    // ── Serialization ─────────────────────────────────────────────────────────

    /// <summary>Load dataset from a JSON file.</summary>
    public static Dataset<TInput> FromFile(string path)
        => FromJson(File.ReadAllText(path));

    /// <summary>Load dataset from a YAML file using a caller-supplied AOT-safe input parser.</summary>
    public static Dataset<TInput> FromYamlFile(string path, Func<JsonNode?, TInput> parseInput)
        => FromYaml(File.ReadAllText(path), parseInput);

    /// <summary>Deserialize dataset from JSON.</summary>
    public static Dataset<TInput> FromJson(string json)
    {
        var dto = JsonSerializer.Deserialize<DatasetDto<TInput>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return dto?.ToDataset() ?? new Dataset<TInput>();
    }

    /// <summary>Deserialize dataset from YAML using a caller-supplied AOT-safe input parser.</summary>
    public static Dataset<TInput> FromYaml(string yaml, Func<JsonNode?, TInput> parseInput)
        => DatasetYamlSerializer.FromYaml(yaml, parseInput);

    /// <summary>Save dataset to a JSON file.</summary>
    public void ToFile(string path)
        => File.WriteAllText(path, JsonSerializer.Serialize(ToDto(),
            new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>Save dataset to a YAML file using a caller-supplied AOT-safe input serializer.</summary>
    public void ToYamlFile(string path, Func<TInput, JsonNode?> serializeInput)
        => File.WriteAllText(path, ToYaml(serializeInput));

    /// <summary>Serialize dataset to YAML using a caller-supplied AOT-safe input serializer.</summary>
    public string ToYaml(Func<TInput, JsonNode?> serializeInput)
        => DatasetYamlSerializer.ToYaml(this, serializeInput);

    /// <summary>Generate JSON Schema for IDE $schema autocompletion.</summary>
    public static string GenerateJsonSchema()
        => DatasetSchemaGenerator.Generate<TInput>();

    /// <summary>
    /// Generate JSON Schema using caller-supplied serializer options for the input type.
    /// Native AOT callers should pass source-generated options for custom input types.
    /// </summary>
    public static string GenerateJsonSchema(JsonSerializerOptions serializerOptions)
        => DatasetSchemaGenerator.Generate<TInput>(serializerOptions);

    /// <summary>
    /// Generate JSON Schema using source-generated metadata for the input type.
    /// This is the preferred overload for Native AOT callers.
    /// </summary>
    public static string GenerateJsonSchema(JsonTypeInfo<TInput> inputTypeInfo)
        => DatasetSchemaGenerator.Generate(inputTypeInfo);

    internal DatasetDto<TInput> ToDto() => new()
    {
        Cases = Cases.Select(c => new EvalCaseDto<TInput>
        {
            CaseId = c.CaseId,
            Name = c.Name,
            Version = c.Version,
            ValidFrom = c.ValidFrom,
            ValidTo = c.ValidTo,
            Input = c.Input,
            GroundTruth = c.GroundTruth,
            Metadata = c.Metadata,
        }).ToList(),
        DatasetId = DatasetId,
        Version = Version,
    };
}

// ── DTOs for serialization (no IEvaluator — those are code-only) ─────────────

internal sealed class DatasetDto<TInput>
{
    [JsonPropertyName("cases")]
    public List<EvalCaseDto<TInput>> Cases { get; set; } = [];

    [JsonPropertyName("dataset_id")]
    public string? DatasetId { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("evaluators")]
    public List<JsonElement>? Evaluators { get; set; }

    public Dataset<TInput> ToDataset() => new()
    {
        Cases = Cases.Select(c => new EvalCase<TInput>
        {
            CaseId = c.CaseId,
            Name = c.Name,
            Version = c.Version,
            ValidFrom = c.ValidFrom,
            ValidTo = c.ValidTo,
            Input = c.Input,
            GroundTruth = c.GroundTruth,
            Metadata = c.Metadata,
            Evaluators = DatasetEvaluatorFactory.CreateMany(c.Evaluators),
        }).ToList(),
        DatasetId = DatasetId,
        Version = Version,
        Evaluators = DatasetEvaluatorFactory.CreateMany(Evaluators),
    };
}

internal sealed class EvalCaseDto<TInput>
{
    [JsonPropertyName("case_id")] public string? CaseId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("valid_from")] public DateTimeOffset? ValidFrom { get; set; }
    [JsonPropertyName("valid_to")] public DateTimeOffset? ValidTo { get; set; }
    [JsonPropertyName("input")] public required TInput Input { get; set; }
    [JsonPropertyName("ground_truth")] public string? GroundTruth { get; set; }
    [JsonPropertyName("metadata")] public IDictionary<string, object>? Metadata { get; set; }
    [JsonPropertyName("evaluators")] public List<JsonElement>? Evaluators { get; set; }
}
