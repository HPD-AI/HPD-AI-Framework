using HPD.Agent.ToolHarness.Coding.Debugging;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.Debugging;

internal sealed class DebugTuiState
{
    public const string StateKey = "hpd.coding.debug";
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reducedEvents = new(StringComparer.Ordinal);
    private readonly HashSet<string> _breakpointProjectionEvents = new(StringComparer.Ordinal);
    public Dictionary<string, DebugBreakpointPresentationState> BreakpointSelections { get; } =
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
        BreakpointSelections[EntryKey(@event)] = DebugBreakpointPresentationState.Create(@event);
        return true;
    }

    public bool BeginReduce(DebugLifecycleEvent @event)
        => _reducedEvents.Add(@event.EventId);

    public bool BeginBreakpointProjection(DebugLifecycleEvent @event)
        => _breakpointProjectionEvents.Add(@event.EventId);

    public DebugBreakpointPresentationState? Reconcile(DebugBreakpointChangedEvent changed)
    {
        foreach (var selection in BreakpointSelections.Values)
        {
            if (!string.Equals(selection.DebugTreeId, changed.DebugTreeId, StringComparison.Ordinal) ||
                !string.Equals(selection.DebugSessionId, changed.DebugSessionId, StringComparison.Ordinal))
                continue;
            if (!selection.Items.TryGetValue(changed.ClientBreakpointId, out var item))
                continue;
            if (changed.Change == DebugBreakpointChangeKind.Removed)
                selection.Items.Remove(changed.ClientBreakpointId);
            else
            {
                item.Acknowledged = changed.Acknowledged;
                item.Verified = changed.Verified;
                item.ResolvedLine = changed.ResolvedLine;
                item.ResolvedColumn = changed.ResolvedColumn;
                item.SafeMessage = changed.SafeMessage;
            }
            selection.HasEvolved = true;
            return selection;
        }
        return null;
    }

    public IReadOnlyList<DebugBreakpointPresentationState> ObserveHits(
        DebugPrimaryStopAvailableEvent stopped)
    {
        var changed = new List<DebugBreakpointPresentationState>();
        var selections = BreakpointSelections.Values.Where(selection =>
                string.Equals(selection.DebugTreeId, stopped.DebugTreeId, StringComparison.Ordinal) &&
                string.Equals(selection.DebugSessionId, stopped.DebugSessionId, StringComparison.Ordinal))
            .OrderBy(selection => selection.EntryKey, StringComparer.Ordinal)
            .ToArray();
        var unknownOwner = stopped.HitBreakpointIdentityUnknown
            ? selections.FirstOrDefault(selection =>
                  stopped.SourcePreview is not null &&
                  selection.Kind == DebugBreakpointKind.Source)
              ?? selections.FirstOrDefault()
            : null;
        foreach (var selection in selections)
        {
            var touched = false;
            foreach (var clientId in stopped.HitBreakpointClientIds)
            {
                if (!selection.Items.TryGetValue(clientId, out var item)) continue;
                item.HitCount++;
                touched = true;
            }
            if (ReferenceEquals(selection, unknownOwner))
            {
                selection.UnknownHitCount++;
                touched = true;
            }
            if (touched) changed.Add(selection);
            if (touched) selection.HasEvolved = true;
        }
        return changed;
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
        foreach (var key in BreakpointSelections
                     .Where(pair => string.Equals(
                         pair.Value.DebugTreeId,
                         treeId,
                         StringComparison.Ordinal))
                     .Select(pair => pair.Key)
                     .ToArray())
            BreakpointSelections.Remove(key);
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
    public DebugPrimaryStopAvailableEvent? CurrentStop { get; set; }
}

