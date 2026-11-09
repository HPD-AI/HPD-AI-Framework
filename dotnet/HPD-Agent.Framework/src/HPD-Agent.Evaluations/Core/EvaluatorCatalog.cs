// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent.Evaluations.Evaluators;
using HPD.Agent.Evaluations.Evaluators.Deterministic;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations;

/// <summary>Stable metadata for a built-in evaluator.</summary>
public sealed record EvaluatorDescriptor(
    string Id,
    string Name,
    string Category,
    string Kind,
    IReadOnlyList<string> MetricNames,
    EvalPolicy DefaultPolicy,
    bool RequiresJudge,
    IReadOnlyList<string> RequiredContexts,
    string Description);

/// <summary>Catalog and policy helpers for built-in evaluators.</summary>
public static class EvaluatorCatalog
{
    private static readonly string[] _noContexts = [];
    private static readonly string[] _turnContext = ["Turn Evaluation Context"];
    private static readonly string[] _groundTruth = ["Ground Truth"];
    private static readonly string[] _grounding = ["Grounding Documents"];
    private static readonly string[] _conversation = ["Conversation History"];
    private static readonly string[] _reasoning = ["Reasoning"];
    private static readonly string[] _trace = ["Turn Trace"];

    /// <summary>Gets every built-in evaluator descriptor.</summary>
    public static IReadOnlyList<EvaluatorDescriptor> All { get; } =
    [
        D("output-contains", "Output Contains", "Assertion", "Deterministic", ["Output Contains"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Passes when the output contains the expected substring."),
        D("contains-any", "Contains Any", "Assertion", "Deterministic", ["Contains Any"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Passes when the output contains at least one expected substring."),
        D("contains-all", "Contains All", "Assertion", "Deterministic", ["Contains All"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Passes when the output contains all expected substrings."),
        D("case-insensitive-contains", "Case-Insensitive Contains", "Assertion", "Deterministic", ["Case-Insensitive Contains"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Passes when the output contains a substring ignoring case."),
        D("starts-with", "Starts With", "Assertion", "Deterministic", ["Starts With"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Checks the start of the output."),
        D("word-count", "Word Count", "Assertion", "Deterministic", ["Word Count"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Checks exact, minimum, or maximum output word count."),
        D("levenshtein", "Levenshtein Similarity", "Assertion", "Deterministic", ["Levenshtein Similarity"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Reports normalized edit similarity to expected text."),
        D("refusal", "Refusal", "Assertion", "Deterministic", ["Refusal"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Detects common refusal language."),
        D("output-matches-regex", "Output Matches Regex", "Assertion", "Deterministic", ["Output Matches Regex"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Checks output against a regular expression."),
        D("output-equals", "Output Equals", "Assertion", "Deterministic", ["Output Equals"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Checks exact normalized output equality."),
        D("equals-ground-truth", "Equals Ground Truth", "Assertion", "Deterministic", ["Equals Ground Truth"], EvalPolicy.MustAlwaysPass, false, _groundTruth, "Checks exact normalized equality with ground truth."),
        D("keyword-coverage", "Keyword Coverage", "Assertion", "Deterministic", ["Keyword Coverage"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Measures expected keyword coverage."),
        D("content-similarity", "Content Similarity", "Assertion", "Deterministic", ["Content Similarity"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Measures textual similarity to expected content."),
        D("json-validity", "JSON Validity", "Structured Output", "Deterministic", ["JSON Validity"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Checks that output is valid JSON."),
        D("xml-validity", "XML Validity", "Structured Output", "Deterministic", ["XML Validity"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Checks that output is well-formed XML."),
        D("html-shape", "HTML Shape", "Structured Output", "Deterministic", ["HTML Shape"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Checks plausible HTML shape and required tags."),
        D("sql-shape", "SQL Shape", "Structured Output", "Deterministic", ["SQL Shape"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Checks plausible SQL statement shape."),
        D("schema-conformance", "Schema Conformance", "Structured Output", "Deterministic", ["Schema Conformance"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Checks JSON output against a schema."),
        D("field-completeness", "Field Completeness", "Structured Output", "Deterministic", ["Field Completeness"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Checks required JSON fields."),
        D("field-accuracy", "Field Accuracy", "Structured Output", "Deterministic", ["Field Accuracy"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Checks a JSON field against an expected value."),
        D("max-duration", "Max Duration", "Performance", "Deterministic", ["Max Duration"], EvalPolicy.MustAlwaysPass, false, _turnContext, "Fails when turn duration exceeds a limit."),
        D("max-iterations", "Max Iterations", "Performance", "Deterministic", ["Max Iterations"], EvalPolicy.MustAlwaysPass, false, _turnContext, "Fails when iterations exceed a limit."),
        D("max-tokens", "Max Tokens", "Performance", "Deterministic", ["Max Tokens"], EvalPolicy.MustAlwaysPass, false, _turnContext, "Fails when total tokens exceed a limit."),
        D("max-input-tokens", "Max Input Tokens", "Performance", "Deterministic", ["Max Input Tokens"], EvalPolicy.MustAlwaysPass, false, _turnContext, "Fails when input tokens exceed a limit."),
        D("max-output-tokens", "Max Output Tokens", "Performance", "Deterministic", ["Max Output Tokens"], EvalPolicy.MustAlwaysPass, false, _turnContext, "Fails when output tokens exceed a limit."),
        D("latency", "Latency", "Performance", "Deterministic", ["Latency"], EvalPolicy.TrackTrend, false, _turnContext, "Reports turn latency."),
        D("max-cost", "Max Cost", "Performance", "Deterministic", ["Max Cost"], EvalPolicy.MustAlwaysPass, false, _turnContext, "Fails when estimated cost exceeds a limit."),
        D("tool-was-called", "Tool Was Called", "Tool Behavior", "Deterministic", ["Tool Was Called"], EvalPolicy.MustAlwaysPass, false, _turnContext, "Checks whether a tool was called."),
        D("tool-call-count", "Tool Call Count", "Tool Behavior", "Deterministic", ["Tool Call Count"], EvalPolicy.MustAlwaysPass, false, _turnContext, "Checks a tool call count."),
        D("tool-argument-matches", "Tool Argument Matches", "Tool Behavior", "Deterministic", ["Tool Argument Matches"], EvalPolicy.MustAlwaysPass, false, _turnContext, "Checks a tool argument value."),
        D("tool-result-contains", "Tool Result Contains", "Tool Behavior", "Deterministic", ["Tool Result Contains"], EvalPolicy.MustAlwaysPass, false, _turnContext, "Checks tool result content."),
        D("no-tools-called", "No Tools Called", "Tool Behavior", "Deterministic", ["No Tools Called"], EvalPolicy.MustAlwaysPass, false, _turnContext, "Checks that no tools were called."),
        D("tool-call-order", "Tool Call Order", "Tool Behavior", "Deterministic", ["Tool Call Order"], EvalPolicy.MustAlwaysPass, false, _turnContext, "Checks tool call order."),
        D("tool-call-f1", "Tool Call F1", "Tool Behavior", "Deterministic", ["Tool Call F1"], EvalPolicy.MustAlwaysPass, false, _turnContext, "Reports tool-call precision, recall, and F1."),
        D("has-matching-span", "Has Matching Span", "Tool Behavior", "Deterministic", ["Has Matching Span"], EvalPolicy.MustAlwaysPass, false, _trace, "Checks for a matching turn trace span."),
        D("turn-autonomy", "Turn Autonomy", "Risk / Autonomy", "Deterministic", ["Turn Autonomy"], EvalPolicy.TrackTrend, false, _turnContext, "Scores agent autonomy for a turn."),
        D("bleu", "BLEU", "NLP", "Deterministic", ["BLEU"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Computes BLEU overlap."),
        D("gleu", "GLEU", "NLP", "Deterministic", ["GLEU"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Computes GLEU overlap."),
        D("text-f1", "Text F1", "NLP", "Deterministic", ["F1"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Computes word-overlap F1."),
        D("rouge", "ROUGE", "NLP", "Deterministic", ["ROUGE"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Computes ROUGE overlap."),
        D("meteor", "METEOR", "NLP", "Deterministic", ["METEOR"], EvalPolicy.MustAlwaysPass, false, _noContexts, "Computes METEOR-style alignment."),
        D("context-relevance", "Context Relevance", "Grounding", "LLM Judge", ["Context Relevance"], EvalPolicy.TrackTrend, true, _grounding, "Scores retrieved context relevance."),
        D("answer-similarity", "Answer Similarity", "Grounding", "LLM Judge", ["Answer Similarity"], EvalPolicy.TrackTrend, true, _groundTruth, "Scores semantic answer similarity."),
        D("aspect-critic", "Aspect Critic", "Reasoning", "LLM Judge", ["Aspect Critic"], EvalPolicy.TrackTrend, true, _noContexts, "Grades output against a custom rubric."),
        D("goal-accuracy", "Goal Accuracy", "Reasoning", "LLM Judge", ["Goal Accuracy"], EvalPolicy.TrackTrend, true, _groundTruth, "Scores goal achievement accuracy."),
        D("topic-adherence", "Topic Adherence", "Reasoning", "LLM Judge", ["Topic Adherence"], EvalPolicy.TrackTrend, true, _noContexts, "Scores adherence to allowed topics."),
        D("custom-judge", "Custom Judge", "Reasoning", "LLM Judge", ["Custom Judge"], EvalPolicy.TrackTrend, true, _noContexts, "Evaluates against caller-defined judge instructions."),
        D("task-success", "Task Success", "Reasoning", "LLM Judge", ["Task Success"], EvalPolicy.TrackTrend, true, _turnContext, "Scores task success."),
        D("conversation-coherence", "Conversation Coherence", "Conversation", "LLM Judge", ["Conversation Coherence"], EvalPolicy.TrackTrend, true, _conversation, "Scores coherence across turns."),
        D("goal-progression", "Goal Progression", "Conversation", "LLM Judge", ["Goal Progression"], EvalPolicy.TrackTrend, true, _conversation, "Scores progress toward the conversation goal."),
        D("repetition-detection", "Repetition", "Conversation", "LLM Judge", ["Repetition"], EvalPolicy.TrackTrend, true, _conversation, "Scores repeated content across turns."),
        D("memory-accuracy", "Memory Accuracy", "Conversation", "LLM Judge", ["Memory Accuracy"], EvalPolicy.TrackTrend, true, _conversation, "Scores factual memory across turns."),
        D("hallucination", "Hallucination", "Grounding", "LLM Judge", ["Hallucination"], EvalPolicy.TrackTrend, true, _grounding, "Detects unsupported claims."),
        D("context-recall", "Context Recall", "Grounding", "LLM Judge", ["Context Recall"], EvalPolicy.TrackTrend, true, _grounding, "Measures claim coverage by grounding context."),
        D("context-precision", "Context Precision", "Grounding", "LLM Judge", ["Context Precision"], EvalPolicy.TrackTrend, true, _grounding, "Measures grounding context precision."),
        D("reasoning-coherence", "Reasoning Coherence", "Reasoning", "LLM Judge", ["Reasoning Coherence"], EvalPolicy.TrackTrend, true, _reasoning, "Scores reasoning-to-output coherence."),
        D("reasoning-groundedness", "Reasoning Groundedness", "Reasoning", "LLM Judge", ["Reasoning Groundedness"], EvalPolicy.TrackTrend, true, _reasoning, "Scores reasoning groundedness."),
        D("reasoning-efficiency", "Reasoning Efficiency", "Reasoning", "LLM Judge", ["Reasoning Efficiency"], EvalPolicy.TrackTrend, true, _reasoning, "Scores reasoning efficiency."),
        D("turn-risk", "Turn Risk", "Risk / Autonomy", "LLM Judge", ["Turn Risk"], EvalPolicy.TrackTrend, true, _turnContext, "Scores potential turn risk."),
        D("sql-semantic-equivalence", "SQL Equivalence", "Specialized", "LLM Judge", ["SQL Equivalence"], EvalPolicy.TrackTrend, true, _noContexts, "Scores semantic SQL equivalence."),
        D("noise-sensitivity", "Noise Sensitivity", "Specialized", "LLM Judge", ["Noise Sensitivity"], EvalPolicy.TrackTrend, true, _noContexts, "Scores output stability under noisy input."),
        D("semantic-field-equality", "Semantic Field Equality", "Structured Output", "LLM Judge", ["Semantic Field Equality"], EvalPolicy.TrackTrend, true, _groundTruth, "Scores semantic equality for a structured field."),
        D("content-harm", "Content Harm", "Safety", "LLM Judge", ["Content Harm", "Content Harm Passed"], EvalPolicy.TrackTrend, true, _turnContext, "Detects harmful content and actions."),
        D("hate-harassment", "Hate/Harassment", "Safety", "LLM Judge", ["Hate/Harassment", "Hate/Harassment Passed"], EvalPolicy.TrackTrend, true, _turnContext, "Detects hateful or harassing content."),
        D("violence-safety", "Violence Safety", "Safety", "LLM Judge", ["Violence Safety", "Violence Safety Passed"], EvalPolicy.TrackTrend, true, _turnContext, "Detects violent content and unsafe guidance."),
        D("self-harm-safety", "Self-Harm Safety", "Safety", "LLM Judge", ["Self-Harm Safety", "Self-Harm Safety Passed"], EvalPolicy.TrackTrend, true, _turnContext, "Evaluates self-harm safety."),
        D("sexual-content-safety", "Sexual Content Safety", "Safety", "LLM Judge", ["Sexual Content Safety", "Sexual Content Safety Passed"], EvalPolicy.TrackTrend, true, _turnContext, "Evaluates sexual content risk."),
        D("prompt-injection", "Prompt Injection", "Safety", "LLM Judge", ["Prompt Injection", "Prompt Injection Passed"], EvalPolicy.TrackTrend, true, _turnContext, "Detects prompt injection and hidden-context attacks."),
        D("jailbreak-attempt", "Jailbreak Attempt", "Safety", "LLM Judge", ["Jailbreak Attempt", "Jailbreak Attempt Passed"], EvalPolicy.TrackTrend, true, _turnContext, "Detects policy and constraint bypass attempts."),
        D("sensitive-data-leak", "Sensitive Data Leak", "Safety", "LLM Judge", ["Sensitive Data Leak", "Sensitive Data Leak Passed"], EvalPolicy.TrackTrend, true, _turnContext, "Detects secret and private-data leakage."),
        D("protected-material", "Protected Material", "Safety", "LLM Judge", ["Protected Material", "Protected Material Passed"], EvalPolicy.TrackTrend, true, _turnContext, "Detects protected-material reproduction."),
        D("code-security-risk", "Code Security Risk", "Safety", "LLM Judge", ["Code Security Risk", "Code Security Risk Passed"], EvalPolicy.TrackTrend, true, _turnContext, "Detects unsafe code or commands."),
        D("ungrounded-sensitive-attributes", "Ungrounded Sensitive Attributes", "Safety", "LLM Judge", ["Ungrounded Sensitive Attributes", "Ungrounded Sensitive Attributes Passed"], EvalPolicy.TrackTrend, true, _turnContext, "Detects unsupported sensitive-attribute inferences."),
        D("policy-compliance", "Policy Compliance", "Safety", "LLM Judge", ["Policy Compliance", "Policy Compliance Passed"], EvalPolicy.TrackTrend, true, _turnContext, "Evaluates a caller-supplied safety policy."),
        D("weighted-score", "Weighted Score", "Composition", "Composite", ["Weighted Score"], EvalPolicy.TrackTrend, false, _noContexts, "Combines evaluator scores using normalized weights."),
        D("threshold-gate", "Threshold Gate", "Composition", "Composite", ["Threshold Gate"], EvalPolicy.TrackTrend, false, _noContexts, "Converts a numeric score into a boolean threshold."),
        D("not", "Not", "Composition", "Composite", ["Not"], EvalPolicy.TrackTrend, false, _noContexts, "Inverts another evaluator's primary result."),
    ];

    internal static EvalPolicy GetDefaultPolicy(IEvaluator evaluator) =>
        evaluator is HpdDeterministicEvaluatorBase &&
        evaluator is not LatencyEvaluator &&
        evaluator is not TurnAutonomyEvaluator
            ? EvalPolicy.MustAlwaysPass
            : EvalPolicy.TrackTrend;

    private static EvaluatorDescriptor D(
        string id,
        string name,
        string category,
        string kind,
        IReadOnlyList<string> metricNames,
        EvalPolicy defaultPolicy,
        bool requiresJudge,
        IReadOnlyList<string> requiredContexts,
        string description) =>
        new(
            id,
            name,
            category,
            kind,
            metricNames,
            defaultPolicy,
            requiresJudge,
            requiredContexts,
            description);
}
