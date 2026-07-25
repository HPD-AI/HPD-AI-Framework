using HPD.Agent.ToolHarness.Coding;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal sealed class DebugBreakpointSelectionEventFactory(
    IDebugSourcePreviewProvider previews)
{
    private const int MaximumItems = 64;
    private const int MaximumTextLength = 512;
    private readonly IDebugSourcePreviewProvider _previews =
        previews ?? throw new ArgumentNullException(nameof(previews));

    public async ValueTask<DebugBreakpointSelectionAppliedEvent> CreateAsync(
        DebugBreakpointMutationResult mutation,
        AgentWorkspace workspace,
        string toolCallId,
        string action,
        string treeId,
        string adapterId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(workspace);
        var bindings = mutation.Bindings
            .GroupBy(item => item.ClientBreakpointId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var beforeSelection = Select(mutation.Before, mutation.Kind);
        var afterSelection = Select(mutation.After, mutation.Kind);
        var before = beforeSelection
            .Take(MaximumItems)
            .Select(item => ToEventItem(item, bindings.GetValueOrDefault(item.ClientBreakpointId), workspace))
            .ToArray();
        var after = afterSelection
            .Take(MaximumItems)
            .Select(item => ToEventItem(item, bindings.GetValueOrDefault(item.ClientBreakpointId), workspace))
            .ToArray();
        var changes = Diff(before, after);
        var sourcePreviews = mutation.Kind == DebugBreakpointKind.Source
            ? await CaptureSourcePreviewsAsync(
                beforeSelection.Concat(afterSelection).ToArray(),
                workspace,
                cancellationToken).ConfigureAwait(false)
            : [];
        var itemCount = Math.Max(beforeSelection.Count, afterSelection.Count);

        return new DebugBreakpointSelectionAppliedEvent
        {
            DebugTreeId = treeId,
            DebugSessionId = mutation.DebugSessionId,
            AdapterId = adapterId,
            ToolCallId = toolCallId,
            Action = action,
            BreakpointKind = mutation.Kind,
            Before = before,
            After = after,
            Changes = changes,
            Counts = mutation.Counts,
            SourcePreviews = sourcePreviews,
            DetailsTruncated = itemCount > MaximumItems,
            TruncationReason = itemCount > MaximumItems ? "breakpoint_item_limit" : null
        };
    }

    private async ValueTask<IReadOnlyList<DebugSourcePreview>> CaptureSourcePreviewsAsync(
        IReadOnlyList<SemanticBreakpointItem> items,
        AgentWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var result = new List<DebugSourcePreview>();
        foreach (var group in items
            .Where(item => item.Path is not null && item.Line is not null)
            .GroupBy(item => item.Path!, StringComparer.Ordinal)
            .Take(8))
        {
            result.Add(await _previews.CaptureAsync(
                new DebugSourcePreviewRequest(
                    workspace,
                    group.Key,
                    group.Select(item => item.Line!.Value).ToArray()),
                cancellationToken).ConfigureAwait(false));
        }
        return result;
    }

    private static IReadOnlyList<DebugBreakpointSelectionDelta> Diff(
        IReadOnlyList<DebugBreakpointSelectionEventItem> before,
        IReadOnlyList<DebugBreakpointSelectionEventItem> after)
    {
        var old = before.ToDictionary(item => item.ClientBreakpointId, StringComparer.Ordinal);
        var current = after.ToDictionary(item => item.ClientBreakpointId, StringComparer.Ordinal);
        return old.Keys.Concat(current.Keys)
            .Distinct(StringComparer.Ordinal)
            .Select(id => new DebugBreakpointSelectionDelta(
                id,
                !old.ContainsKey(id) ? DebugBreakpointSelectionDeltaKind.Added :
                !current.ContainsKey(id) ? DebugBreakpointSelectionDeltaKind.Removed :
                old[id] == current[id] ? DebugBreakpointSelectionDeltaKind.Unchanged :
                DebugBreakpointSelectionDeltaKind.Updated))
            .ToArray();
    }

    private static IReadOnlyList<SemanticBreakpointItem> Select(
        DebugDesiredBreakpointSnapshot snapshot,
        DebugBreakpointKind kind)
        => WithOccurrenceIdentities(kind switch
        {
            DebugBreakpointKind.Source => snapshot.Source.Select(item => new SemanticBreakpointItem(
                BreakpointIdentity.Source(item), kind, item.Path, item.Line, item.Column, null,
                item.Condition, item.HitCondition, item.LogMessage)).ToArray(),
            DebugBreakpointKind.Function => snapshot.Function.Select(item => new SemanticBreakpointItem(
                BreakpointIdentity.Function(item), kind, null, null, null, item.Name,
                item.Condition, item.HitCondition, null)).ToArray(),
            DebugBreakpointKind.Exception => snapshot.Exception.Select(item => new SemanticBreakpointItem(
                BreakpointIdentity.Exception(item), kind, null, null, null, item.FilterId,
                item.Condition, null, null)).ToArray(),
            DebugBreakpointKind.Instruction => snapshot.Instruction.Select(item => new SemanticBreakpointItem(
                BreakpointIdentity.Instruction(item), kind, null, null, null, "Instruction breakpoint",
                item.Condition, item.HitCondition, null)).ToArray(),
            DebugBreakpointKind.Data => snapshot.Data.Select(item => new SemanticBreakpointItem(
                BreakpointIdentity.Data(item), kind, null, null, null, "Data breakpoint",
                item.Condition, item.HitCondition, null)).ToArray(),
            _ => []
        });

    private static IReadOnlyList<SemanticBreakpointItem> WithOccurrenceIdentities(
        IReadOnlyList<SemanticBreakpointItem> items)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        return items.Select(item =>
        {
            occurrences.TryGetValue(item.ClientBreakpointId, out var occurrence);
            occurrences[item.ClientBreakpointId] = occurrence + 1;
            return item with
            {
                ClientBreakpointId = BreakpointIdentity.Occurrence(
                    item.ClientBreakpointId,
                    occurrence)
            };
        }).ToArray();
    }

    private static DebugBreakpointSelectionEventItem ToEventItem(
        SemanticBreakpointItem item,
        DebugBreakpointBindingState? binding,
        AgentWorkspace workspace)
        => new()
        {
            ClientBreakpointId = item.ClientBreakpointId,
            Kind = item.Kind,
            DisplayPath = item.Path is null ? null : DisplayPath(workspace, item.Path),
            RequestedLine = item.Line,
            RequestedColumn = item.Column,
            ResolvedLine = binding?.ResolvedLine,
            ResolvedColumn = binding?.ResolvedColumn,
            SafeDisplayName = SafeText(item.Name),
            Condition = SafeText(item.Condition),
            HitCondition = SafeText(item.HitCondition),
            LogMessage = SafeText(item.LogMessage),
            Acknowledged = binding?.Acknowledged == true,
            Verified = binding?.Verified == true,
            SafeMessage = SafeText(binding?.Message)
        };

    private static string DisplayPath(AgentWorkspace workspace, string path)
    {
        try
        {
            var fullPath = workspace.ResolveWorkspacePath(path);
            var owner = workspace.GetOwningRoot(fullPath);
            var relative = Path.GetRelativePath(owner.Path, fullPath);
            var ambiguous = workspace.Roots.Count(root =>
                File.Exists(Path.Combine(root.Path, relative))) > 1;
            return SafeText(ambiguous ? $"@{owner.Id}/{relative}" : relative)!;
        }
        catch (AgentWorkspaceException)
        {
            return SafeText(Path.GetFileName(path)) ?? "source";
        }
    }

    private static string? SafeText(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= MaximumTextLength
                ? value
            : value[..MaximumTextLength];

    private sealed record SemanticBreakpointItem(
        string ClientBreakpointId,
        DebugBreakpointKind Kind,
        string? Path,
        long? Line,
        long? Column,
        string? Name,
        string? Condition,
        string? HitCondition,
        string? LogMessage);
}
