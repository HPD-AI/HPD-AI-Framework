// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Evaluations.Evaluators;
using HPD.Agent.Evaluations.Tests.Infrastructure;

namespace HPD.Agent.Evaluations.Tests.Evaluators;

/// <summary>
/// Tests for HpdLlmJudgeEvaluatorBase — the single-call XML-tag judge base class.
///
/// Key behaviors:
/// 1. Without ChatConfiguration → error diagnostic returned, judge is NOT called.
/// 2. With valid ChatConfiguration → BuildJudgePrompt() called, response parsed.
/// 3. Judge LLM throws → error diagnostic, no metric value set.
/// 4. MarkAsHpdBuiltIn() is called on the result.
///
/// Tests use a minimal concrete subclass (StubJudgeEvaluator) rather than a real evaluator
/// so they test only the base class contract, not the specific prompt logic.
/// </summary>
public sealed class HpdLlmJudgeBaseClassTests
{
    // ── Concrete stub extending HpdLlmJudgeEvaluatorBase ─────────────────────

    /// <summary>
    /// Minimal stub that records the judge prompt it was given and
    /// allows the test to control what response text to parse.
    /// Metric name: "Stub Score" (NumericMetric).
    /// Response format: "<S0>cot</S0><S1>reason</S1><S2>0.8</S2>"
    /// </summary>
    private sealed class StubLlmJudge : HpdLlmJudgeEvaluatorBase
    {
        public IReadOnlyList<ChatMessage>? LastPrompt { get; private set; }

        public override IReadOnlyCollection<string> EvaluationMetricNames => ["Stub Score"];

        protected override List<ChatMessage> BuildJudgePrompt(
            IEnumerable<ChatMessage> messages,
            ChatResponse modelResponse,
            IEnumerable<EvaluationContext>? additionalContext)
        {
            var prompt = new List<ChatMessage>
            {
                new(ChatRole.User, $"Rate this response: {modelResponse.Text}")
            };
            LastPrompt = prompt;
            return prompt;
        }

        protected override void ParseJudgeResponse(
            string responseText,
            EvaluationResult result,
            ChatResponse judgeResponse,
            TimeSpan duration)
        {
            var metric = result.Metrics["Stub Score"] as NumericMetric;
            if (metric is null) return;

            // Try to parse a numeric value from "<S2>X.X</S2>"
            var match = System.Text.RegularExpressions.Regex.Match(
                responseText, @"<S2>(?<v>[\d.]+)</S2>");

            if (match.Success && double.TryParse(match.Groups["v"].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var score))
            {
                metric.Value = score;
                metric.Reason = "Parsed from judge response.";
            }
        }

        protected override EvaluationResult CreateEmptyResult() =>
            new(new NumericMetric("Stub Score"));
    }

    private static ChatResponse Respond(string text) =>
        new([new ChatMessage(ChatRole.Assistant, text)]);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LlmJudge_NoChatConfiguration_ReturnsErrorDiagnostic()
    {
        var judge = new StubLlmJudge();

        // chatConfiguration is null → must return error diagnostic, not call judge
        var result = await judge.EvaluateAsync(
            [],
            Respond("some output"),
            chatConfiguration: null);

        result.ShouldHaveErrorDiagnostic();
        judge.LastPrompt.Should().BeNull("BuildJudgePrompt should NOT be called when no client is provided");
    }

    [Fact]
    public async Task LlmJudge_ValidClient_CallsJudgeAndParsesScore()
    {
        var fakeClient = new FakeJudgeChatClient();
        fakeClient.EnqueueResponse("<S0>thinking</S0><S1>good response</S1><S2>0.9</S2>");

        var chatConfig = new ChatConfiguration(fakeClient);
        var judge = new StubLlmJudge();

        var result = await judge.EvaluateAsync(
            [],
            Respond("Paris is the capital."),
            chatConfiguration: chatConfig);

        fakeClient.CallCount.Should().Be(1, "judge should be called exactly once");
        judge.LastPrompt.Should().NotBeNull();

        var metric = result.Metrics["Stub Score"] as NumericMetric;
        metric!.Value.Should().Be(0.9);
        metric.ShouldBeMarkedAsBuiltIn();
    }

    [Fact]
    public async Task LlmJudge_ClientThrows_ReturnsErrorDiagnostic()
    {
        var fakeClient = new FakeJudgeChatClient();
        fakeClient.ThrowOn(new HttpRequestException("connection refused"));

        var chatConfig = new ChatConfiguration(fakeClient);
        var judge = new StubLlmJudge();

        var result = await judge.EvaluateAsync(
            [],
            Respond("Paris"),
            chatConfiguration: chatConfig);

        result.ShouldHaveErrorDiagnostic();
        var metric = result.Metrics["Stub Score"] as NumericMetric;
        metric!.Value.Should().BeNull("metric should not be set when judge call fails");
    }

