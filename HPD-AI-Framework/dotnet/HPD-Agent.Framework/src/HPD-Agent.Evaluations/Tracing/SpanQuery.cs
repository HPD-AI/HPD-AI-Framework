// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.RegularExpressions;

namespace HPD.Agent.Evaluations.Tracing;

/// <summary>
/// Declarative, serializable query language for asserting agent behavior over a TurnTrace.
/// All conditions in a single SpanQuery combine with AND semantics.
/// Or is EXCLUSIVE — it cannot be combined with any other condition in the same object.
/// Serializes to/from JSON for use in dataset files.
/// </summary>
public sealed class SpanQuery
{
    // ── Name conditions ───────────────────────────────────────────────────────

    public string? NameEquals { get; init; }
    public string? NameContains { get; init; }
    public string? NameMatchesRegex { get; init; }

    // ── Attribute conditions ──────────────────────────────────────────────────

    public IDictionary<string, string>? HasAttributes { get; init; }
    public IReadOnlyList<string>? HasAttributeKeys { get; init; }

    // ── Timing conditions ─────────────────────────────────────────────────────

    public TimeSpan? MinDuration { get; init; }
    public TimeSpan? MaxDuration { get; init; }

    // ── Count conditions ──────────────────────────────────────────────────────

    public int? MinChildCount { get; init; }
    public int? MaxChildCount { get; init; }

    // ── Child conditions ──────────────────────────────────────────────────────

    public SpanQuery? SomeChildMatches { get; init; }
    public SpanQuery? AllChildrenMatch { get; init; }
    public SpanQuery? NoChildMatches { get; init; }

    // ── Descendant conditions ─────────────────────────────────────────────────

    public SpanQuery? SomeDescendantMatches { get; init; }
    public SpanQuery? AllDescendantsMatch { get; init; }
    public SpanQuery? NoDescendantMatches { get; init; }

    /// <summary>Prune subtrees matching this query during DFS traversal.</summary>
    public SpanQuery? StopRecursingWhen { get; init; }

    // ── Ancestor conditions ───────────────────────────────────────────────────

    public SpanQuery? SomeAncestorMatches { get; init; }
    public SpanQuery? AllAncestorsMatch { get; init; }
    public SpanQuery? NoAncestorMatches { get; init; }
    public int? MinDepth { get; init; }
    public int? MaxDepth { get; init; }

    // ── Logical combinators ───────────────────────────────────────────────────

    public SpanQuery? Not { get; init; }
    public IReadOnlyList<SpanQuery>? And { get; init; }

    /// <summary>EXCLUSIVE: throws InvalidOperationException if combined with any other condition.</summary>
    public IReadOnlyList<SpanQuery>? Or { get; init; }

    // ── Matching ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Tests whether a ToolCallSpan (treated as a leaf span with no children) matches
    /// this query. Ancestor context is provided for ancestor conditions.
    /// Evaluation order: Or exclusivity → Not/And → name → attributes → timing →
    /// children → descendants → ancestors.
    /// </summary>
    public bool Matches(ToolCallSpan span, IReadOnlyList<ToolCallSpan>? ancestorPath = null)
        => MatchesSpanAdapter(new ToolCallSpanAdapter(span, ancestorPath ?? []));

    /// <summary>
    /// Tests whether any ToolCallSpan in the TurnTrace matches this query.
    /// Iterates all spans across all iterations, providing the turn-level ancestry.
    /// </summary>
    public bool MatchesAny(TurnTrace trace)
    {
        foreach (var iteration in trace.Iterations)
        foreach (var span in iteration.ToolCalls)
        {
            if (Matches(span))
                return true;
        }
        return false;
    }

    // ── Internal matching infrastructure ─────────────────────────────────────

