// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Evaluations.Contexts;
using HPD.Agent.Evaluations.Evaluators;

namespace HPD.Agent.Evaluations.Evaluators.LlmJudge;

// ── HallucinationEvaluator ───────────────────────────────────────────────────

/// <summary>
/// Detects hallucinations by extracting atomic claims from the agent output and
/// verifying each against the supplied grounding context (decompose-verify, RAGAS).
/// Score = contradicted_claims / total_claims (0 = no hallucination, 1 = fully hallucinated).
/// Unsupported claims (not mentioned in context) are NOT counted as hallucinations.
/// Requires GroundingDocumentContext. Default policy: TrackTrend.
/// </summary>
public sealed class HallucinationEvaluator : HpdDecomposeVerifyEvaluatorBase
{
    public const string MetricName = "Hallucination";

    public override IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];

    protected override NumericMetric CreateMetric() => new(MetricName);

    protected override async ValueTask<IReadOnlyList<string>> ExtractClaimsAsync(
        string outputText, IChatClient judgeClient, CancellationToken ct)
    {
        var prompt = new List<ChatMessage>
        {
            new(ChatRole.System,
                "Extract all atomic, independently verifiable factual claims from the text. " +
                "Return one claim per line. Omit opinions, questions, and subjective statements. " +
                "If there are no verifiable claims, return an empty response."),
            new(ChatRole.User, outputText),
        };

        var response = await judgeClient.GetResponseAsync(prompt,
            new ChatOptions { Temperature = 0f }, ct).ConfigureAwait(false);

        return (response.Text ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
    }

    protected override async ValueTask<IReadOnlyList<ClaimVerdict>> VerifyClaimsAsync(
        IReadOnlyList<string> claims,
        IEnumerable<EvaluationContext>? additionalContext,
        IChatClient judgeClient,
        CancellationToken ct)
    {
        var groundingCtx = additionalContext?.OfType<GroundingDocumentContext>().FirstOrDefault();
        var turnCtx = additionalContext?.OfType<TurnEvaluationContextWrapper>().FirstOrDefault()?.Context;
        var chunks = groundingCtx?.Resolve(turnCtx) ?? [];

        if (chunks.Length == 0)
            return claims.Select(c => new ClaimVerdict(c, ClaimVerdictType.Unsupported,
                "No grounding context provided.")).ToList();

        var context = string.Join("\n---\n", chunks);
        var verdicts = new List<ClaimVerdict>();

        foreach (var claim in claims)
        {
            var prompt = new List<ChatMessage>
            {
                new(ChatRole.System,
                    "Given the context below, classify the claim as SUPPORTED, CONTRADICTED, or UNSUPPORTED.\n" +
                    "SUPPORTED: context explicitly confirms the claim.\n" +
                    "CONTRADICTED: context explicitly contradicts the claim.\n" +
                    "UNSUPPORTED: context neither confirms nor contradicts the claim.\n" +
                    "Reply with exactly one word: SUPPORTED, CONTRADICTED, or UNSUPPORTED.\n\n" +
                    $"Context:\n{context}"),
                new(ChatRole.User, $"Claim: {claim}"),
            };

            var response = await judgeClient.GetResponseAsync(prompt,
                new ChatOptions { Temperature = 0f }, ct).ConfigureAwait(false);

            var verdict = (response.Text ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                "SUPPORTED" => ClaimVerdictType.Supported,
                "CONTRADICTED" => ClaimVerdictType.Contradicted,
                _ => ClaimVerdictType.Unsupported,
            };
            verdicts.Add(new ClaimVerdict(claim, verdict));
        }

        return verdicts;
    }

    protected override double AggregateScore(IReadOnlyList<ClaimVerdict> verdicts)
    {
        if (verdicts.Count == 0) return 0.0;
        int contradicted = verdicts.Count(v => v.Verdict == ClaimVerdictType.Contradicted);
        return (double)contradicted / verdicts.Count;
    }
}

