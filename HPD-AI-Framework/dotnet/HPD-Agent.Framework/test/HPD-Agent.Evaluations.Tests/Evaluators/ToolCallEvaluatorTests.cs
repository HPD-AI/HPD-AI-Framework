// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Evaluations.Evaluators.Deterministic;
using HPD.Agent.Evaluations.Tests.Infrastructure;

namespace HPD.Agent.Evaluations.Tests.Evaluators;

/// <summary>
/// Tests for tool-call deterministic evaluators:
/// - ToolWasCalledEvaluator
/// - ToolCallCountEvaluator
/// - ToolArgumentMatchesEvaluator
/// - NoToolsCalledEvaluator
/// - ToolCallOrderEvaluator
///
/// All evaluators require TurnEvaluationContextWrapper in additionalContext.
/// Missing context → error diagnostic, null metric value.
/// </summary>
public sealed class ToolCallEvaluatorTests
{
    private static readonly ChatResponse EmptyResponse =
        new([new ChatMessage(ChatRole.Assistant, "ok")]);

    // ── ToolWasCalledEvaluator ────────────────────────────────────────────────

    [Fact]
    public async Task ToolWasCalled_ToolPresent_ReturnsTrue()
    {
        var ctx = new TestContextBuilder()
            .WithToolCall("SearchTool")
            .BuildAsAdditionalContext();

        var result = await new ToolWasCalledEvaluator("SearchTool")
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Tool Was Called", true)
            .ShouldBeMarkedAsBuiltIn();
    }

    [Fact]
    public async Task ToolWasCalled_ToolAbsent_ReturnsFalse()
    {
        var ctx = new TestContextBuilder()
            .WithToolCall("OtherTool")
            .BuildAsAdditionalContext();

        var result = await new ToolWasCalledEvaluator("SearchTool")
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Tool Was Called", false);
    }

    [Fact]
    public async Task ToolWasCalled_NoToolCalls_ReturnsFalse()
    {
        var ctx = new TestContextBuilder().BuildAsAdditionalContext(); // no tool calls

        var result = await new ToolWasCalledEvaluator("SearchTool")
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Tool Was Called", false);
    }

    [Fact]
    public async Task ToolWasCalled_NoContext_ReturnsErrorDiagnostic()
    {
        // No TurnEvaluationContextWrapper in additionalContext
        var result = await new ToolWasCalledEvaluator("SearchTool")
            .EvaluateAsync([], EmptyResponse, additionalContext: null);

        result.ShouldHaveErrorDiagnostic();
        // Metric value should be null (not set on error path)
        var metric = result.Metrics["Tool Was Called"] as BooleanMetric;
        metric.Should().NotBeNull();
        metric!.Value.Should().BeNull();
    }

    [Fact]
    public async Task ToolWasCalled_MultipleTools_MatchesCorrectOne()
    {
        var ctx = new TestContextBuilder()
            .WithToolCall("Tool1", callId: "c1")
            .WithToolCall("Tool2", callId: "c2")
            .WithToolCall("SearchTool", callId: "c3")
            .BuildAsAdditionalContext();

        var result = await new ToolWasCalledEvaluator("SearchTool")
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Tool Was Called", true);
    }

    // ── ToolCallCountEvaluator ────────────────────────────────────────────────

    [Fact]
    public async Task ToolCallCount_ExactMatch_ReturnsTrue()
    {
        var ctx = new TestContextBuilder()
            .WithToolCall("Fetch", callId: "c1")
            .WithToolCall("Fetch", callId: "c2")
            .WithToolCall("Fetch", callId: "c3")
            .BuildAsAdditionalContext();

        var result = await new ToolCallCountEvaluator("Fetch", expectedCount: 3)
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Tool Call Count", true);
    }

    [Fact]
    public async Task ToolCallCount_Mismatch_ReturnsFalse()
    {
        var ctx = new TestContextBuilder()
            .WithToolCall("Fetch", callId: "c1")
            .BuildAsAdditionalContext();

        var result = await new ToolCallCountEvaluator("Fetch", expectedCount: 3)
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Tool Call Count", false);
        var metric = result.Metrics["Tool Call Count"] as BooleanMetric;
        metric!.Reason.Should().Contain("1").And.Contain("expected: 3");
    }

    [Fact]
    public async Task ToolCallCount_DifferentToolIgnored_CountsCorrectTool()
    {
        var ctx = new TestContextBuilder()
            .WithToolCall("Other", callId: "c1")
            .WithToolCall("Fetch", callId: "c2")
            .WithToolCall("Other", callId: "c3")
            .BuildAsAdditionalContext();

        // Only 1 "Fetch" call, even though 3 total tool calls
        var result = await new ToolCallCountEvaluator("Fetch", expectedCount: 1)
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Tool Call Count", true);
    }

    // ── ToolArgumentMatchesEvaluator ──────────────────────────────────────────