    [Fact]
    public async Task LlmJudge_UnparsableResponse_MetricValueNull()
    {
        var fakeClient = new FakeJudgeChatClient();
        fakeClient.EnqueueResponse("I cannot evaluate this.");  // no <S2> tag

        var chatConfig = new ChatConfiguration(fakeClient);
        var judge = new StubLlmJudge();

        var result = await judge.EvaluateAsync(
            [],
            Respond("Some output"),
            chatConfiguration: chatConfig);

        // No error (judge call succeeded), but ParseJudgeResponse couldn't extract a value
        var metric = result.Metrics["Stub Score"] as NumericMetric;
        metric!.Value.Should().BeNull("unparsable response → value remains null");
    }

    [Fact]
    public async Task LlmJudge_PromptContainsModelResponse()
    {
        var fakeClient = new FakeJudgeChatClient();
        fakeClient.EnqueueResponse("<S2>0.5</S2>");

        var chatConfig = new ChatConfiguration(fakeClient);
        var judge = new StubLlmJudge();

        await judge.EvaluateAsync([], Respond("The answer is 42."), chatConfiguration: chatConfig);

        // The stub puts the model response text into the prompt
        judge.LastPrompt![0].Text.Should().Contain("The answer is 42.");
    }
}

/// <summary>
/// Tests for HpdDecomposeVerifyEvaluatorBase — the multi-step claim-based judge base class.
///
/// Key behaviors:
/// 1. Without ChatConfiguration → error diagnostic.
/// 2. Empty response text → error diagnostic (no claims to extract).
/// 3. Normal flow: ExtractClaims → VerifyClaims → AggregateScore → BuildReason.
/// 4. Zero claims extracted → score 0, warning diagnostic.
/// </summary>
public sealed class HpdDecomposeVerifyBaseClassTests
{
    // ── Concrete stub ─────────────────────────────────────────────────────────

    private sealed class StubDecomposeVerify : HpdDecomposeVerifyEvaluatorBase
    {
        private readonly string[] _claimsToReturn;
        private readonly ClaimVerdictType _verdictForAll;

        public StubDecomposeVerify(
            string[] claimsToReturn,
            ClaimVerdictType verdictForAll = ClaimVerdictType.Supported)
        {
            _claimsToReturn = claimsToReturn;
            _verdictForAll = verdictForAll;
        }

        public override IReadOnlyCollection<string> EvaluationMetricNames => ["Stub DV Score"];

        protected override ValueTask<IReadOnlyList<string>> ExtractClaimsAsync(
            string outputText, IChatClient judgeClient, CancellationToken ct)
            => ValueTask.FromResult<IReadOnlyList<string>>(_claimsToReturn);

        protected override ValueTask<IReadOnlyList<ClaimVerdict>> VerifyClaimsAsync(
            IReadOnlyList<string> claims,
            IEnumerable<EvaluationContext>? additionalContext,
            IChatClient judgeClient,
            CancellationToken ct)
        {
            var verdicts = claims
                .Select(c => new ClaimVerdict(c, _verdictForAll))
                .ToList();
            return ValueTask.FromResult<IReadOnlyList<ClaimVerdict>>(verdicts);
        }

        protected override double AggregateScore(IReadOnlyList<ClaimVerdict> verdicts)
        {
            if (verdicts.Count == 0) return 0.0;
            int supported = verdicts.Count(v => v.Verdict == ClaimVerdictType.Supported);
            return (double)supported / verdicts.Count;
        }

        protected override NumericMetric CreateMetric() => new("Stub DV Score");
    }

    private static ChatResponse Respond(string text) =>
        new([new ChatMessage(ChatRole.Assistant, text)]);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DecomposeVerify_NoChatConfiguration_ReturnsErrorDiagnostic()
    {
        var evaluator = new StubDecomposeVerify(["claim 1"]);

        var result = await evaluator.EvaluateAsync(
            [],
            Respond("some output"),
            chatConfiguration: null);

        result.ShouldHaveErrorDiagnostic();
    }

    [Fact]
    public async Task DecomposeVerify_EmptyResponse_ReturnsErrorDiagnostic()
    {
        var fakeClient = new FakeJudgeChatClient();
        var chatConfig = new ChatConfiguration(fakeClient);

        var evaluator = new StubDecomposeVerify(["claim 1"]);

        var result = await evaluator.EvaluateAsync(
            [],
            Respond(string.Empty),  // empty response text
            chatConfiguration: chatConfig);

        result.ShouldHaveErrorDiagnostic();
        fakeClient.CallCount.Should().Be(0, "judge should not be called for empty response");
    }

