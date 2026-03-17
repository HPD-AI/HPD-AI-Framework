// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent;
using HPD.Agent.Evaluations.Integration;
using HPD.Agent.Evaluations.Storage;

namespace HPD.Agent.Evaluations.Tests.Integration;

/// <summary>
/// Tests for RetroactiveScorer — offline scoring of saved branches.
///
/// Key behaviors:
/// 1. ScoreBranchAsync with missing branch → ArgumentException.
/// 2. ScoreBranchAsync with one turn → one ReportCase.
/// 3. ScoreBranchAsync with N turns → N ReportCases.
/// 4. Evaluator result propagates into ReportCase.EvaluationResult.
/// 5. With IScoreStore → scores are persisted.
/// 6. ForceRescore=false (default) → already-scored turns skipped.
/// 7. ForceRescore=true → turns rescored regardless.
/// 8. CompareBranchesAsync → BranchComparisonReport with two sub-reports.
/// 9. TournamentAsync → entries ranked by score descending.
/// </summary>
public sealed class RetroactiveScorerTests
{
    // ── ScoreBranchAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ScoreBranch_MissingBranch_Throws()
    {
        var store = new FakeSessionStore();

        var act = async () => await RetroactiveScorer.ScoreBranchAsync(
            store, "sess-1", "nonexistent",
            [new StubDeterministicEvaluator("Score")]);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*nonexistent*");
    }

    [Fact]
    public async Task ScoreBranch_OneTurn_OneReportCase()
    {
        var store = new FakeSessionStore();
        var branch = new BranchBuilder("sess-1", "branch-1")
            .AddUserMessage("What is 2+2?")
            .AddAssistantMessage("4")
            .Build();
        store.AddBranch("sess-1", branch);

        var report = await RetroactiveScorer.ScoreBranchAsync(
            store, "sess-1", "branch-1",
            [new StubDeterministicEvaluator("Score")]);

        report.Cases.Should().ContainSingle("one turn in branch → one report case");
    }

    [Fact]
    public async Task ScoreBranch_TwoTurns_TwoReportCases()
    {
        var store = new FakeSessionStore();
        var branch = new BranchBuilder("sess-1", "branch-1")
            .AddUserMessage("Turn 1")
            .AddAssistantMessage("Response 1")
            .AddUserMessage("Turn 2")
            .AddAssistantMessage("Response 2")
            .Build();
        store.AddBranch("sess-1", branch);

        var report = await RetroactiveScorer.ScoreBranchAsync(
            store, "sess-1", "branch-1",
            [new StubDeterministicEvaluator("Score")]);

        report.Cases.Should().HaveCount(2);
    }

    [Fact]
    public async Task ScoreBranch_EvaluatorResult_InReportCase()
    {
        var store = new FakeSessionStore();
        var branch = new BranchBuilder("sess-1", "branch-1")
            .AddUserMessage("Q")
            .AddAssistantMessage("A")
            .Build();
        store.AddBranch("sess-1", branch);

        var report = await RetroactiveScorer.ScoreBranchAsync(
            store, "sess-1", "branch-1",
            [new StubDeterministicEvaluator("Score", pass: true)]);

        var @case = report.Cases.Single();
        @case.EvaluationResult.Metrics.Should().ContainKey("Score");
        var metric = @case.EvaluationResult.Metrics["Score"] as BooleanMetric;
        metric!.Value.Should().BeTrue();
    }

    [Fact]
    public async Task ScoreBranch_EmptyBranch_ReturnsEmptyReport()
    {
        var store = new FakeSessionStore();
        var branch = new Branch("sess-1", "b1"); // empty messages by default
        store.AddBranch("sess-1", branch);

        var report = await RetroactiveScorer.ScoreBranchAsync(
            store, "sess-1", "b1",
            [new StubDeterministicEvaluator("Score")]);

        report.Cases.Should().BeEmpty("empty branch has no turns to score");
    }

    // ── IScoreStore integration ────────────────────────────────────────────────

    [Fact]
    public async Task ScoreBranch_WithScoreStore_WritesRecord()
    {
        var sessionStore = new FakeSessionStore();
        var scoreStore = new InMemoryScoreStore();
        var branch = new BranchBuilder("sess-1", "branch-1")
            .AddUserMessage("Q")
            .AddAssistantMessage("A")
            .Build();
        sessionStore.AddBranch("sess-1", branch);

        await RetroactiveScorer.ScoreBranchAsync(
            sessionStore, "sess-1", "branch-1",
            [new StubDeterministicEvaluator("Score")],
            scoreStore: scoreStore);

        var records = await scoreStore.GetScoresAsync(sessionId: "sess-1").ToListAsync();
        records.Should().ContainSingle("one turn scored → one record written");
        records[0].Source.Should().Be(EvaluationSource.Retroactive);
    }

    [Fact]
    public async Task ScoreBranch_ForceRescore_False_SkipsAlreadyScoredTurns()
    {
        var sessionStore = new FakeSessionStore();
        var scoreStore = new InMemoryScoreStore();
        var branch = new BranchBuilder("sess-1", "branch-1")
            .AddUserMessage("Q")
            .AddAssistantMessage("A")
            .Build();
        sessionStore.AddBranch("sess-1", branch);

        // Score once
        await RetroactiveScorer.ScoreBranchAsync(
            sessionStore, "sess-1", "branch-1",
            [new StubDeterministicEvaluator("Score")],
            scoreStore: scoreStore,
            options: new RetroactiveScorerOptions { ForceRescore = false });

        // Score again with ForceRescore=false → should skip the already-scored turn
        await RetroactiveScorer.ScoreBranchAsync(
            sessionStore, "sess-1", "branch-1",
            [new StubDeterministicEvaluator("Score")],
            scoreStore: scoreStore,
            options: new RetroactiveScorerOptions { ForceRescore = false });

        var records = await scoreStore.GetScoresAsync(sessionId: "sess-1").ToListAsync();
        records.Should().ContainSingle("second pass should not duplicate the record");
    }