    [Fact]
    public async Task ToolArgumentMatches_CorrectArgValue_ReturnsTrue()
    {
        var ctx = new TestContextBuilder()
            .WithToolCall("Search", argsJson: """{"query":"capital of France","limit":5}""")
            .BuildAsAdditionalContext();

        var result = await new ToolArgumentMatchesEvaluator("Search", "query", "capital of France")
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Tool Argument Matches", true);
    }

    [Fact]
    public async Task ToolArgumentMatches_WrongArgValue_ReturnsFalse()
    {
        var ctx = new TestContextBuilder()
            .WithToolCall("Search", argsJson: """{"query":"something else"}""")
            .BuildAsAdditionalContext();

        var result = await new ToolArgumentMatchesEvaluator("Search", "query", "capital of France")
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Tool Argument Matches", false);
    }

    [Fact]
    public async Task ToolArgumentMatches_ArgKeyMissing_ReturnsFalse()
    {
        var ctx = new TestContextBuilder()
            .WithToolCall("Search", argsJson: """{"other_key":"value"}""")
            .BuildAsAdditionalContext();

        var result = await new ToolArgumentMatchesEvaluator("Search", "query", "paris")
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Tool Argument Matches", false);
    }

    [Fact]
    public async Task ToolArgumentMatches_MalformedJson_SkipsAndReturnsFalse()
    {
        var ctx = new TestContextBuilder()
            .WithToolCall("Search", argsJson: "not-json{{")
            .BuildAsAdditionalContext();

        // Should not throw on malformed JSON — skips and returns false
        var result = await new ToolArgumentMatchesEvaluator("Search", "query", "paris")
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Tool Argument Matches", false);
    }

    [Fact]
    public async Task ToolArgumentMatches_MatchesAnyCallWithThatTool()
    {
        // Two calls to "Search" — second one has the matching arg
        var ctx = new TestContextBuilder()
            .WithToolCall("Search", callId: "c1", argsJson: """{"query":"wrong"}""")
            .WithToolCall("Search", callId: "c2", argsJson: """{"query":"paris"}""")
            .BuildAsAdditionalContext();

        var result = await new ToolArgumentMatchesEvaluator("Search", "query", "paris")
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Tool Argument Matches", true);
    }

    // ── NoToolsCalledEvaluator ────────────────────────────────────────────────

    [Fact]
    public async Task NoToolsCalled_EmptyToolList_ReturnsTrue()
    {
        var ctx = new TestContextBuilder().BuildAsAdditionalContext();

        var result = await new NoToolsCalledEvaluator()
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("No Tools Called", true);
    }

    [Fact]
    public async Task NoToolsCalled_HasToolCalls_ReturnsFalse()
    {
        var ctx = new TestContextBuilder()
            .WithToolCall("SomeTool")
            .BuildAsAdditionalContext();

        var result = await new NoToolsCalledEvaluator()
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("No Tools Called", false);
    }

    // ── ToolCallOrderEvaluator ────────────────────────────────────────────────

    [Fact]
    public async Task ToolCallOrder_ExactOrder_ReturnsTrue()
    {
        var ctx = new TestContextBuilder()
            .WithToolCall("Search", callId: "c1")
            .WithToolCall("Fetch", callId: "c2")
            .WithToolCall("Summarize", callId: "c3")
            .BuildAsAdditionalContext();

        var result = await new ToolCallOrderEvaluator(["Search", "Fetch", "Summarize"])
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Tool Call Order", true);
    }

    [Fact]
    public async Task ToolCallOrder_SubsequenceMatch_ReturnsTrue()
    {
        // Expected is a subsequence of actual — should pass (interspersed extra calls are ok)
        var ctx = new TestContextBuilder()
            .WithToolCall("Search", callId: "c1")
            .WithToolCall("Other", callId: "c2")   // extra tool — not in expected
            .WithToolCall("Summarize", callId: "c3")
            .BuildAsAdditionalContext();

        var result = await new ToolCallOrderEvaluator(["Search", "Summarize"])
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Tool Call Order", true);
    }

    [Fact]
    public async Task ToolCallOrder_WrongOrder_ReturnsFalse()
    {
        var ctx = new TestContextBuilder()
            .WithToolCall("Summarize", callId: "c1")
            .WithToolCall("Search", callId: "c2")
            .BuildAsAdditionalContext();

        // Expected: Search → Summarize (but actual: Summarize → Search)
        var result = await new ToolCallOrderEvaluator(["Search", "Summarize"])
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Tool Call Order", false);
    }

    [Fact]
    public async Task ToolCallOrder_MissingToolInActual_ReturnsFalse()
    {
        var ctx = new TestContextBuilder()
            .WithToolCall("Search", callId: "c1")
            .BuildAsAdditionalContext();

        var result = await new ToolCallOrderEvaluator(["Search", "Fetch"])
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Tool Call Order", false);
    }
}
