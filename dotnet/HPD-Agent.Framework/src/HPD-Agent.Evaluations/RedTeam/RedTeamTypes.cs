// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent.Evaluations.Batch;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations.RedTeam;

/// <summary>Broad category for a red-team attack case.</summary>
public enum RedTeamCategory
{
    PromptInjection,
    Jailbreak,
    ToolAbuse,
    DataLeakage,
    Privacy,
    CodeInjection,
    AccessControl,
    Agency,
    Safety,
    Policy,
    Reliability,
}

/// <summary>Intended severity of a generated red-team case.</summary>
public enum RedTeamSeverity
{
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>
/// A generated adversarial case. It is not a separate runner primitive; it converts
/// into an ordinary EvalCase and runs through HPD's existing evaluation pipeline.
/// </summary>
public sealed record RedTeamCase
{
    public string Id { get; init; } = string.Empty;
    public string PluginId { get; init; } = string.Empty;
    public string? StrategyId { get; init; }
    public RedTeamCategory Category { get; init; }
    public RedTeamSeverity Severity { get; init; } = RedTeamSeverity.Medium;
    public string Goal { get; init; } = string.Empty;
    public string Input { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }
    public IReadOnlyList<IEvaluator> Evaluators { get; init; } = [];
}

/// <summary>Context passed to red-team case generators.</summary>
public sealed record RedTeamGenerationContext
{
    public int CasesPerPlugin { get; init; } = 5;
    public IReadOnlyList<string> SeedInputs { get; init; } = [];
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }
    public IReadOnlyList<IEvaluator> GlobalEvaluators { get; init; } = [];
}

/// <summary>Context passed to red-team mutation strategies.</summary>
public sealed record RedTeamStrategyContext
{
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }
}

/// <summary>Generates base adversarial cases for one attack family.</summary>
public interface IRedTeamPlugin
{
    string Id { get; }
    string DisplayName { get; }
    RedTeamCategory Category { get; }

    ValueTask<IReadOnlyList<RedTeamCase>> GenerateAsync(
        RedTeamGenerationContext context,
        CancellationToken ct = default);
}

/// <summary>Mutates or wraps adversarial cases to test bypass strategies.</summary>
public interface IRedTeamStrategy
{
    string Id { get; }
    string DisplayName { get; }

    ValueTask<IReadOnlyList<RedTeamCase>> ApplyAsync(
        IReadOnlyList<RedTeamCase> cases,
        RedTeamStrategyContext context,
        CancellationToken ct = default);
}

/// <summary>Conversion helpers between red-team cases and ordinary HPD eval cases.</summary>
public static class RedTeamCaseExtensions
{
    public const string MetadataCaseId = "red_team_case_id";
    public const string MetadataPluginId = "red_team_plugin_id";
    public const string MetadataStrategyId = "red_team_strategy_id";
    public const string MetadataCategory = "red_team_category";
    public const string MetadataSeverity = "red_team_severity";
    public const string MetadataGoal = "red_team_goal";

    public static EvalCase<string> ToEvalCase(this RedTeamCase redTeamCase)
    {
        ArgumentNullException.ThrowIfNull(redTeamCase);

        return new EvalCase<string>
        {
            CaseId = redTeamCase.Id,
            Name = redTeamCase.Id,
            Input = redTeamCase.Input,
            Metadata = BuildMetadata(redTeamCase),
            Evaluators = redTeamCase.Evaluators,
        };
    }

    public static Dataset<string> ToDataset(
        this IEnumerable<RedTeamCase> redTeamCases,
        string? datasetId = null,
        string? version = null,
        IReadOnlyList<IEvaluator>? evaluators = null)
    {
        ArgumentNullException.ThrowIfNull(redTeamCases);

        return new Dataset<string>
        {
            DatasetId = datasetId,
            Version = version,
            Cases = redTeamCases.Select(c => c.ToEvalCase()).ToList(),
            Evaluators = evaluators ?? [],
        };
    }

    internal static Dictionary<string, object> BuildMetadata(RedTeamCase redTeamCase)
    {
        var metadata = redTeamCase.Metadata is null
            ? new Dictionary<string, object>(StringComparer.Ordinal)
            : new Dictionary<string, object>(redTeamCase.Metadata, StringComparer.Ordinal);

        metadata[MetadataCaseId] = redTeamCase.Id;
        metadata[MetadataPluginId] = redTeamCase.PluginId;
        if (!string.IsNullOrWhiteSpace(redTeamCase.StrategyId))
            metadata[MetadataStrategyId] = redTeamCase.StrategyId!;
        metadata[MetadataCategory] = redTeamCase.Category.ToString();
        metadata[MetadataSeverity] = redTeamCase.Severity.ToString();
        metadata[MetadataGoal] = redTeamCase.Goal;

        return metadata;
    }
}