    [Fact]
    public async Task ScoreBranch_ForceRescore_True_RescoresTurns()
    {
        var sessionStore = new FakeSessionStore();
        var scoreStore = new InMemoryScoreStore();
        var branch = new BranchBuilder("sess-1", "branch-1")
            .AddUserMessage("Q")
            .AddAssistantMessage("A")
            .Build();
        sessionStore.AddBranch("sess-1", branch);

        await RetroactiveScorer.ScoreBranchAsync(
            sessionStore, "sess-1", "branch-1",
            [new StubDeterministicEvaluator("Score")],
            scoreStore: scoreStore);

        // ForceRescore=true → should score again regardless
        await RetroactiveScorer.ScoreBranchAsync(
            sessionStore, "sess-1", "branch-1",
            [new StubDeterministicEvaluator("Score")],
            scoreStore: scoreStore,
            options: new RetroactiveScorerOptions { ForceRescore = true });

        var records = await scoreStore.GetScoresAsync(sessionId: "sess-1").ToListAsync();
        records.Should().HaveCount(2, "ForceRescore=true must produce a new record even when one exists");
    }

    // ── CompareBranchesAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task CompareBranches_ReturnsTwoSubReports()
    {
        var sessionStore = new FakeSessionStore();
        var b1 = new BranchBuilder("sess-1", "b1").AddUserMessage("Q").AddAssistantMessage("A1").Build();
        var b2 = new BranchBuilder("sess-1", "b2").AddUserMessage("Q").AddAssistantMessage("A2").Build();
        sessionStore.AddBranch("sess-1", b1);
        sessionStore.AddBranch("sess-1", b2);

        var comparison = await RetroactiveScorer.CompareBranchesAsync(
            sessionStore, "sess-1", "b1", "b2",
            [new StubDeterministicEvaluator("Score")]);

        comparison.Branch1Report.Cases.Should().ContainSingle();
        comparison.Branch2Report.Cases.Should().ContainSingle();
    }

    [Fact]
    public async Task CompareBranches_MissingBranch_Throws()
    {
        var sessionStore = new FakeSessionStore();
        var b1 = new BranchBuilder("sess-1", "b1").AddUserMessage("Q").AddAssistantMessage("A").Build();
        sessionStore.AddBranch("sess-1", b1);

        var act = async () => await RetroactiveScorer.CompareBranchesAsync(
            sessionStore, "sess-1", "b1", "missing",
            [new StubDeterministicEvaluator("Score")]);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── TournamentAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task Tournament_RanksDescendingByScore()
    {
        var sessionStore = new FakeSessionStore();

        // b-pass: evaluator returns pass (score=1), b-fail: evaluator returns fail (score=0)
        var bPass = new BranchBuilder("sess-1", "b-pass").AddUserMessage("Q").AddAssistantMessage("A").Build();
        var bFail = new BranchBuilder("sess-1", "b-fail").AddUserMessage("Q").AddAssistantMessage("A").Build();
        sessionStore.AddBranch("sess-1", bPass);
        sessionStore.AddBranch("sess-1", bFail);

        // Use NumericMetric evaluator: pass branch gets 0.8, fail branch gets 0.0
        var tournament = await RetroactiveScorer.TournamentAsync(
            sessionStore, "sess-1", ["b-pass", "b-fail"],
            new NumericStubEvaluator("Score", scoreForPass: 0.8));

        tournament.Rankings.Should().HaveCount(2);
        // Ranked descending by score: b-pass first
        tournament.Rankings[0].BranchId.Should().Be("b-pass");
        tournament.Rankings[1].BranchId.Should().Be("b-fail");
        tournament.Rankings[0].Rank.Should().Be(1);
        tournament.Rankings[1].Rank.Should().Be(2);
    }

    [Fact]
    public async Task Tournament_SingleBranch_SingleEntry()
    {
        var sessionStore = new FakeSessionStore();
        var branch = new BranchBuilder("sess-1", "only").AddUserMessage("Q").AddAssistantMessage("A").Build();
        sessionStore.AddBranch("sess-1", branch);

        var result = await RetroactiveScorer.TournamentAsync(
            sessionStore, "sess-1", ["only"],
            new StubDeterministicEvaluator("Score"));

        result.Rankings.Should().ContainSingle();
        result.Rankings[0].Rank.Should().Be(1);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Returns a fixed NumericMetric score so we can rank branches.</summary>
    private sealed class NumericStubEvaluator(string metricName, double scoreForPass) : IEvaluator
    {
        public IReadOnlyCollection<string> EvaluationMetricNames => [metricName];

        public ValueTask<EvaluationResult> EvaluateAsync(
            IEnumerable<ChatMessage> messages,
            ChatResponse modelResponse,
            ChatConfiguration? chatConfiguration = null,
            IEnumerable<EvaluationContext>? additionalContext = null,
            CancellationToken cancellationToken = default)
        {
            var metric = new NumericMetric(metricName) { Value = scoreForPass };
            return ValueTask.FromResult(new EvaluationResult(metric));
        }
    }
}

file static class AsyncEnumerableExt3
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source) list.Add(item);
        return list;
    }
}
