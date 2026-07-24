using HPD.Agent.ToolHarness.Coding.Debugging;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.Debugging;

internal sealed class DebugTuiState
{
    public const string StateKey = "hpd.coding.debug";
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reducedEvents = new(StringComparer.Ordinal);
    public Dictionary<string, DebugBreakpointSelectionAppliedEvent> BreakpointSelections { get; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, DebugSessionTuiState> Sessions { get; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, DebugTreeTuiState> Trees { get; } =
        new(StringComparer.Ordinal);
    public string? ActiveTreeId { get; private set; }
    public long Version { get; private set; }

    public bool Apply(DebugBreakpointSelectionAppliedEvent @event)
    {
        var eventIdentity = @event.EventId ?? $"{@event.DebugTreeId}:{@event.ToolCallId}:{@event.Action}";
        if (!_seen.Add(eventIdentity)) return false;
        BreakpointSelections[EntryKey(@event)] = @event;
        return true;
    }

    public bool BeginReduce(DebugLifecycleEvent @event)
        => _reducedEvents.Add(@event.EventId);

    public DebugBreakpointSelectionAppliedEvent? Reconcile(DebugBreakpointChangedEvent changed)
    {
        foreach (var (key, selection) in BreakpointSelections.ToArray())
        {
            if (!string.Equals(selection.DebugTreeId, changed.DebugTreeId, StringComparison.Ordinal) ||
                !string.Equals(selection.DebugSessionId, changed.DebugSessionId, StringComparison.Ordinal))
                continue;
            var matches = selection.After
                .Select((item, index) => (item, index))
                .Where(candidate =>
                    candidate.item.ResolvedLine == changed.Line &&
                    candidate.item.ResolvedColumn == changed.Column &&
                    (changed.DisplayPath is null ||
                        string.Equals(
                            Path.GetFileName(candidate.item.DisplayPath),
                            changed.DisplayPath,
                            StringComparison.Ordinal)))
                .ToArray();
            if (matches.Length != 1) continue;
            var match = matches[0];
            var after = selection.After.ToList();
            if (string.Equals(changed.Reason, "removed", StringComparison.Ordinal))
                after.RemoveAt(match.index);
            else
                after[match.index] = match.item with
                {
                    Verified = changed.Verified,
                    Acknowledged = true,
                    ResolvedLine = changed.Line,
                    ResolvedColumn = changed.Column,
                    SafeMessage = changed.SafeMessage
                };
            var verified = after.Count(item => item.Verified);
            var updated = selection with
            {
                After = after,
                Counts = selection.Counts with
                {
                    Verified = verified,
                    Pending = Math.Max(0, selection.Counts.Requested - verified)
                }
            };
            BreakpointSelections[key] = updated;
            return updated;
        }
        return null;
    }

    public static string EntryKey(DebugBreakpointSelectionAppliedEvent @event)
        => $"hpd.coding.debug:breakpoints:{@event.DebugTreeId}:{@event.ToolCallId}:{@event.BreakpointKind}";

    public DebugSessionTuiState Session(string treeId, string sessionId)
    {
        var key = $"{treeId}:{sessionId}";
        if (!Sessions.TryGetValue(key, out var session))
        {
            session = new DebugSessionTuiState(treeId, sessionId);
            Sessions.Add(key, session);
        }
        return session;
    }

    public DebugTreeTuiState Tree(string treeId)
    {
        if (!Trees.TryGetValue(treeId, out var tree))
        {
            tree = new DebugTreeTuiState(treeId);
            Trees.Add(treeId, tree);
        }
        return tree;
    }

    public void Touch(string treeId)
    {
        Tree(treeId).LastChanged = ++Version;
        ActiveTreeId = Trees.Values
            .OrderByDescending(tree => Rank(tree.Status))
            .ThenByDescending(tree => tree.LastChanged)
            .Select(tree => tree.DebugTreeId)
            .FirstOrDefault();
    }

    public void Evict(string treeId)
    {
        Trees.Remove(treeId);
        foreach (var key in Sessions.Keys.Where(key =>
            key.StartsWith(treeId + ":", StringComparison.Ordinal)).ToArray())
            Sessions.Remove(key);
        TouchSelection();
    }

    private void TouchSelection()
        => ActiveTreeId = Trees.Values
            .OrderByDescending(tree => Rank(tree.Status))
            .ThenByDescending(tree => tree.LastChanged)
            .Select(tree => tree.DebugTreeId)
            .FirstOrDefault();

    private static int Rank(string status)
        => status switch
        {
            "Stopped" => 4,
            "Running" => 3,
            "Starting" => 2,
            "Terminated" or "Faulted" => 1,
            _ => 0
        };
}

internal sealed record DebugSessionTuiState(string DebugTreeId, string DebugSessionId)
{
    public string Status { get; set; } = "Starting";
    public int? StoppedThreadId { get; set; }
    public string? StopReason { get; set; }
    public long? SuspensionEpoch { get; set; }
    public DebugStopSummaryAvailableEvent? CurrentStop { get; set; }
}

internal sealed record DebugTreeTuiState(string DebugTreeId)
{
    public string? AdapterId { get; set; }
    public string Status { get; set; } = "Starting";
    public long LastChanged { get; set; }
    public DebugBreakpointCounts Breakpoints { get; set; } = new(0, 0, 0, 0);
    public int ThreadCount { get; set; }
    public int ModuleCount { get; set; }
    public int SourceCount { get; set; }
    public long DroppedOutputRecords { get; set; }
    public Queue<string> Output { get; } = new();
}
