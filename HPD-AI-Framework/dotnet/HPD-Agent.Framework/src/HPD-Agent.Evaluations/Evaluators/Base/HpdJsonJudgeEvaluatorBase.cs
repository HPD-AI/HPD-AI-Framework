// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations.Evaluators;

/// <summary>
/// Base class for LLM-as-judge evaluators that use JSON structured output.
/// Handles timing, GetResponseAsync with ResponseFormat.Json, JSON parsing,
/// HpdJsonOutputFixer fallback on JsonException, metadata population, MarkAsHpdBuiltIn.
/// Subclasses implement prompt construction, JSON deserialization, and result population.
/// Used by GoalProgressionEvaluator, MemoryAccuracyEvaluator.
/// </summary>
public abstract class HpdJsonJudgeEvaluatorBase<TRating> : HpdEvaluatorBase
    where TRating : class
{
    private static readonly ChatOptions _judgeOptions = new()
    {
        Temperature = 0f,
        ResponseFormat = ChatResponseFormat.Json,
    };

    /// <summary>Build the message list sent to the judge LLM.</summary>
    protected abstract List<ChatMessage> BuildJudgePrompt(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext);

    /// <summary>
    /// Deserialize the judge response JSON into the typed rating object.
    /// Return null if the JSON cannot be parsed even after repair.
    /// </summary>
    protected abstract TRating? ParseRating(string json);

    /// <summary>
    /// Populate metrics on <paramref name="result"/> from the parsed rating.
    /// Add rich metadata fields via metric.AddOrUpdateMetadata().
    /// </summary>
    protected abstract void PopulateResult(
        TRating rating,
        EvaluationResult result,
        ChatResponse judgeResponse,
        TimeSpan duration);

    /// <summary>Creates an EvaluationResult with metric shells for error paths.</summary>
    protected abstract EvaluationResult CreateEmptyResult();

    public sealed override async ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var result = CreateEmptyResult();

        if (chatConfiguration is null)
        {
            result.AddDiagnosticsToAllMetrics(
                EvaluationDiagnostic.Error($"No {nameof(ChatConfiguration)} was provided."));
            return result;
        }

        var prompt = BuildJudgePrompt(messages, modelResponse, additionalContext);

        var sw = Stopwatch.StartNew();
        ChatResponse judgeResponse;
        try
        {
            judgeResponse = await chatConfiguration.ChatClient
                .GetResponseAsync(prompt, _judgeOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.AddDiagnosticsToAllMetrics(
                EvaluationDiagnostic.Error($"Judge LLM call failed: {ex.Message}"));
            return result;
        }
        finally
        {
            sw.Stop();
        }

        // Attempt JSON parse; fall back to LLM-based repair on failure
        TRating? rating = ParseRating(judgeResponse.Text ?? string.Empty);
        if (rating is null)
        {
            string repairedJson = await HpdJsonOutputFixer
                .RepairJsonAsync(judgeResponse.Text ?? string.Empty, chatConfiguration.ChatClient, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(repairedJson))
                rating = ParseRating(repairedJson);
        }

        if (rating is null)
        {
            result.AddDiagnosticsToAllMetrics(
                EvaluationDiagnostic.Error(
                    "Judge response could not be parsed as valid JSON even after repair."));
            return result;
        }

        PopulateResult(rating, result, judgeResponse, sw.Elapsed);
        result.MarkAllMetricsAsHpdBuiltIn();
        return result;
    }
}