    [Fact]
    public async Task DecomposeVerify_ZeroClaims_ReturnsZeroWithWarning()
    {
        var fakeClient = new FakeJudgeChatClient();
        var chatConfig = new ChatConfiguration(fakeClient);

        // Stub returns empty claims list
        var evaluator = new StubDecomposeVerify([]);

        var result = await evaluator.EvaluateAsync(
            [],
            Respond("This response has no verifiable claims."),
            chatConfiguration: chatConfig);

        var metric = result.Metrics["Stub DV Score"] as NumericMetric;
        metric!.Value.Should().Be(0.0);
        metric.Diagnostics.Should().Contain(d =>
            d.Severity == EvaluationDiagnosticSeverity.Warning &&
            d.Message.Contains("empty"));
    }

    [Fact]
    public async Task DecomposeVerify_AllSupported_ReturnsOne()
    {
        var fakeClient = new FakeJudgeChatClient();
        var chatConfig = new ChatConfiguration(fakeClient);

        var evaluator = new StubDecomposeVerify(
            ["claim 1", "claim 2", "claim 3"],
            ClaimVerdictType.Supported);

        var result = await evaluator.EvaluateAsync(
            [],
            Respond("Paris is the capital of France."),
            chatConfiguration: chatConfig);

        result.ShouldHaveNumericMetricInRange("Stub DV Score", 1.0, 1.0);
    }

    [Fact]
    public async Task DecomposeVerify_AllContradicted_ReturnsZero()
    {
        var fakeClient = new FakeJudgeChatClient();
        var chatConfig = new ChatConfiguration(fakeClient);

        var evaluator = new StubDecomposeVerify(
            ["claim 1", "claim 2"],
            ClaimVerdictType.Contradicted);

        var result = await evaluator.EvaluateAsync(
            [],
            Respond("Some text"),
            chatConfiguration: chatConfig);

        result.ShouldHaveNumericMetricInRange("Stub DV Score", 0.0, 0.0);
    }

    [Fact]
    public async Task DecomposeVerify_HalfSupported_ReturnsHalf()
    {
        // Mix: 2 supported + 2 contradicted claims
        // Need a custom stub that alternates verdicts
        var fakeClient = new FakeJudgeChatClient();
        var chatConfig = new ChatConfiguration(fakeClient);

        var evaluator = new AlternatingVerdictStub(4);

        var result = await evaluator.EvaluateAsync(
            [],
            Respond("Some text"),
            chatConfiguration: chatConfig);

        result.ShouldHaveNumericMetricInRange("Stub DV Score", 0.49, 0.51);
    }

    [Fact]
    public async Task DecomposeVerify_ReasonDescribesCounts()
    {
        var fakeClient = new FakeJudgeChatClient();
        var chatConfig = new ChatConfiguration(fakeClient);

        var evaluator = new StubDecomposeVerify(
            ["c1", "c2"],
            ClaimVerdictType.Supported);

        var result = await evaluator.EvaluateAsync(
            [],
            Respond("Text"),
            chatConfiguration: chatConfig);

        var metric = result.Metrics["Stub DV Score"] as NumericMetric;
        metric!.Reason.Should().Contain("2").And.Contain("supported");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Alternates Supported/Contradicted to get a 50% score.</summary>
    private sealed class AlternatingVerdictStub(int claimCount) : HpdDecomposeVerifyEvaluatorBase
    {
        public override IReadOnlyCollection<string> EvaluationMetricNames => ["Stub DV Score"];

        protected override ValueTask<IReadOnlyList<string>> ExtractClaimsAsync(
            string outputText, IChatClient judgeClient, CancellationToken ct)
            => ValueTask.FromResult<IReadOnlyList<string>>(
                Enumerable.Range(0, claimCount).Select(i => $"claim {i}").ToList());

        protected override ValueTask<IReadOnlyList<ClaimVerdict>> VerifyClaimsAsync(
            IReadOnlyList<string> claims,
            IEnumerable<EvaluationContext>? additionalContext,
            IChatClient judgeClient, CancellationToken ct)
        {
            var verdicts = claims.Select((c, i) => new ClaimVerdict(c,
                i % 2 == 0 ? ClaimVerdictType.Supported : ClaimVerdictType.Contradicted)).ToList();
            return ValueTask.FromResult<IReadOnlyList<ClaimVerdict>>(verdicts);
        }

        protected override double AggregateScore(IReadOnlyList<ClaimVerdict> verdicts) =>
            verdicts.Count == 0 ? 0.0 :
                (double)verdicts.Count(v => v.Verdict == ClaimVerdictType.Supported) / verdicts.Count;

        protected override NumericMetric CreateMetric() => new("Stub DV Score");
    }
}
