// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Evaluations.Tracing;

namespace HPD.Agent.Evaluations.Contexts;

/// <summary>
/// Expected output text for ground-truth comparison evaluators.
/// Used by AnswerSimilarityEvaluator, ContextRecallEvaluator, and passed through to
/// MS CompletenessEvaluator / EquivalenceEvaluator.
/// Serialized as JSON string per MS Pattern C for round-trip safety.
/// </summary>
public sealed class GroundTruthContext : EvaluationContext
{
    public const string ContextName = "Ground Truth";

    /// <summary>The expected (ground truth) output text.</summary>
    public string Expected { get; }

    public GroundTruthContext(string expected)
        : base(ContextName, JsonSerializer.Serialize(new { expected }))
    {
        Expected = expected;
    }
}

/// <summary>
/// Chain-of-thought reasoning text for reasoning-aware evaluators.
/// Unique to HPD — no other framework exposes raw reasoning tokens.
/// Used by ReasoningCoherenceEvaluator and ReasoningGroundednessEvaluator.
/// </summary>
public sealed class ReasoningContext : EvaluationContext
{
    public const string ContextName = "Reasoning";

    /// <summary>Full aggregated reasoning/thinking token text for this turn.</summary>
    public string Reasoning { get; }

    public ReasoningContext(string reasoning)
        : base(ContextName, JsonSerializer.Serialize(new { reasoning }))
    {
        Reasoning = reasoning;
    }
}

/// <summary>
/// Serialized TurnTrace span tree for span-based behavioral evaluators.
/// Used by HasMatchingSpanEvaluator.
/// </summary>
public sealed class TurnTraceContext : EvaluationContext
{
    public const string ContextName = "Turn Trace";

    /// <summary>The full turn span tree.</summary>
    public TurnTrace Trace { get; }

    public TurnTraceContext(TurnTrace trace)
        : base(ContextName, JsonSerializer.Serialize(trace))
    {
        Trace = trace;
    }
}

/// <summary>
/// Prior turn messages for multi-turn conversation evaluators.
/// Used by ConversationCoherenceEvaluator and MemoryAccuracyEvaluator.
/// </summary>
public sealed class ConversationHistoryContext : EvaluationContext
{
    public const string ContextName = "Conversation History";

    /// <summary>Chat messages from prior turns (does not include the current turn).</summary>
    public IReadOnlyList<ChatMessage> History { get; }

    public ConversationHistoryContext(IReadOnlyList<ChatMessage> history)
        : base(ContextName, SerializeHistory(history))
    {
        History = history;
    }

    private static string SerializeHistory(IReadOnlyList<ChatMessage> history)
    {
        var simplified = history.Select(m => new
        {
            role = m.Role.Value,
            text = m.Text
        });
        return JsonSerializer.Serialize(simplified);
    }
}

/// <summary>
/// Retrieved RAG context for grounding and hallucination evaluators.
/// Two forms: static (chunks known at construction time) and dynamic (chunks resolved
/// at evaluation time from the current TurnEvaluationContext).
/// Used by HallucinationEvaluator, ContextRelevanceEvaluator, ContextPrecisionEvaluator.
/// </summary>
public sealed class GroundingDocumentContext : EvaluationContext
{
    public const string ContextName = "Grounding Documents";

    private readonly string[]? _staticChunks;
    private readonly Func<TurnEvaluationContext, string[]>? _dynamicResolver;

    /// <summary>Static form — for offline/CI where context is known ahead of time.</summary>
    public GroundingDocumentContext(string[] chunks)
        : base(ContextName, JsonSerializer.Serialize(chunks))
    {
        _staticChunks = chunks;
    }

    /// <summary>
    /// Dynamic form — for live scoring where context comes from tool results at runtime.
    /// The delegate is invoked at evaluation time, not at context construction time.
    /// e.g.: new GroundingDocumentContext(ctx =>
    ///     ctx.Attributes.TryGetValue("retrieved_chunks", out var c)
    ///         ? (string[])c : [])
    /// </summary>
    public GroundingDocumentContext(Func<TurnEvaluationContext, string[]> getChunks)
        : base(ContextName, "[]")  // placeholder; real chunks resolved via Resolve()
    {
        _dynamicResolver = getChunks;
    }

    /// <summary>
    /// Resolves and returns the grounding chunks. For the static form, returns the
    /// pre-supplied array. For the dynamic form, invokes the delegate with the current
    /// TurnEvaluationContext.
    /// </summary>
    public string[] Resolve(TurnEvaluationContext? ctx = null)
    {
        if (_staticChunks != null)
            return _staticChunks;
        if (_dynamicResolver != null && ctx != null)
            return _dynamicResolver(ctx);
        return [];
    }

    /// <summary>True if this context requires a TurnEvaluationContext to resolve chunks.</summary>
    public bool IsDynamic => _dynamicResolver != null;
}

/// <summary>
/// Available tool definitions for tool-call quality evaluators.
/// Passed through to MS ToolCallAccuracyEvaluator / TaskAdherenceEvaluator /
/// IntentResolutionEvaluator which each define their own context type wrappers.
/// </summary>
public sealed class ToolDefinitionsContext : EvaluationContext
{
    public const string ContextName = "Tool Definitions";

    /// <summary>The tools available to the agent during this turn.</summary>
    public IReadOnlyList<AITool> Tools { get; }

    public ToolDefinitionsContext(IReadOnlyList<AITool> tools)
        : base(ContextName, SerializeTools(tools))
    {
        Tools = tools;
    }

    private static string SerializeTools(IReadOnlyList<AITool> tools)
    {
        var simplified = tools.Select(t => new
        {
            name = t.Name,
            description = t.Description
        });
        return JsonSerializer.Serialize(simplified);
    }
}
