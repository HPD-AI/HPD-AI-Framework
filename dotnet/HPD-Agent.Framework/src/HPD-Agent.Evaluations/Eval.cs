// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent.Evaluations.Evaluators.Deterministic;
using HPD.Agent.Evaluations.Evaluators.Nlp;
using HPD.Agent.Evaluations.Tracing;
using Microsoft.Extensions.AI.Evaluation;
using System.Text.Json;

namespace HPD.Agent.Evaluations;

/// <summary>
/// Convenience factory for common HPD evaluators.
/// </summary>
public static class Eval
{
    public static IEvaluator Contains(string value) => new OutputContainsEvaluator(value);

    public static IEvaluator ContainsAny(params string[] values) => new ContainsAnyEvaluator(values);

    public static IEvaluator ContainsAll(params string[] values) => new ContainsAllEvaluator(values);

    public static IEvaluator ContainsIgnoringCase(string value) => new CaseInsensitiveContainsEvaluator(value);

    public static IEvaluator OutputEquals(string value) => new OutputEqualsEvaluator(value);

    public static IEvaluator MatchesRegex(string pattern) => new OutputMatchesRegexEvaluator(pattern);

    public static IEvaluator StartsWith(string value, bool ignoreCase = false) => new StartsWithEvaluator(value, ignoreCase);

    public static IEvaluator WordCount(int? min = null, int? max = null, int? exact = null) =>
        new WordCountEvaluator(min, max, exact);

    public static IEvaluator Levenshtein(string expected) => new LevenshteinEvaluator(expected);

    public static IEvaluator Refusal() => new RefusalEvaluator();

    public static IEvaluator EqualsGroundTruth() => new EqualsGroundTruthEvaluator();

    public static IEvaluator KeywordCoverage(params string[] keywords) =>
        new KeywordCoverageEvaluator(keywords);

    public static IEvaluator ContentSimilarity(string expected) =>
        new ContentSimilarityEvaluator(expected);

    public static IEvaluator JsonValidity() => new JsonValidityEvaluator();

    public static IEvaluator XmlValidity() => new XmlValidityEvaluator();

    public static IEvaluator HtmlShape(params string[] requiredTags) =>
        new HtmlShapeEvaluator(requiredTags);

    public static IEvaluator SqlShape() => new SqlShapeEvaluator();

    public static IEvaluator SchemaConformance(string schema) =>
        new SchemaConformanceEvaluator(schema);

    public static IEvaluator SchemaConformance(JsonElement schema) =>
        new SchemaConformanceEvaluator(schema);

    public static IEvaluator FieldCompleteness(params string[] fields) =>
        new FieldCompletenessEvaluator(fields);

    public static IEvaluator FieldAccuracy(string field, string expectedValue) =>
        new FieldAccuracyEvaluator(field, expectedValue);

    public static IEvaluator ToolCalled(string toolName) => new ToolWasCalledEvaluator(toolName);

    public static IEvaluator ToolCallCount(string toolName, int expectedCount) =>
        new ToolCallCountEvaluator(toolName, expectedCount);

    public static IEvaluator ToolArgumentMatches(string toolName, string argumentName, string expectedValue) =>
        new ToolArgumentMatchesEvaluator(toolName, argumentName, expectedValue);

    public static IEvaluator ToolResultContains(string toolName, string expectedText) =>
        new ToolResultContainsEvaluator(toolName, expectedText);

    public static IEvaluator NoToolsCalled() => new NoToolsCalledEvaluator();

    public static IEvaluator ToolCallOrder(params string[] expectedOrder) =>
        new ToolCallOrderEvaluator(expectedOrder);

    public static IEvaluator ToolCallF1(params string[] expectedTools) =>
        new ToolCallF1Evaluator(expectedTools);

    public static IEvaluator MaxDuration(double maxSeconds) =>
        new MaxDurationEvaluator(maxSeconds);

    public static IEvaluator MaxIterations(int maxIterations) =>
        new MaxIterationsEvaluator(maxIterations);

    public static IEvaluator MaxTokens(int maxTokens) =>
        new MaxTokensEvaluator(maxTokens);

    public static IEvaluator MaxInputTokens(int maxTokens) =>
        new MaxInputTokensEvaluator(maxTokens);

    public static IEvaluator MaxOutputTokens(int maxTokens) =>
        new MaxOutputTokensEvaluator(maxTokens);

    public static IEvaluator Latency() => new LatencyEvaluator();

    public static IEvaluator MaxCost(double maxCostUsd) =>
        new MaxCostEvaluator(maxCostUsd);

    public static IEvaluator HasMatchingSpan(SpanQuery query) =>
        new HasMatchingSpanEvaluator(query);

    public static IEvaluator TurnAutonomy(TurnAutonomyEvaluatorOptions? options = null) =>
        new TurnAutonomyEvaluator(options);

    public static IEvaluator Bleu(params string[] references) => new BleuEvaluator(references);

    public static IEvaluator Gleu(params string[] references) => new GleuEvaluator(references);

    public static IEvaluator TextF1(string groundTruth) => new TextF1Evaluator(groundTruth);

    public static IEvaluator Rouge(string reference, RougeVariant variant = RougeVariant.RougeL) =>
        new RougeEvaluator(reference, variant);

    public static IEvaluator Meteor(params string[] references) =>
        new MeteorEvaluator(references);

    public static IEvaluator Meteor(
        MeteorEvaluatorOptions options,
        params string[] references) =>
        new MeteorEvaluator(options, references);
}