internal sealed class DebugBreakpointPresentationState
{
    public required string DebugTreeId { get; init; }
    public required string DebugSessionId { get; init; }
    public required string ToolCallId { get; init; }
    public required string EntryKey { get; init; }
    public required DebugBreakpointKind Kind { get; init; }
    public required IReadOnlyList<DebugBreakpointSelectionEventItem> Before { get; init; }
    public required IReadOnlyList<DebugBreakpointSelectionDelta> Changes { get; init; }
    public Dictionary<string, DebugBreakpointPresentationItem> Items { get; } =
        new(StringComparer.Ordinal);
    public IReadOnlyList<DebugSourcePreview> SourcePreviews { get; init; } = [];
    public bool DetailsTruncated { get; init; }
    public int UnknownHitCount { get; set; }
    public bool HasEvolved { get; set; }

    public DebugBreakpointCounts Counts
    {
        get
        {
            var acknowledged = Items.Values.Count(item => item.Acknowledged);
            var verified = Items.Values.Count(item => item.Verified);
            return new(Items.Count, acknowledged, verified, Math.Max(0, Items.Count - verified),
                Items.Values.Count(item => item.HitCount > 0), UnknownHitCount);
        }
    }

    public IReadOnlyList<DebugBreakpointSelectionEventItem> After
        => Items.Values.Select(item => item.ToEventItem()).ToArray();

    public static DebugBreakpointPresentationState Create(DebugBreakpointSelectionAppliedEvent evt)
    {
        var state = new DebugBreakpointPresentationState
        {
            DebugTreeId = evt.DebugTreeId,
            DebugSessionId = evt.DebugSessionId,
            ToolCallId = evt.ToolCallId,
            EntryKey = DebugTuiState.EntryKey(evt),
            Kind = evt.BreakpointKind,
            Before = evt.Before,
            Changes = evt.Changes,
            SourcePreviews = evt.SourcePreviews,
            DetailsTruncated = evt.DetailsTruncated
        };
        foreach (var item in evt.After)
            state.Items[item.ClientBreakpointId] = DebugBreakpointPresentationItem.Create(item);
        return state;
    }
}

internal sealed class DebugBreakpointPresentationItem
{
    public required string ClientBreakpointId { get; init; }
    public required DebugBreakpointKind Kind { get; init; }
    public string? DisplayPath { get; init; }
    public long? RequestedLine { get; init; }
    public long? RequestedColumn { get; init; }
    public long? ResolvedLine { get; set; }
    public long? ResolvedColumn { get; set; }
    public string? SafeDisplayName { get; init; }
    public string? Condition { get; init; }
    public string? HitCondition { get; init; }
    public string? LogMessage { get; init; }
    public bool Acknowledged { get; set; }
    public bool Verified { get; set; }
    public string? SafeMessage { get; set; }
    public int HitCount { get; set; }

    public static DebugBreakpointPresentationItem Create(DebugBreakpointSelectionEventItem item)
        => new()
        {
            ClientBreakpointId = item.ClientBreakpointId,
            Kind = item.Kind,
            DisplayPath = item.DisplayPath,
            RequestedLine = item.RequestedLine,
            RequestedColumn = item.RequestedColumn,
            ResolvedLine = item.ResolvedLine,
            ResolvedColumn = item.ResolvedColumn,
            SafeDisplayName = item.SafeDisplayName,
            Condition = item.Condition,
            HitCondition = item.HitCondition,
            LogMessage = item.LogMessage,
            Acknowledged = item.Acknowledged,
            Verified = item.Verified,
            SafeMessage = item.SafeMessage
        };

    public DebugBreakpointSelectionEventItem ToEventItem()
        => new()
        {
            ClientBreakpointId = ClientBreakpointId,
            Kind = Kind,
            DisplayPath = DisplayPath,
            RequestedLine = RequestedLine,
            RequestedColumn = RequestedColumn,
            ResolvedLine = ResolvedLine,
            ResolvedColumn = ResolvedColumn,
            SafeDisplayName = SafeDisplayName,
            Condition = Condition,
            HitCondition = HitCondition,
            LogMessage = LogMessage,
            Acknowledged = Acknowledged,
            Verified = Verified,
            SafeMessage = SafeMessage
        };
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