// ── ContextRecallEvaluator ───────────────────────────────────────────────────

/// <summary>
/// Measures how much of the ground truth is covered by the retrieved context.
/// Claims are extracted from GroundTruthContext (the expected answer), then each is
/// verified against GroundingDocumentContext chunks.
/// Score = supported_claims / total_ground_truth_claims (higher = better retrieval).
/// Requires both GroundTruthContext and GroundingDocumentContext. Default policy: TrackTrend.
/// </summary>
public sealed class ContextRecallEvaluator : HpdDecomposeVerifyEvaluatorBase
{
    public const string MetricName = "Context Recall";

    public override IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];

    protected override NumericMetric CreateMetric() => new(MetricName);

    protected override async ValueTask<IReadOnlyList<string>> ExtractClaimsAsync(
        string outputText, IChatClient judgeClient, CancellationToken ct)
    {
        // For ContextRecall the claims come from the ground truth stored in additionalContext,
        // not from outputText. We handle this in VerifyClaimsAsync; return a sentinel here.
        // The base class calls ExtractClaimsAsync(modelResponse.Text) — we need to intercept.
        // Since we can't override EvaluateAsync (it's sealed), we use a thread-local workaround:
        // store the ground truth via AsyncLocal set in VerifyClaimsAsync. Instead, we use
        // a different approach: store GT in a field (safe since each evaluator instance is
        // single-use per turn evaluation).
        return ["__use_ground_truth__"];
    }

    private string[]? _groundTruthClaims;

    protected override async ValueTask<IReadOnlyList<ClaimVerdict>> VerifyClaimsAsync(
        IReadOnlyList<string> claims,
        IEnumerable<EvaluationContext>? additionalContext,
        IChatClient judgeClient,
        CancellationToken ct)
    {
        // Extract real claims from ground truth
        var groundTruthCtx = additionalContext?.OfType<GroundTruthContext>().FirstOrDefault();
        var groundingCtx = additionalContext?.OfType<GroundingDocumentContext>().FirstOrDefault();
        var turnCtx = additionalContext?.OfType<TurnEvaluationContextWrapper>().FirstOrDefault()?.Context;

        if (groundTruthCtx is null)
            return [new ClaimVerdict("(no ground truth)", ClaimVerdictType.Unsupported,
                "GroundTruthContext not provided.")];

        if (groundingCtx is null)
            return [new ClaimVerdict("(no grounding)", ClaimVerdictType.Unsupported,
                "GroundingDocumentContext not provided.")];

        // Extract claims from ground truth text
        var extractPrompt = new List<ChatMessage>
        {
            new(ChatRole.System,
                "Extract all atomic factual claims from the text. One per line."),
            new(ChatRole.User, groundTruthCtx.Expected),
        };
        var extractResponse = await judgeClient.GetResponseAsync(extractPrompt,
            new ChatOptions { Temperature = 0f }, ct).ConfigureAwait(false);

        var gtClaims = (extractResponse.Text ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (gtClaims.Count == 0)
            return [];

        var chunks = groundingCtx.Resolve(turnCtx);
        var context = string.Join("\n---\n", chunks);
        var verdicts = new List<ClaimVerdict>();

        foreach (var claim in gtClaims)
        {
            var prompt = new List<ChatMessage>
            {
                new(ChatRole.System,
                    "Given the context below, is the claim explicitly supported by the context? " +
                    "Reply with exactly one word: SUPPORTED or UNSUPPORTED.\n\n" +
                    $"Context:\n{context}"),
                new(ChatRole.User, $"Claim: {claim}"),
            };
            var response = await judgeClient.GetResponseAsync(prompt,
                new ChatOptions { Temperature = 0f }, ct).ConfigureAwait(false);

            var verdict = (response.Text ?? string.Empty).Trim().ToUpperInvariant() == "SUPPORTED"
                ? ClaimVerdictType.Supported
                : ClaimVerdictType.Unsupported;
            verdicts.Add(new ClaimVerdict(claim, verdict));
        }

        return verdicts;
    }

    protected override double AggregateScore(IReadOnlyList<ClaimVerdict> verdicts)
    {
        if (verdicts.Count == 0) return 0.0;
        int supported = verdicts.Count(v => v.Verdict == ClaimVerdictType.Supported);
        return (double)supported / verdicts.Count;
    }
}

