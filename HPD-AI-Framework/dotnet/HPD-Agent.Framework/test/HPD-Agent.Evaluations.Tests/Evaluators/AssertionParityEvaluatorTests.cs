// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using HPD.Agent.Evaluations.Evaluators.Composite;
using HPD.Agent.Evaluations.Evaluators.Deterministic;
using HPD.Agent.Evaluations.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations.Tests.Evaluators;

public sealed class AssertionParityEvaluatorTests
{
    private static ChatResponse Respond(string text) =>
        new([new ChatMessage(ChatRole.Assistant, text)]);

    [Fact]
    public async Task ContainsAny_WhenOneValuePresent_ReturnsTrue()
    {
        var result = await new ContainsAnyEvaluator("London", "Paris")
            .EvaluateAsync([], Respond("Paris is the capital."));

        result.ShouldHaveBooleanMetric("Contains Any", true)
            .ShouldBeMarkedAsBuiltIn();
    }

    [Fact]
    public async Task ContainsAll_WhenOneValueMissing_ReturnsFalse()
    {
        var result = await new ContainsAllEvaluator("Paris", "France", "Berlin")
            .EvaluateAsync([], Respond("Paris is in France."));

        result.ShouldHaveBooleanMetric("Contains All", false);
    }

    [Fact]
    public async Task CaseInsensitiveContains_IgnoresCase()
    {
        var result = await new CaseInsensitiveContainsEvaluator("PARIS")
            .EvaluateAsync([], Respond("paris"));

        result.ShouldHaveBooleanMetric("Case-Insensitive Contains", true);
    }

    [Fact]
    public async Task StartsWith_MatchesPrefix()
    {
        var result = await new StartsWithEvaluator("Result:")
            .EvaluateAsync([], Respond("Result: ok"));

        result.ShouldHaveBooleanMetric("Starts With", true);
    }

    [Fact]
    public async Task WordCount_WithinMinMax_ReturnsTrueAndMetadata()
    {
        var result = await new WordCountEvaluator(min: 3, max: 5)
            .EvaluateAsync([], Respond("Paris is very nice"));

        var metric = result.ShouldHaveBooleanMetric("Word Count", true);
        metric.Metadata.Should().ContainKey("word-count");
    }

    [Fact]
    public async Task Levenshtein_IdenticalStrings_ReturnsOne()
    {
        var result = await new LevenshteinEvaluator("Paris")
            .EvaluateAsync([], Respond("Paris"));

        result.ShouldHaveNumericMetricInRange("Levenshtein Similarity", 1.0, 1.0);
    }

    [Fact]
    public async Task Refusal_CommonPhrase_ReturnsTrue()
    {
        var result = await new RefusalEvaluator()
            .EvaluateAsync([], Respond("I cannot help with that request."));

        result.ShouldHaveBooleanMetric("Refusal", true);
    }

    [Fact]
    public async Task JsonValidity_ValidJson_ReturnsTrue()
    {
        var result = await new JsonValidityEvaluator()
            .EvaluateAsync([], Respond("""{"answer":"Paris"}"""));

        result.ShouldHaveBooleanMetric("JSON Validity", true);
    }

    [Fact]
    public async Task JsonValidity_InvalidJson_ReturnsFalse()
    {
        var result = await new JsonValidityEvaluator()
            .EvaluateAsync([], Respond("""{"answer":"""));

        result.ShouldHaveBooleanMetric("JSON Validity", false);
    }

    [Fact]
    public async Task XmlValidity_ValidXml_ReturnsTrue()
    {
        var result = await new XmlValidityEvaluator()
            .EvaluateAsync([], Respond("<answer>Paris</answer>"));

        result.ShouldHaveBooleanMetric("XML Validity", true);
    }

    [Fact]
    public async Task HtmlShape_RequiredTagPresent_ReturnsTrue()
    {
        var result = await new HtmlShapeEvaluator("main")
            .EvaluateAsync([], Respond("<main><h1>Paris</h1></main>"));

        result.ShouldHaveBooleanMetric("HTML Shape", true);
    }

    [Fact]
    public async Task SqlShape_SelectWithBalancedParens_ReturnsTrue()
    {
        var result = await new SqlShapeEvaluator()
            .EvaluateAsync([], Respond("SELECT name FROM cities WHERE country = 'France'"));

        result.ShouldHaveBooleanMetric("SQL Shape", true);
    }

    [Fact]
    public async Task SqlShape_Prose_ReturnsFalse()
    {
        var result = await new SqlShapeEvaluator()
            .EvaluateAsync([], Respond("You should query the cities table."));

        result.ShouldHaveBooleanMetric("SQL Shape", false);
    }

    [Fact]
    public async Task Latency_ReportsDurationSeconds()
    {
        var ctx = new TestContextBuilder()
            .WithDuration(TimeSpan.FromMilliseconds(250))
            .BuildAsAdditionalContext();

        var result = await new LatencyEvaluator()
            .EvaluateAsync([], Respond("ok"), additionalContext: ctx);

        result.ShouldHaveNumericMetricInRange("Latency", 0.25, 0.25);
    }

    [Fact]
    public async Task MaxCost_UsesEvalContextMetric()
    {
        var ctx = new TestContextBuilder()
            .WithMetric("cost_usd", 0.001)
            .BuildAsAdditionalContext();

        var result = await new MaxCostEvaluator(0.01)
            .EvaluateAsync([], Respond("ok"), additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Max Cost", true);
    }

    [Fact]
    public async Task MaxCost_MissingCostMetric_ReturnsWarningDiagnostic()
    {
        var ctx = new TestContextBuilder().BuildAsAdditionalContext();

        var result = await new MaxCostEvaluator(0.01)
            .EvaluateAsync([], Respond("ok"), additionalContext: ctx);

        var metric = result.Metrics["Max Cost"];
        metric.Diagnostics.Should().Contain(d => d.Severity == EvaluationDiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task ToolCallF1_PerfectMatch_ReturnsOne()
    {
        var ctx = new TestContextBuilder()
            .WithToolCall("Search", callId: "1")
            .WithToolCall("Fetch", callId: "2")
            .BuildAsAdditionalContext();

        var result = await new ToolCallF1Evaluator("Search", "Fetch")
            .EvaluateAsync([], Respond("ok"), additionalContext: ctx);

        result.ShouldHaveNumericMetricInRange("Tool Call F1", 1.0, 1.0);
    }

    [Fact]
    public async Task ToolCallF1_PartialMatch_ReturnsPartialScore()
    {
        var ctx = new TestContextBuilder()
            .WithToolCall("Search", callId: "1")
            .WithToolCall("Other", callId: "2")
            .BuildAsAdditionalContext();

        var result = await new ToolCallF1Evaluator("Search", "Fetch")
            .EvaluateAsync([], Respond("ok"), additionalContext: ctx);

        result.ShouldHaveNumericMetricInRange("Tool Call F1", 0.49, 0.51);
    }

    [Fact]
    public async Task NotEvaluator_InvertsInnerBooleanMetric()
    {
        var result = await new NotEvaluator(new OutputContainsEvaluator("secret"))
            .EvaluateAsync([], Respond("public answer"));

        result.ShouldHaveBooleanMetric("Not (Output Contains)", true);
    }
}
