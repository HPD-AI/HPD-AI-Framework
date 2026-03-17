// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Collections.Concurrent;

namespace HPD.Agent.Evaluations;

/// <summary>
/// AsyncLocal mid-run instrumentation API. Tools and middleware can annotate
/// the current evaluation run from within the agent's execution.
/// Both methods are silent no-ops when called outside an active EvaluationMiddleware turn.
/// </summary>
public static class EvalContext
{
    private static readonly AsyncLocal<EvalContextData?> _current = new();

    /// <summary>
    /// Sets a named attribute on the current evaluation context.
    /// Useful for carrying runtime data (e.g. retrieved RAG chunks) to evaluators
    /// without changing the tool's return value.
    /// No-op if called outside an active evaluation context.
    /// </summary>
    public static void SetAttribute(string name, object value)
    {
        _current.Value?.Attributes[name] = value;
    }

    /// <summary>
    /// Increments a named metric on the current evaluation context.
    /// Thread-safe: safe to call from parallel tool tasks.
    /// No-op if called outside an active evaluation context.
    /// </summary>
    public static void IncrementMetric(string name, double amount)
    {
        _current.Value?.Metrics.AddOrUpdate(name, amount, (_, existing) => existing + amount);
    }

    /// <summary>
    /// Activates a new EvalContextData for the current async scope and returns it.
    /// Called by EvaluationMiddleware in BeforeMessageTurnAsync before the agentic loop starts.
    /// </summary>
    internal static EvalContextData Activate()
    {
        var data = new EvalContextData();
        _current.Value = data;
        return data;
    }

    /// <summary>
    /// Clears the current eval context. Called by EvaluationMiddleware after AfterMessageTurnAsync.
    /// </summary>
    internal static void Deactivate()
    {
        _current.Value = null;
    }
}

/// <summary>
/// Accumulator for mid-run instrumentation data. Uses ConcurrentDictionary for
/// thread-safety across parallel tool call tasks (AsyncLocal flows the reference
/// into child tasks; mutations on the shared object are visible to all tasks).
/// </summary>
internal sealed class EvalContextData
{
    public ConcurrentDictionary<string, object> Attributes { get; } = new();
    public ConcurrentDictionary<string, double> Metrics { get; } = new();
}
