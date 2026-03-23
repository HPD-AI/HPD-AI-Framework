// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Collections.Concurrent;

namespace HPD.Agent.Evaluations.Tracing;

/// <summary>
/// Buffers agent events during a single message turn so EvaluationMiddleware can
/// reconstruct timing and permission data when building TurnTrace.
///
/// Activated in BeforeMessageTurnAsync and consumed in AfterMessageTurnAsync.
/// EvaluationMiddleware populates this via its IAgentEventObserver.OnEventAsync
/// implementation running concurrently with the turn.
///
/// Thread-safe: concurrent tool calls may write to ToolCallStarts simultaneously.
/// </summary>
internal sealed class TurnEventBuffer
{
    // ── Message-turn level ────────────────────────────────────────────────────

    public DateTimeOffset TurnStartedAt { get; private set; }
    public TimeSpan TurnDuration { get; private set; }
    public string MessageTurnId { get; private set; } = string.Empty;

    // ── Per-iteration boundaries ───────────────────────────────────────────────

    // Key = iteration number
    private readonly ConcurrentDictionary<int, DateTimeOffset> _iterationStartTimes = new();
    private readonly ConcurrentDictionary<int, DateTimeOffset> _iterationEndTimes = new();

    // ── Tool call timing ──────────────────────────────────────────────────────

    // Key = callId
    private readonly ConcurrentDictionary<string, (string Name, string? ToolkitName, DateTimeOffset StartedAt)> _toolCallStarts = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _toolCallEnds = new();

    // ── Permission denials ─────────────────────────────────────────────────────

    // Set of callIds that were permission-denied this turn
    private readonly ConcurrentDictionary<string, bool> _deniedCallIds = new();

    // ── Mutation methods (called from OnEventAsync) ───────────────────────────

    public void RecordTurnStarted(string messageTurnId, DateTimeOffset at)
    {
        MessageTurnId = messageTurnId;
        TurnStartedAt = at;
    }

    public void RecordTurnFinished(TimeSpan duration) =>
        TurnDuration = duration;

    public void RecordIterationStarted(int iteration, DateTimeOffset at) =>
        _iterationStartTimes[iteration] = at;

    public void RecordIterationFinished(int iteration, DateTimeOffset at) =>
        _iterationEndTimes[iteration] = at;

    public void RecordToolCallStarted(string callId, string name, string? toolkitName, DateTimeOffset at) =>
        _toolCallStarts[callId] = (name, toolkitName, at);

    public void RecordToolCallEnded(string callId, DateTimeOffset at) =>
        _toolCallEnds[callId] = at;

    public void RecordPermissionDenied(string callId) =>
        _deniedCallIds[callId] = true;

    // ── Query methods (called from AfterMessageTurnAsync) ────────────────────

    public TimeSpan GetIterationDuration(int iteration)
    {
        if (_iterationStartTimes.TryGetValue(iteration, out var start) &&
            _iterationEndTimes.TryGetValue(iteration, out var end))
            return end - start;
        return TimeSpan.Zero;
    }

    public TimeSpan GetToolCallDuration(string callId)
    {
        if (_toolCallStarts.TryGetValue(callId, out var startEntry) &&
            _toolCallEnds.TryGetValue(callId, out var end))
            return end - startEntry.StartedAt;
        return TimeSpan.Zero;
    }

    public (string Name, string? ToolkitName)? GetToolCallInfo(string callId)
    {
        if (_toolCallStarts.TryGetValue(callId, out var entry))
            return (entry.Name, entry.ToolkitName);
        return null;
    }

    public bool WasPermissionDenied(string callId) =>
        _deniedCallIds.ContainsKey(callId);

    public IReadOnlySet<string> AllStartedCallIds =>
        _toolCallStarts.Keys.ToHashSet();

    /// <summary>
    /// Returns all iteration numbers recorded in the buffer, in ascending order.
    /// An iteration number is present if at least a start time was recorded for it.
    /// </summary>
    public IReadOnlyList<int> GetIterationNumbers()
        => _iterationStartTimes.Keys.OrderBy(k => k).ToList();

    /// <summary>
    /// Returns the callIds of tool calls whose start timestamp falls within the
    /// iteration window [iterationStart, iterationEnd). Tool calls started exactly
    /// at iterationEnd are excluded. If the iteration end is unknown, all tool calls
    /// started at or after iterationStart are attributed to this iteration.
    /// </summary>
    public IReadOnlyList<string> GetToolCallIdsForIteration(int iteration)
    {
        if (!_iterationStartTimes.TryGetValue(iteration, out var iterStart))
            return [];

        _iterationEndTimes.TryGetValue(iteration, out var iterEnd);
        bool hasEnd = _iterationEndTimes.ContainsKey(iteration);

        return _toolCallStarts
            .Where(kv =>
            {
                var callStart = kv.Value.StartedAt;
                if (callStart < iterStart) return false;
                if (hasEnd && callStart >= iterEnd) return false;
                return true;
            })
            .Select(kv => kv.Key)
            .ToList();
    }
}
