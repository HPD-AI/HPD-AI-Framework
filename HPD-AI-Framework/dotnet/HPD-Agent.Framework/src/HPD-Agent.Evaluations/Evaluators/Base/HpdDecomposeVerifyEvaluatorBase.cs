// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations.Evaluators;

/// <summary>
/// Classification of a claim relative to grounding context.
/// </summary>
public enum ClaimVerdictType
{
    /// <summary>The claim is explicitly supported by the grounding context.</summary>
    Supported,

    /// <summary>The claim directly contradicts the grounding context.</summary>
    Contradicted,

    /// <summary>The claim is not mentioned in the grounding context (neither supported nor contradicted).</summary>
    Unsupported,
}

/// <summary>The verdict for a single extracted claim.</summary>
public sealed record ClaimVerdict(
    string Claim,
    ClaimVerdictType Verdict,
    string? Reason = null);

/// <summary>
/// Base class for LLM-as-judge evaluators that use the decompose-verify pattern (RAGAS methodology):
///   Step 1 (LLM): Extract atomic claims from the response text.
///   Step 2 (LLM): Verify each claim against grounding context with a binary verdict.
///   Step 3 (deterministic): Aggregate verdicts into a numeric score — no LLM call.
///   Step 4 (optional LLM): Generate a coherent reason citing the per-claim verdicts.
///
/// More reproducible than holistic single-pass scoring because the judge answers a binary
/// question per claim rather than assigning a subjective holistic score.
/// Used by HallucinationEvaluator, ContextRecallEvaluator, ContextPrecisionEvaluator.
/// </summary>
public abstract class HpdDecomposeVerifyEvaluatorBase : HpdEvaluatorBase
{
    // ── Step 1: Claim extraction ──────────────────────────────────────────────

    /// <summary>
    /// Extract atomic, independently verifiable claims from the response text.
    /// Returns an empty list if the response contains no verifiable claims.
    /// </summary>
    protected abstract ValueTask<IReadOnlyList<string>> ExtractClaimsAsync(
        string outputText,
        IChatClient judgeClient,
        CancellationToken ct);

    // ── Step 2: Claim verification ────────────────────────────────────────────

    /// <summary>
    /// Verify each extracted claim against the grounding context.
    /// Each claim receives a ClaimVerdict with a supported/contradicted/unsupported verdict.
    /// </summary>
    protected abstract ValueTask<IReadOnlyList<ClaimVerdict>> VerifyClaimsAsync(
        IReadOnlyList<string> claims,
        IEnumerable<EvaluationContext>? additionalContext,
        IChatClient judgeClient,
        CancellationToken ct);

    // ── Step 3: Deterministic aggregation ─────────────────────────────────────

    /// <summary>
    /// Compute the final numeric score from the claim verdicts. No LLM call.
    /// HallucinationEvaluator: contradicted / total (lower = better)
    /// ContextRecallEvaluator: supported / total (higher = better)
    /// </summary>
    protected abstract double AggregateScore(IReadOnlyList<ClaimVerdict> verdicts);

    // ── Step 4: Reason generation (optional) ──────────────────────────────────

    /// <summary>
    /// Generate a human-readable reason for the score. Default implementation
    /// builds a summary from the verdict counts without an LLM call.
    /// Subclasses may override to make a third LLM call citing specific verdicts.
    /// </summary>
    protected virtual string BuildReason(IReadOnlyList<ClaimVerdict> verdicts, double score)
    {
        int supported = verdicts.Count(v => v.Verdict == ClaimVerdictType.Supported);
        int contradicted = verdicts.Count(v => v.Verdict == ClaimVerdictType.Contradicted);
        int unsupported = verdicts.Count(v => v.Verdict == ClaimVerdictType.Unsupported);
        return $"Score: {score:F2}. Of {verdicts.Count} claims: {supported} supported, " +
               $"{contradicted} contradicted, {unsupported} unsupported by context.";
    }

    /// <summary>Creates the typed NumericMetric this evaluator produces.</summary>
    protected abstract NumericMetric CreateMetric();

    // ── Orchestration ─────────────────────────────────────────────────────────

    public sealed override async ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var metric = CreateMetric();
        var result = new EvaluationResult(metric);

        if (chatConfiguration is null)
        {
            metric.AddDiagnostics(EvaluationDiagnostic.Error(
                $"No {nameof(ChatConfiguration)} was provided. An IChatClient is required."));
            return result;
        }

        if (string.IsNullOrWhiteSpace(modelResponse.Text))
        {
            metric.AddDiagnostics(EvaluationDiagnostic.Error(
                $"The {nameof(modelResponse)} supplied for evaluation was null or empty."));
            return result;
        }

        var judgeClient = chatConfiguration.ChatClient;

        // Step 1 — extract claims
        IReadOnlyList<string> claims = await ExtractClaimsAsync(
            modelResponse.Text, judgeClient, cancellationToken).ConfigureAwait(false);

        if (claims.Count == 0)
        {
            metric.Value = 0.0;
            metric.Reason = "No verifiable claims could be extracted from the response.";
            metric.AddDiagnostics(EvaluationDiagnostic.Warning(
                "ExtractClaims returned an empty list. Score set to 0."));
            metric.MarkAsHpdBuiltIn();
            return result;
        }

        // Step 2 — verify each claim
        IReadOnlyList<ClaimVerdict> verdicts = await VerifyClaimsAsync(
            claims, additionalContext, judgeClient, cancellationToken).ConfigureAwait(false);

        // Step 3 — deterministic aggregation
        double score = verdicts.Count > 0 ? AggregateScore(verdicts) : 0.0;
        metric.Value = Math.Round(score, 2, MidpointRounding.AwayFromZero);

        // Step 4 — reason
        metric.Reason = BuildReason(verdicts, metric.Value ?? 0.0);

        metric.MarkAsHpdBuiltIn();
        return result;
    }
}
