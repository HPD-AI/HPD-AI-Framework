// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: Apache-2.0

using HPD.Agent.Evaluations.Evaluators.Deterministic;
using Microsoft.Extensions.AI.Evaluation;

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
}
