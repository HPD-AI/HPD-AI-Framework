// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations.Batch;

/// <summary>
/// A single evaluation test case with input, optional ground truth, and metadata.
/// </summary>
public sealed class EvalCase<TInput>
{
    /// <summary>Optional name for reporting. Defaults to "case-N" if null.</summary>
    public string? Name { get; init; }

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

    /// <summary>Deserialize dataset from JSON.</summary>
    public static Dataset<TInput> FromJson(string json)
    {
        var dto = JsonSerializer.Deserialize<DatasetDto<TInput>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return dto?.ToDataset() ?? new Dataset<TInput>();
    }

    /// <summary>Save dataset to a JSON file.</summary>
    public void ToFile(string path)
        => File.WriteAllText(path, JsonSerializer.Serialize(ToDto(),
            new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>Generate JSON Schema for IDE $schema autocompletion.</summary>
    public static string GenerateJsonSchema()
    {
        // Simplified schema — a full implementation would use AIJsonUtilities.CreateJsonSchema
        return """{"$schema":"http://json-schema.org/draft-07/schema#","type":"object","properties":{"cases":{"type":"array","items":{"type":"object"}}}}""";
    }

    private DatasetDto<TInput> ToDto() => new()
    {
        Cases = Cases.Select(c => new EvalCaseDto<TInput>
        {
            Name = c.Name,
            Input = c.Input,
            GroundTruth = c.GroundTruth,
            Metadata = c.Metadata,
        }).ToList(),
    };
}

// ── DTOs for serialization (no IEvaluator — those are code-only) ─────────────

internal sealed class DatasetDto<TInput>
{
    [JsonPropertyName("cases")]
    public List<EvalCaseDto<TInput>> Cases { get; set; } = [];

    public Dataset<TInput> ToDataset() => new()
    {
        Cases = Cases.Select(c => new EvalCase<TInput>
        {
            Name = c.Name,
            Input = c.Input,
            GroundTruth = c.GroundTruth,
            Metadata = c.Metadata,
        }).ToList(),
    };
}

internal sealed class EvalCaseDto<TInput>
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("input")] public required TInput Input { get; set; }
    [JsonPropertyName("ground_truth")] public string? GroundTruth { get; set; }
    [JsonPropertyName("metadata")] public IDictionary<string, object>? Metadata { get; set; }
}
