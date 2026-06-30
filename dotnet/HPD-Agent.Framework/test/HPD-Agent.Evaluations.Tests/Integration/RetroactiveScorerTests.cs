// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent;
using HPD.Agent.Evaluations.Evaluators.LlmJudge;
using HPD.Agent.Evaluations.Integration;
using HPD.Agent.Evaluations.Storage;
using HPD.Agent.Evaluations.Tests.Infrastructure;

namespace HPD.Agent.Evaluations.Tests.Integration;

/// <summary>
/// Tests for RetroactiveScorer — offline scoring of saved threads.
///
/// Key behaviors:
/// 1. ScoreThreadAsync with missing thread → ArgumentException.
/// 2. ScoreThreadAsync with one turn → one ReportCase.
/// 3. ScoreThreadAsync with N turns → N ReportCases.
/// 4. Evaluator result propagates into ReportCase.EvaluationResult.
/// 5. With IScoreStore → scores are persisted.
/// 6. ForceRescore=false (default) → already-scored turns skipped.
/// 7. ForceRescore=true → turns rescored regardless.
/// 8. CompareThreadsAsync → ThreadComparisonReport with two sub-reports.
/// 9. TournamentAsync → entries ranked by score descending.
/// </summary>
public sealed class RetroactiveScorerTests
{
    // ── ScoreThreadAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ScoreThread_MissingThread_Throws()
    {
        var store = new FakeSessionStore();

        var act = async () => await RetroactiveScorer.ScoreThreadAsync(
            store, "sess-1", "nonexistent",
            [new StubDeterministicEvaluator("Score")]);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*nonexistent*");
    }

    [Fact]
    public async Task ScoreThread_OneTurn_OneReportCase()
    {
        var store = new FakeSessionStore();
        var thread = new ThreadBuilder("sess-1", "thread-1")
            .AddUserMessage("What is 2+2?")
            .AddAssistantMessage("4")
            .Build();
        store.AddThread("sess-1", thread);

        var report = await RetroactiveScorer.ScoreThreadAsync(
            store, "sess-1", "thread-1",
            [new StubDeterministicEvaluator("Score")]);

        report.Cases.Should().ContainSingle("one turn in thread → one report case");
    }

    [Fact]
    public async Task ScoreThread_TwoTurns_TwoReportCases()
    {
        var store = new FakeSessionStore();
        var thread = new ThreadBuilder("sess-1", "thread-1")
            .AddUserMessage("Turn 1")
            .AddAssistantMessage("Response 1")
            .AddUserMessage("Turn 2")
            .AddAssistantMessage("Response 2")
            .Build();
        store.AddThread("sess-1", thread);

        var report = await RetroactiveScorer.ScoreThreadAsync(
            store, "sess-1", "thread-1",
            [new StubDeterministicEvaluator("Score")]);

        report.Cases.Should().HaveCount(2);
    }

    [Fact]
    public async Task ScoreThread_EvaluatorResult_InReportCase()
    {
        var store = new FakeSessionStore();
        var thread = new ThreadBuilder("sess-1", "thread-1")
            .AddUserMessage("Q")
            .AddAssistantMessage("A")
            .Build();
        store.AddThread("sess-1", thread);

        var report = await RetroactiveScorer.ScoreThreadAsync(
            store, "sess-1", "thread-1",
            [new StubDeterministicEvaluator("Score", pass: true)]);

        var @case = report.Cases.Single();
        @case.EvaluationResult.Metrics.Should().ContainKey("Score");
        var metric = @case.EvaluationResult.Metrics["Score"] as BooleanMetric;
        metric!.Value.Should().BeTrue();
    }

