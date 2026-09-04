using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Agent.ToolHarness.Coding.TUI.SourcePresentation;
using HPD.TUI.Core;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.Debugging;

internal sealed class DebugBreakpointCellView : HPD.TUI.Core.Component
{
    private readonly DebugBreakpointCell _cell;
    private readonly IComponent _content;

    public DebugBreakpointCellView(DebugBreakpointCell cell, CodingHarnessTuiTheme theme)
    {
        _cell = cell;
        _content = cell.SourcePreviews.Count > 0
            ? new AnnotatedSourceView(CreateSourceDocument(), theme)
            : new DebugTextRowsView(CreateRows(), theme);
    }

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
        => _content.Measure(in context, constraints);

    public override void Render(in RenderContext context, ref DisplayListBuilder output)
        => _content.Render(in context, ref output);

    public override bool HandleInput(in TuiInputEvent input) => false;

    private string[] CreateRows()
    {
        return _cell.After.Select(item =>
            $"{Marker(item)} {DisplayLabel(item)} · {Status(item)}")
            .Concat(_cell.Before
                .Where(item => _cell.Changes.Any(change =>
                    change.ClientBreakpointId == item.ClientBreakpointId &&
                    change.Kind == DebugBreakpointSelectionDeltaKind.Removed))
                .Select(item => $"− {DisplayLabel(item)} · removed"))
            .Append(_cell.Truncated ? "… breakpoint details truncated" : null)
            .Where(static row => row is not null)
            .Select(static row => row!)
            .ToArray();
    }

    private static string DisplayLabel(DebugBreakpointSelectionEventItem item)
        => item.SafeDisplayName is { Length: > 0 } name
            ? $"{KindLabel(item.Kind)}: {name}"
            : KindLabel(item.Kind);

    private static string KindLabel(DebugBreakpointKind kind)
        => kind switch
        {
            DebugBreakpointKind.Source => "Source",
            DebugBreakpointKind.Function => "Function",
            DebugBreakpointKind.Exception => "Exception",
            DebugBreakpointKind.Data => "Data",
            DebugBreakpointKind.Instruction => "Instruction",
            _ => "Breakpoint"
        };

    private AnnotatedSourceDocument CreateSourceDocument()
    {
        var removedIds = _cell.Changes
            .Where(change => change.Kind == DebugBreakpointSelectionDeltaKind.Removed)
            .Select(change => change.ClientBreakpointId)
            .ToHashSet(StringComparer.Ordinal);
        var items = _cell.After.Concat(_cell.Before.Where(item =>
                removedIds.Contains(item.ClientBreakpointId)))
            .GroupBy(item => (
                item.DisplayPath,
                item.ResolvedLine ?? item.RequestedLine))
            .ToDictionary(group => group.Key, group => group.Last());
        var changes = _cell.Changes.ToDictionary(
            change => change.ClientBreakpointId,
            change => change.Kind,
            StringComparer.Ordinal);
        return new AnnotatedSourceDocument(
            _cell.SourcePreviews.Count == 1 ? _cell.SourcePreviews[0].DisplayPath : null,
            _cell.SourcePreviews.Count == 1 ? _cell.SourcePreviews[0].Language : null,
            _cell.SourcePreviews.SelectMany(preview => preview.Hunks.Select(hunk =>
                new AnnotatedSourceHunk(hunk.Lines.Select((text, offset) =>
                {
                    var line = hunk.StartLine + offset;
                    items.TryGetValue((preview.DisplayPath, (long?)line), out var item);
                    if (item is null)
                        return new AnnotatedSourceLine(line, text, []);
                    changes.TryGetValue(item.ClientBreakpointId, out var change);
                    var marker = change == DebugBreakpointSelectionDeltaKind.Removed
                        ? "−"
                        : Marker(item);
                    return new AnnotatedSourceLine(
                        line,
                        text,
                        [new SourceAnnotation(marker, Tone(item, change), Status(item))],
                        Trailing(item, change),
                        item.Verified ? SourceLineEmphasis.Subtle : SourceLineEmphasis.Warning);
                }).ToArray()))).ToArray(),
            _cell.Truncated || _cell.SourcePreviews.Any(preview => preview.Truncated),
            _cell.Truncated ? "breakpoint details truncated" : null);
    }

    private string Marker(DebugBreakpointSelectionEventItem item)
        => _cell.HitBreakpointClientIds.Contains(item.ClientBreakpointId) ? "●" :
            item.Condition is not null ? "◆" :
            item.HitCondition is not null ? "◈" :
            item.LogMessage is not null ? "◇" :
            item.Verified ? "◆" :
            item.Acknowledged ? "○" : "!";

    private static SourceAnnotationTone Tone(
        DebugBreakpointSelectionEventItem item,
        DebugBreakpointSelectionDeltaKind change)
        => change == DebugBreakpointSelectionDeltaKind.Removed ? SourceAnnotationTone.Removed :
            item.Verified ? SourceAnnotationTone.Success :
            item.Acknowledged ? SourceAnnotationTone.Warning :
            SourceAnnotationTone.Error;

    private static string Status(DebugBreakpointSelectionEventItem item)
        => item.Verified ? "resolved" :
            item.Acknowledged ? item.SafeMessage ?? "pending" :
            item.SafeMessage ?? "not acknowledged";

    private static string? Trailing(
        DebugBreakpointSelectionEventItem item,
        DebugBreakpointSelectionDeltaKind change)
    {
        var parts = new List<string>();
        if (change == DebugBreakpointSelectionDeltaKind.Added) parts.Add("added");
        if (change == DebugBreakpointSelectionDeltaKind.Removed) parts.Add("removed");
        if (item.Condition is not null) parts.Add($"when {item.Condition}");
        if (item.HitCondition is not null) parts.Add($"hit {item.HitCondition}");
        if (item.LogMessage is not null) parts.Add($"log {item.LogMessage}");
        if (item.ResolvedLine is { } resolved &&
            item.RequestedLine is { } requested &&
            resolved != requested)
            parts.Add($"resolved from requested line {requested}");
        if (!item.Verified) parts.Add(Status(item));
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }
}