    private bool MatchesSpanAdapter(ToolCallSpanAdapter adapter)
    {
        // Step 1 — Or exclusivity check
        if (Or != null && HasAnyOtherConditionBesideOr())
            throw new InvalidOperationException(
                "SpanQuery.Or is exclusive and cannot be combined with other conditions.");

        // Step 2 — Or (evaluated first as an early-exit)
        if (Or != null)
            return Or.Any(q => q.MatchesSpanAdapter(adapter));

        // Step 2 — Not, And
        if (Not != null && Not.MatchesSpanAdapter(adapter))
            return false;
        if (And != null && !And.All(q => q.MatchesSpanAdapter(adapter)))
            return false;

        // Step 3 — Name conditions
        if (NameEquals != null && adapter.Name != NameEquals)
            return false;
        if (NameContains != null && !adapter.Name.Contains(NameContains, StringComparison.Ordinal))
            return false;
        if (NameMatchesRegex != null && !Regex.IsMatch(adapter.Name, NameMatchesRegex))
            return false;

        // Step 4 — Attribute conditions
        if (HasAttributeKeys != null)
        {
            foreach (var key in HasAttributeKeys)
                if (!adapter.HasAttributeKey(key))
                    return false;
        }
        if (HasAttributes != null)
        {
            foreach (var kvp in HasAttributes)
                if (!adapter.HasAttribute(kvp.Key, kvp.Value))
                    return false;
        }

        // Step 5 — Timing conditions
        if (MinDuration.HasValue && adapter.Duration < MinDuration.Value)
            return false;
        if (MaxDuration.HasValue && adapter.Duration > MaxDuration.Value)
            return false;

        // Step 6 — Child conditions (ToolCallSpans are leaves — no children)
        var children = adapter.Children;
        if (MinChildCount.HasValue && children.Count < MinChildCount.Value)
            return false;
        if (MaxChildCount.HasValue && children.Count > MaxChildCount.Value)
            return false;
        if (SomeChildMatches != null && !children.Any(c => SomeChildMatches.MatchesSpanAdapter(c)))
            return false;
        if (AllChildrenMatch != null && children.Count > 0 && !children.All(c => AllChildrenMatch.MatchesSpanAdapter(c)))
            return false;
        if (NoChildMatches != null && children.Any(c => NoChildMatches.MatchesSpanAdapter(c)))
            return false;

        // Step 7 — Descendant conditions (DFS with StopRecursingWhen pruning)
        if (SomeDescendantMatches != null || AllDescendantsMatch != null || NoDescendantMatches != null)
        {
            var descendants = CollectDescendants(adapter, StopRecursingWhen);
            if (SomeDescendantMatches != null && !descendants.Any(d => SomeDescendantMatches.MatchesSpanAdapter(d)))
                return false;
            if (AllDescendantsMatch != null && descendants.Count > 0 && !descendants.All(d => AllDescendantsMatch.MatchesSpanAdapter(d)))
                return false;
            if (NoDescendantMatches != null && descendants.Any(d => NoDescendantMatches.MatchesSpanAdapter(d)))
                return false;
        }

        // Step 8 — Ancestor conditions
        var ancestors = adapter.Ancestors;
        if (MinDepth.HasValue && ancestors.Count < MinDepth.Value)
            return false;
        if (MaxDepth.HasValue && ancestors.Count > MaxDepth.Value)
            return false;
        if (SomeAncestorMatches != null && !ancestors.Any(a => SomeAncestorMatches.MatchesSpanAdapter(a)))
            return false;
        if (AllAncestorsMatch != null && ancestors.Count > 0 && !ancestors.All(a => AllAncestorsMatch.MatchesSpanAdapter(a)))
            return false;
        if (NoAncestorMatches != null && ancestors.Any(a => NoAncestorMatches.MatchesSpanAdapter(a)))
            return false;

        return true;
    }

    private bool HasAnyOtherConditionBesideOr() =>
        Not != null || And != null ||
        NameEquals != null || NameContains != null || NameMatchesRegex != null ||
        HasAttributes != null || HasAttributeKeys != null ||
        MinDuration.HasValue || MaxDuration.HasValue ||
        MinChildCount.HasValue || MaxChildCount.HasValue ||
        SomeChildMatches != null || AllChildrenMatch != null || NoChildMatches != null ||
        SomeDescendantMatches != null || AllDescendantsMatch != null || NoDescendantMatches != null ||
        StopRecursingWhen != null ||
        SomeAncestorMatches != null || AllAncestorsMatch != null || NoAncestorMatches != null ||
        MinDepth.HasValue || MaxDepth.HasValue;

    private static List<ToolCallSpanAdapter> CollectDescendants(
        ToolCallSpanAdapter node, SpanQuery? stopWhen)
    {
        var result = new List<ToolCallSpanAdapter>();
        foreach (var child in node.Children)
        {
            if (stopWhen?.MatchesSpanAdapter(child) == true)
                continue;
            result.Add(child);
            result.AddRange(CollectDescendants(child, stopWhen));
        }
        return result;
    }

    // ── Span adapter (thin wrapper giving uniform shape to ToolCallSpan) ──────

    /// <summary>
    /// Wraps a ToolCallSpan to present the uniform interface used by query matching.
    /// ToolCallSpans are leaves — they have no children. The span's fields are mapped
    /// to query-matchable properties (Name, Duration, Attributes via HasAttributeKey/Value).
    /// </summary>
    private sealed class ToolCallSpanAdapter
    {
        private readonly ToolCallSpan _span;
        private readonly IReadOnlyList<ToolCallSpan> _ancestorPath;

        public ToolCallSpanAdapter(ToolCallSpan span, IReadOnlyList<ToolCallSpan> ancestorPath)
        {
            _span = span;
            _ancestorPath = ancestorPath;
        }

        public string Name => _span.Name;
        public TimeSpan Duration => _span.Duration;
        public IReadOnlyList<ToolCallSpanAdapter> Children => [];
        public IReadOnlyList<ToolCallSpanAdapter> Ancestors =>
            _ancestorPath.Select(a => new ToolCallSpanAdapter(a, [])).ToList();

        public bool HasAttributeKey(string key) => key switch
        {
            "callId" => true,
            "toolkitName" => _span.ToolkitName != null,
            "wasPermissionDenied" => true,
            _ => false,
        };

        public bool HasAttribute(string key, string value) => key switch
        {
            "callId" => _span.CallId == value,
            "toolkitName" => _span.ToolkitName == value,
            "wasPermissionDenied" => bool.TryParse(value, out var b) && b == _span.WasPermissionDenied,
            _ => false,
        };
    }
}
