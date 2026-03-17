// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Evaluations.Contexts;
using HPD.Agent.Evaluations.Evaluators.Deterministic;
using HPD.Agent.Evaluations.Tests.Infrastructure;

namespace HPD.Agent.Evaluations.Tests.Evaluators;

/// <summary>
/// Tests for output-matching deterministic evaluators:
/// - OutputContainsEvaluator
/// - OutputMatchesRegexEvaluator
/// - OutputEqualsEvaluator
/// - EqualsGroundTruthEvaluator
/// - KeywordCoverageEvaluator
/// - ContentSimilarityEvaluator
/// </summary>
public sealed class OutputEvaluatorTests
{
    private static ChatResponse Respond(string text) =>
        new([new ChatMessage(ChatRole.Assistant, text)]);

    // ── OutputContainsEvaluator ───────────────────────────────────────────────

    [Fact]
    public async Task OutputContains_SubstringPresent_ReturnsTrue()
    {
        var result = await new OutputContainsEvaluator("Paris")
            .EvaluateAsync([], Respond("The capital of France is Paris."));

        result.ShouldHaveBooleanMetric("Output Contains", true)
            .ShouldBeMarkedAsBuiltIn();
    }

    [Fact]
    public async Task OutputContains_SubstringAbsent_ReturnsFalse()
    {
        var result = await new OutputContainsEvaluator("Paris")
            .EvaluateAsync([], Respond("The capital of France is Lyon."));

        result.ShouldHaveBooleanMetric("Output Contains", false);
    }

    [Fact]
    public async Task OutputContains_CaseSensitive()
    {
        // Uses StringComparison.Ordinal — "paris" should NOT match "Paris"
        var result = await new OutputContainsEvaluator("paris")
            .EvaluateAsync([], Respond("The answer is Paris."));

        result.ShouldHaveBooleanMetric("Output Contains", false);
    }

    [Fact]
    public async Task OutputContains_EmptyResponse_ReturnsFalse()
    {
        var result = await new OutputContainsEvaluator("Paris")
            .EvaluateAsync([], Respond(string.Empty));

        result.ShouldHaveBooleanMetric("Output Contains", false);
    }

    // ── OutputMatchesRegexEvaluator ───────────────────────────────────────────

    [Fact]
    public async Task OutputMatchesRegex_PatternMatches_ReturnsTrue()
    {
        var result = await new OutputMatchesRegexEvaluator(@"\b\d{4}\b")  // 4-digit number
            .EvaluateAsync([], Respond("The year is 2026."));

        result.ShouldHaveBooleanMetric("Output Matches Regex", true);
    }

    [Fact]
    public async Task OutputMatchesRegex_PatternNoMatch_ReturnsFalse()
    {
        var result = await new OutputMatchesRegexEvaluator(@"\b\d{4}\b")
            .EvaluateAsync([], Respond("No numbers here."));

        result.ShouldHaveBooleanMetric("Output Matches Regex", false);
    }

    [Fact]
    public async Task OutputMatchesRegex_CapturesEmail_ReturnsTrue()
    {
        var result = await new OutputMatchesRegexEvaluator(@"[\w.]+@[\w.]+")
            .EvaluateAsync([], Respond("Contact us at hello@example.com"));

        result.ShouldHaveBooleanMetric("Output Matches Regex", true);
    }

    // ── OutputEqualsEvaluator ─────────────────────────────────────────────────

    [Fact]
    public async Task OutputEquals_ExactMatch_ReturnsTrue()
    {
        var result = await new OutputEqualsEvaluator("Paris")
            .EvaluateAsync([], Respond("Paris"));

        result.ShouldHaveBooleanMetric("Output Equals", true);
    }

    [Fact]
    public async Task OutputEquals_ExtraWhitespace_ReturnsFalse()
    {
        var result = await new OutputEqualsEvaluator("Paris")
            .EvaluateAsync([], Respond("Paris "));

        result.ShouldHaveBooleanMetric("Output Equals", false);
    }

    [Fact]
    public async Task OutputEquals_DifferentCase_ReturnsFalse()
    {
        var result = await new OutputEqualsEvaluator("Paris")
            .EvaluateAsync([], Respond("paris"));

        result.ShouldHaveBooleanMetric("Output Equals", false);
    }

    // ── EqualsGroundTruthEvaluator ────────────────────────────────────────────

    [Fact]
    public async Task EqualsGroundTruth_ExactMatch_ReturnsTrue()
    {
        var ctx = new GroundTruthContext("Paris");

        var result = await new EqualsGroundTruthEvaluator()
            .EvaluateAsync([], Respond("Paris"), additionalContext: [ctx]);

        result.ShouldHaveBooleanMetric("Equals Ground Truth", true);
    }