    [Fact]
    public async Task ScoreThread_EmptyThread_ReturnsEmptyReport()
    {
        var store = new FakeSessionStore();
        var thread = new Thread("sess-1", "b1"); // empty messages by default
        store.AddThread("sess-1", thread);

        var report = await RetroactiveScorer.ScoreThreadAsync(
            store, "sess-1", "b1",
            [new StubDeterministicEvaluator("Score")]);

        report.Cases.Should().BeEmpty("empty thread has no turns to score");
    }

    // ── IScoreStore integration ────────────────────────────────────────────────

    [Fact]
    public async Task ScoreThread_WithScoreStore_WritesRecord()
    {
        var sessionStore = new FakeSessionStore();
        var scoreStore = new InMemoryScoreStore();
        var thread = new ThreadBuilder("sess-1", "thread-1")
            .AddUserMessage("Q")
            .AddAssistantMessage("A")
            .Build();
        sessionStore.AddThread("sess-1", thread);

        await RetroactiveScorer.ScoreThreadAsync(
            sessionStore, "sess-1", "thread-1",
            [new StubDeterministicEvaluator("Score")],
            scoreStore: scoreStore);

        var records = await scoreStore.GetScoresAsync(sessionId: "sess-1").ToListAsync();
        records.Should().ContainSingle("one turn scored → one record written");
        records[0].Source.Should().Be(EvaluationSource.Retroactive);
    }

    [Fact]
    public async Task ScoreThread_WithJudgeEvaluator_WritesJudgeCallsToScoreRecord()
    {
        var sessionStore = new FakeSessionStore();
        var scoreStore = new InMemoryScoreStore();
        var judge = new FakeJudgeChatClient();
        judge.EnqueueResponse("<S0>ok</S0><S1>retro captured</S1><S2>true</S2>");
        var thread = new ThreadBuilder("sess-1", "thread-1")
            .AddUserMessage("Q")
            .AddAssistantMessage("A")
            .Build();
        sessionStore.AddThread("sess-1", thread);

        await RetroactiveScorer.ScoreThreadAsync(
            sessionStore,
            "sess-1",
            "thread-1",
            [new AspectCriticEvaluator("passes")],
            chatConfiguration: new ChatConfiguration(judge),
            scoreStore: scoreStore);

        var records = await scoreStore.GetScoresAsync(sessionId: "sess-1").ToListAsync();
        var record = records.Should().ContainSingle().Which;
        var call = record.JudgeCalls.Should().ContainSingle().Which;
        call.EvaluatorName.Should().Be(nameof(AspectCriticEvaluator));
        call.Succeeded.Should().BeTrue();
        call.Response!.Text.Should().Contain("<S2>true</S2>");
    }

    [Fact]
    public async Task ScoreThread_ForceRescore_False_SkipsAlreadyScoredTurns()
    {
        var sessionStore = new FakeSessionStore();
        var scoreStore = new InMemoryScoreStore();
        var thread = new ThreadBuilder("sess-1", "thread-1")
            .AddUserMessage("Q")
            .AddAssistantMessage("A")
            .Build();
        sessionStore.AddThread("sess-1", thread);

        // Score once
        await RetroactiveScorer.ScoreThreadAsync(
            sessionStore, "sess-1", "thread-1",
            [new StubDeterministicEvaluator("Score")],
            scoreStore: scoreStore,
            options: new RetroactiveScorerOptions { ForceRescore = false });

        // Score again with ForceRescore=false → should skip the already-scored turn
        await RetroactiveScorer.ScoreThreadAsync(
            sessionStore, "sess-1", "thread-1",
            [new StubDeterministicEvaluator("Score")],
            scoreStore: scoreStore,
            options: new RetroactiveScorerOptions { ForceRescore = false });

        var records = await scoreStore.GetScoresAsync(sessionId: "sess-1").ToListAsync();
        records.Should().ContainSingle("second pass should not duplicate the record");
    }

