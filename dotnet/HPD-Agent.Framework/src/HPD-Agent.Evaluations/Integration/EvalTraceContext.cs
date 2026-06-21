// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using HPD.Agent.Evaluations.Storage;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Evaluations.Integration;

/// <summary>
/// Eval-scoped trace collector for judge calls. Uses AsyncLocal so concurrent
/// evaluator tasks keep independent trace buffers without touching thread state.
/// </summary>
internal static class EvalTraceContext
{
    private static readonly AsyncLocal<EvalTraceData?> Current = new();

    internal static EvalTraceScope Activate(string evaluatorName)
    {
        var previous = Current.Value;
        var data = new EvalTraceData(evaluatorName);
        Current.Value = data;
        return new EvalTraceScope(previous, data);
    }

    internal static void AddJudgeCall(JudgeCallRecord record)
    {
        Current.Value?.JudgeCalls.Enqueue(record);
    }

    internal static void AddCapturedJudgePrompt(IReadOnlyList<ChatMessage> prompt)
    {
        Current.Value?.CapturedJudgePrompts.Enqueue(prompt);
    }

    internal static bool TryGetLatestCapturedJudgePrompt(out IReadOnlyList<ChatMessage> prompt)
    {
        prompt = [];
        var current = Current.Value;
        if (current is null)
            return false;

        var found = false;
        while (current.CapturedJudgePrompts.TryDequeue(out var captured))
        {
            prompt = captured;
            found = true;
        }

        return found;
    }

    internal static string? CurrentEvaluatorName => Current.Value?.EvaluatorName;

    internal sealed class EvalTraceScope : IDisposable
    {
        private readonly EvalTraceData? _previous;
        private readonly EvalTraceData _data;
        private bool _disposed;

        internal EvalTraceScope(EvalTraceData? previous, EvalTraceData data)
        {
            _previous = previous;
            _data = data;
        }

        internal IReadOnlyList<JudgeCallRecord> Snapshot() => _data.JudgeCalls.ToArray();

        public void Dispose()
        {
            if (_disposed)
                return;

            Current.Value = _previous;
            _disposed = true;
        }
    }

    internal sealed class EvalTraceData(string evaluatorName)
    {
        public string EvaluatorName { get; } = evaluatorName;
        public ConcurrentQueue<JudgeCallRecord> JudgeCalls { get; } = new();
        public ConcurrentQueue<IReadOnlyList<ChatMessage>> CapturedJudgePrompts { get; } = new();
    }
}