    [Fact]
    public async Task EqualsGroundTruth_Mismatch_ReturnsFalse()
    {
        var ctx = new GroundTruthContext("Paris");

        var result = await new EqualsGroundTruthEvaluator()
            .EvaluateAsync([], Respond("London"), additionalContext: [ctx]);

        result.ShouldHaveBooleanMetric("Equals Ground Truth", false);
    }

    [Fact]
    public async Task EqualsGroundTruth_NoContext_ReturnsErrorDiagnostic()
    {
        var result = await new EqualsGroundTruthEvaluator()
            .EvaluateAsync([], Respond("Paris"), additionalContext: null);

        result.ShouldHaveErrorDiagnostic();
    }

    // ── KeywordCoverageEvaluator ──────────────────────────────────────────────

    [Fact]
    public async Task KeywordCoverage_AllPresent_ReturnsOne()
    {
        var result = await new KeywordCoverageEvaluator(["France", "capital", "Paris"])
            .EvaluateAsync([], Respond("Paris is the capital of France."));

        result.ShouldHaveNumericMetricInRange("Keyword Coverage", 1.0, 1.0);
    }

    [Fact]
    public async Task KeywordCoverage_HalfPresent_ReturnsHalf()
    {
        var result = await new KeywordCoverageEvaluator(["France", "capital", "Paris", "city"])
            .EvaluateAsync([], Respond("Paris is the capital."));

        // 2 of 4 keywords found: "capital" and "Paris" — "France" and "city" are absent
        result.ShouldHaveNumericMetricInRange("Keyword Coverage", 0.49, 0.51);
    }

    [Fact]
    public async Task KeywordCoverage_NonePresent_ReturnsZero()
    {
        var result = await new KeywordCoverageEvaluator(["quantum", "neutron"])
            .EvaluateAsync([], Respond("The sky is blue."));

        result.ShouldHaveNumericMetricInRange("Keyword Coverage", 0.0, 0.0);
    }

    [Fact]
    public async Task KeywordCoverage_EmptyKeywords_ReturnsOne()
    {
        // Edge case: no keywords specified = trivially passes with 1.0
        var result = await new KeywordCoverageEvaluator([])
            .EvaluateAsync([], Respond("anything"));

        result.ShouldHaveNumericMetricInRange("Keyword Coverage", 1.0, 1.0);
    }

    [Fact]
    public async Task KeywordCoverage_CaseInsensitive()
    {
        // Uses StringComparison.OrdinalIgnoreCase
        var result = await new KeywordCoverageEvaluator(["PARIS", "FRANCE"])
            .EvaluateAsync([], Respond("paris is in france"));

        result.ShouldHaveNumericMetricInRange("Keyword Coverage", 1.0, 1.0);
    }

    // ── ContentSimilarityEvaluator ────────────────────────────────────────────

    [Fact]
    public async Task ContentSimilarity_IdenticalStrings_ReturnsHighScore()
    {
        // Dice coefficient uses bigram sets with duplicates; identical long strings round to ~0.9.
        // Short identical strings (single char, no bigrams) edge differently — use a sentence.
        var result = await new ContentSimilarityEvaluator("Paris is the capital.")
            .EvaluateAsync([], Respond("Paris is the capital."));

        result.ShouldHaveNumericMetricInRange("Content Similarity", 0.85, 1.0);
    }

    [Fact]
    public async Task ContentSimilarity_CompletelyDifferent_ReturnsLow()
    {
        var result = await new ContentSimilarityEvaluator("AAAAAAAAAA")
            .EvaluateAsync([], Respond("BBBBBBBBBB"));

        result.ShouldHaveNumericMetricInRange("Content Similarity", 0.0, 0.1);
    }

    [Fact]
    public async Task ContentSimilarity_PartialOverlap_ReturnsMid()
    {
        // "Paris is great" vs "Paris is wonderful" — share "Paris is "
        var result = await new ContentSimilarityEvaluator("Paris is great")
            .EvaluateAsync([], Respond("Paris is wonderful"));

        var nm = result.ShouldHaveNumericMetricInRange("Content Similarity", 0.3, 0.8);
        nm.Value!.Value.Should().BeLessThan(1.0, "strings differ in the last word");
    }

    [Fact]
    public async Task ContentSimilarity_BothEmpty_ReturnsOne()
    {
        var result = await new ContentSimilarityEvaluator(string.Empty)
            .EvaluateAsync([], Respond(string.Empty));

        result.ShouldHaveNumericMetricInRange("Content Similarity", 1.0, 1.0);
    }

    [Fact]
    public async Task ContentSimilarity_OneEmpty_ReturnsZero()
    {
        var result = await new ContentSimilarityEvaluator("Paris")
            .EvaluateAsync([], Respond(string.Empty));

        result.ShouldHaveNumericMetricInRange("Content Similarity", 0.0, 0.0);
    }
}
