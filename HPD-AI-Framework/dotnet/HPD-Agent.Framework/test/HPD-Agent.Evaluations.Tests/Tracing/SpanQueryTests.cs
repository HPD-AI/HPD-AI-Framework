// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using HPD.Agent.Evaluations.Tracing;

namespace HPD.Agent.Evaluations.Tests.Tracing;

/// <summary>
/// Tests for SpanQuery — the declarative query language for asserting agent behavior over TurnTrace.
///
/// SpanQuery.Matches(ToolCallSpan) — tests a single span.
/// SpanQuery.MatchesAny(TurnTrace) — tests whether any span in the trace matches.
///
/// Conditions combine with AND semantics (all must pass).
/// Or is EXCLUSIVE — it cannot be combined with other conditions.
/// </summary>
public sealed class SpanQueryTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ToolCallSpan MakeSpan(
        string name,
        string callId = "c1",
        string argsJson = "{}",
        string result = "ok",
        TimeSpan? duration = null,
        bool permissionDenied = false) =>
        new()
        {
            CallId = callId,
            Name = name,
            ToolkitName = null,
            ArgumentsJson = argsJson,
            Result = result,
            Duration = duration ?? TimeSpan.FromMilliseconds(100),
            WasPermissionDenied = permissionDenied,
        };

    private static TurnTrace MakeTrace(params ToolCallSpan[] spans)
    {
        var iteration = new IterationSpan
        {
            IterationNumber = 0,
            Usage = null,
            ToolCalls = spans.ToList(),
            AssistantText = null,
            ReasoningText = null,
            FinishReason = "stop",
            Duration = TimeSpan.FromSeconds(1),
        };

        return new TurnTrace
        {
            MessageTurnId = "msg-1",
            AgentName = "TestAgent",
            StartedAt = DateTimeOffset.UtcNow,
            Duration = TimeSpan.FromSeconds(1),
            Iterations = [iteration],
        };
    }

    // ── NameEquals ────────────────────────────────────────────────────────────

    [Fact]
    public void NameEquals_ExactMatch_ReturnsTrue()
    {
        var span = MakeSpan("SearchTool");
        var query = new SpanQuery { NameEquals = "SearchTool" };

        query.Matches(span).Should().BeTrue();
    }

    [Fact]
    public void NameEquals_Mismatch_ReturnsFalse()
    {
        var span = MakeSpan("SearchTool");
        var query = new SpanQuery { NameEquals = "FetchTool" };

        query.Matches(span).Should().BeFalse();
    }

    [Fact]
    public void NameEquals_CaseSensitive()
    {
        var span = MakeSpan("SearchTool");
        var query = new SpanQuery { NameEquals = "searchtool" };

        query.Matches(span).Should().BeFalse();
    }

    // ── NameContains ──────────────────────────────────────────────────────────

    [Fact]
    public void NameContains_SubstringPresent_ReturnsTrue()
    {
        var span = MakeSpan("SearchTool");
        var query = new SpanQuery { NameContains = "Search" };

        query.Matches(span).Should().BeTrue();
    }

    [Fact]
    public void NameContains_SubstringAbsent_ReturnsFalse()
    {
        var span = MakeSpan("SearchTool");
        var query = new SpanQuery { NameContains = "Fetch" };

        query.Matches(span).Should().BeFalse();
    }

    // ── NameMatchesRegex ──────────────────────────────────────────────────────

    [Fact]
    public void NameMatchesRegex_PatternMatches_ReturnsTrue()
    {
        var span = MakeSpan("SearchTool");
        var query = new SpanQuery { NameMatchesRegex = @"^Search.*Tool$" };

        query.Matches(span).Should().BeTrue();
    }

    [Fact]
    public void NameMatchesRegex_PatternNoMatch_ReturnsFalse()
    {
        var span = MakeSpan("ReadFile");
        var query = new SpanQuery { NameMatchesRegex = @"^Write.*" };

        query.Matches(span).Should().BeFalse();
    }

    // ── Timing conditions ─────────────────────────────────────────────────────

    [Fact]
    public void MinDuration_SpanLonger_ReturnsTrue()
    {
        var span = MakeSpan("Slow", duration: TimeSpan.FromSeconds(5));
        var query = new SpanQuery { MinDuration = TimeSpan.FromSeconds(2) };

        query.Matches(span).Should().BeTrue();
    }

    [Fact]
    public void MinDuration_SpanShorter_ReturnsFalse()
    {
        var span = MakeSpan("Fast", duration: TimeSpan.FromMilliseconds(10));
        var query = new SpanQuery { MinDuration = TimeSpan.FromSeconds(2) };

        query.Matches(span).Should().BeFalse();
    }

    [Fact]
    public void MaxDuration_SpanWithinLimit_ReturnsTrue()
    {
        var span = MakeSpan("Quick", duration: TimeSpan.FromMilliseconds(50));
        var query = new SpanQuery { MaxDuration = TimeSpan.FromSeconds(1) };

        query.Matches(span).Should().BeTrue();
    }

    [Fact]
    public void MaxDuration_SpanExceedsLimit_ReturnsFalse()
    {
        var span = MakeSpan("HungCall", duration: TimeSpan.FromSeconds(60));
        var query = new SpanQuery { MaxDuration = TimeSpan.FromSeconds(30) };

        query.Matches(span).Should().BeFalse();
    }

    // ── Combined AND conditions ───────────────────────────────────────────────

    [Fact]
    public void MultipleConditions_AllMust_Pass()
    {
        var span = MakeSpan("SearchTool", duration: TimeSpan.FromSeconds(3));
        var query = new SpanQuery
        {
            NameContains = "Search",
            MinDuration = TimeSpan.FromSeconds(1),
            MaxDuration = TimeSpan.FromSeconds(5),
        };

        query.Matches(span).Should().BeTrue();
    }

    [Fact]
    public void MultipleConditions_OneFailsAnd_ReturnsFalse()
    {
        var span = MakeSpan("SearchTool", duration: TimeSpan.FromSeconds(10));
        var query = new SpanQuery
        {
            NameContains = "Search",
            MaxDuration = TimeSpan.FromSeconds(5), // this fails — span takes 10s
        };

        query.Matches(span).Should().BeFalse();
    }

    // ── Not ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Not_MatchingSubquery_ReturnsFalse()
    {
        var span = MakeSpan("DeleteFile");
        var query = new SpanQuery
        {
            Not = new SpanQuery { NameContains = "Delete" }
        };

        query.Matches(span).Should().BeFalse();
    }

    [Fact]
    public void Not_NonMatchingSubquery_ReturnsTrue()
    {
        var span = MakeSpan("ReadFile");
        var query = new SpanQuery
        {
            Not = new SpanQuery { NameContains = "Delete" }
        };

        query.Matches(span).Should().BeTrue();
    }

    // ── And ───────────────────────────────────────────────────────────────────

    [Fact]
    public void And_AllSubqueriesMatch_ReturnsTrue()
    {
        var span = MakeSpan("SearchTool", duration: TimeSpan.FromSeconds(2));
        var query = new SpanQuery
        {
            And = [
                new SpanQuery { NameContains = "Search" },
                new SpanQuery { MinDuration = TimeSpan.FromSeconds(1) },
            ]
        };

        query.Matches(span).Should().BeTrue();
    }

    [Fact]
    public void And_OneSubqueryFails_ReturnsFalse()
    {
        var span = MakeSpan("ReadTool");
        var query = new SpanQuery
        {
            And = [
                new SpanQuery { NameContains = "Read" },
                new SpanQuery { NameContains = "Write" },  // fails
            ]
        };

        query.Matches(span).Should().BeFalse();
    }

    // ── Or ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Or_FirstBranchMatches_ReturnsTrue()
    {
        var span = MakeSpan("ReadFile");
        var query = new SpanQuery
        {
            Or = [
                new SpanQuery { NameEquals = "ReadFile" },
                new SpanQuery { NameEquals = "WriteFile" },
            ]
        };

        query.Matches(span).Should().BeTrue();
    }

    [Fact]
    public void Or_SecondBranchMatches_ReturnsTrue()
    {
        var span = MakeSpan("WriteFile");
        var query = new SpanQuery
        {
            Or = [
                new SpanQuery { NameEquals = "ReadFile" },
                new SpanQuery { NameEquals = "WriteFile" },
            ]
        };

        query.Matches(span).Should().BeTrue();
    }

    [Fact]
    public void Or_NoBranchMatches_ReturnsFalse()
    {
        var span = MakeSpan("DeleteFile");
        var query = new SpanQuery
        {
            Or = [
                new SpanQuery { NameEquals = "ReadFile" },
                new SpanQuery { NameEquals = "WriteFile" },
            ]
        };

        query.Matches(span).Should().BeFalse();
    }

    [Fact]
    public void Or_CombinedWithOtherCondition_Throws()
    {
        var span = MakeSpan("SomeTool");
        var query = new SpanQuery
        {
            Or = [new SpanQuery { NameEquals = "SomeTool" }],
            NameContains = "Some",  // EXCLUSIVE violation
        };

        var act = () => query.Matches(span);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Or*exclusive*");
    }

    // ── EmptyQuery (no conditions) ────────────────────────────────────────────

    [Fact]
    public void EmptyQuery_MatchesAnySpan()
    {
        var span = MakeSpan("AnythingAtAll");
        var query = new SpanQuery();  // all null

        query.Matches(span).Should().BeTrue();
    }

    // ── MatchesAny (TurnTrace integration) ───────────────────────────────────

    [Fact]
    public void MatchesAny_SpanPresentInTrace_ReturnsTrue()
    {
        var trace = MakeTrace(
            MakeSpan("Tool1"),
            MakeSpan("SearchTool"),
            MakeSpan("Tool3"));

        var query = new SpanQuery { NameEquals = "SearchTool" };

        query.MatchesAny(trace).Should().BeTrue();
    }

    [Fact]
    public void MatchesAny_SpanAbsentFromTrace_ReturnsFalse()
    {
        var trace = MakeTrace(MakeSpan("Tool1"), MakeSpan("Tool2"));

        var query = new SpanQuery { NameEquals = "Missing" };

        query.MatchesAny(trace).Should().BeFalse();
    }

    [Fact]
    public void MatchesAny_EmptyTrace_ReturnsFalse()
    {
        var trace = MakeTrace();  // no spans

        var query = new SpanQuery { NameContains = "Anything" };

        query.MatchesAny(trace).Should().BeFalse();
    }
}