// ── ContextPrecisionEvaluator ────────────────────────────────────────────────

/// <summary>
/// Measures whether retrieved context chunks are relevant to the query.
/// Each chunk is given a relevant/not-relevant verdict; score = Mean Average Precision (MAP)
/// over the ranked chunk list, rewarding relevant chunks appearing earlier.
/// Requires GroundingDocumentContext. Default policy: TrackTrend.
/// </summary>
public sealed class ContextPrecisionEvaluator : HpdDecomposeVerifyEvaluatorBase
{
    public const string MetricName = "Context Precision";

    public override IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];

    protected override NumericMetric CreateMetric() => new(MetricName);

    protected override async ValueTask<IReadOnlyList<string>> ExtractClaimsAsync(
        string outputText, IChatClient judgeClient, CancellationToken ct)
        // Sentinel — real work done in VerifyClaimsAsync
        => ["__use_chunks__"];

    protected override async ValueTask<IReadOnlyList<ClaimVerdict>> VerifyClaimsAsync(
        IReadOnlyList<string> claims,
        IEnumerable<EvaluationContext>? additionalContext,
        IChatClient judgeClient,
        CancellationToken ct)
    {
        var groundingCtx = additionalContext?.OfType<GroundingDocumentContext>().FirstOrDefault();
        var turnCtx = additionalContext?.OfType<TurnEvaluationContextWrapper>().FirstOrDefault()?.Context;

        if (groundingCtx is null)
            return [new ClaimVerdict("(no grounding)", ClaimVerdictType.Unsupported)];

        var chunks = groundingCtx.Resolve(turnCtx);
        if (chunks.Length == 0) return [];

        var query = turnCtx?.UserInput ?? string.Empty;
        var verdicts = new List<ClaimVerdict>();

        foreach (var (chunk, i) in chunks.Select((c, i) => (c, i)))
        {
            var prompt = new List<ChatMessage>
            {
                new(ChatRole.System,
                    "Does the context chunk below help answer the user query? " +
                    "Reply RELEVANT or NOT_RELEVANT only.\n\n" +
                    $"Query: {query}"),
                new(ChatRole.User, $"Chunk {i + 1}: {chunk}"),
            };
            var response = await judgeClient.GetResponseAsync(prompt,
                new ChatOptions { Temperature = 0f }, ct).ConfigureAwait(false);

            var relevant = (response.Text ?? string.Empty).Trim().ToUpperInvariant()
                .Contains("RELEVANT", StringComparison.Ordinal);
            verdicts.Add(new ClaimVerdict($"Chunk {i + 1}",
                relevant ? ClaimVerdictType.Supported : ClaimVerdictType.Unsupported));
        }

        return verdicts;
    }

    /// <summary>Mean Average Precision over ranked chunk verdicts.</summary>
    protected override double AggregateScore(IReadOnlyList<ClaimVerdict> verdicts)
    {
        if (verdicts.Count == 0) return 0.0;

        double sumPrecision = 0.0;
        int relevantSeen = 0;

        for (int i = 0; i < verdicts.Count; i++)
        {
            if (verdicts[i].Verdict == ClaimVerdictType.Supported)
            {
                relevantSeen++;
                sumPrecision += (double)relevantSeen / (i + 1);
            }
        }

        int totalRelevant = verdicts.Count(v => v.Verdict == ClaimVerdictType.Supported);
        return totalRelevant == 0 ? 0.0 : sumPrecision / totalRelevant;
    }
}