    [Fact]
    public async Task ScoreThread_ForceRescore_True_RescoresTurns()
    {
        var sessionStore = new FakeSessionStore();
        var scoreStore = new InMemoryScoreStore();
        var thread = new ThreadBuilder("sess-1", "thread-1")
            .AddUserMessage("Q")
            .AddAssistantMessage("A")
            .Build();
        sessionStore.AddThread("sess-1", thread);

        await RetroactiveScorer.ScoreThreadAsync(
            sessionStore, "sess-1", "thread-1",
            [new StubDeterministicEvaluator("Score")],
            scoreStore: scoreStore);

        // ForceRescore=true → should score again regardless
        await RetroactiveScorer.ScoreThreadAsync(
            sessionStore, "sess-1", "thread-1",
            [new StubDeterministicEvaluator("Score")],
            scoreStore: scoreStore,
            options: new RetroactiveScorerOptions { ForceRescore = true });

        var records = await scoreStore.GetScoresAsync(sessionId: "sess-1").ToListAsync();
        records.Should().HaveCount(2, "ForceRescore=true must produce a new record even when one exists");
    }

    // ── CompareThreadsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task CompareThreads_ReturnsTwoSubReports()
    {
        var sessionStore = new FakeSessionStore();
        var b1 = new ThreadBuilder("sess-1", "b1").AddUserMessage("Q").AddAssistantMessage("A1").Build();
        var b2 = new ThreadBuilder("sess-1", "b2").AddUserMessage("Q").AddAssistantMessage("A2").Build();
        sessionStore.AddThread("sess-1", b1);
        sessionStore.AddThread("sess-1", b2);

        var comparison = await RetroactiveScorer.CompareThreadsAsync(
            sessionStore, "sess-1", "b1", "b2",
            [new StubDeterministicEvaluator("Score")]);

        comparison.Thread1Report.Cases.Should().ContainSingle();
        comparison.Thread2Report.Cases.Should().ContainSingle();
    }

    [Fact]
    public async Task CompareThreads_MissingThread_Throws()
    {
        var sessionStore = new FakeSessionStore();
        var b1 = new ThreadBuilder("sess-1", "b1").AddUserMessage("Q").AddAssistantMessage("A").Build();
        sessionStore.AddThread("sess-1", b1);

        var act = async () => await RetroactiveScorer.CompareThreadsAsync(
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
        var bPass = new ThreadBuilder("sess-1", "b-pass").AddUserMessage("Q").AddAssistantMessage("A").Build();
        var bFail = new ThreadBuilder("sess-1", "b-fail").AddUserMessage("Q").AddAssistantMessage("A").Build();
        sessionStore.AddThread("sess-1", bPass);
        sessionStore.AddThread("sess-1", bFail);

        // Use NumericMetric evaluator: pass thread gets 0.8, fail thread gets 0.0
        var tournament = await RetroactiveScorer.TournamentAsync(
            sessionStore, "sess-1", ["b-pass", "b-fail"],
            new NumericStubEvaluator("Score", scoreForPass: 0.8));

        tournament.Rankings.Should().HaveCount(2);
        // Ranked descending by score: b-pass first
        tournament.Rankings[0].ThreadId.Should().Be("b-pass");
        tournament.Rankings[1].ThreadId.Should().Be("b-fail");
        tournament.Rankings[0].Rank.Should().Be(1);
        tournament.Rankings[1].Rank.Should().Be(2);
    }

    [Fact]
    public async Task Tournament_SingleThread_SingleEntry()
    {
        var sessionStore = new FakeSessionStore();
        var thread = new ThreadBuilder("sess-1", "only").AddUserMessage("Q").AddAssistantMessage("A").Build();
        sessionStore.AddThread("sess-1", thread);

        var result = await RetroactiveScorer.TournamentAsync(
            sessionStore, "sess-1", ["only"],
            new StubDeterministicEvaluator("Score"));

        result.Rankings.Should().ContainSingle();
        result.Rankings[0].Rank.Should().Be(1);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Returns a fixed NumericMetric score so we can rank threads.</summary>
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
