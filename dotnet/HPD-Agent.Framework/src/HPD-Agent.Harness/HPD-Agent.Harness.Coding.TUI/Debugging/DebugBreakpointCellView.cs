using HPD.Agent.ToolHarness.Coding.TUI.SourcePresentation;
using HPD.TUI.Core;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.Debugging;

internal sealed class DebugBreakpointCellView : IComponent
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

    public Measurement Measure(in RenderContext context, int maxWidth)
        => _content.Measure(context, maxWidth);

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
        => _content.Render(context, maxWidth, ref output);

    public bool HandleInput(in TuiInputEvent input) => false;

    private string[] CreateRows()
    {
        return _cell.After.Select(item =>
            $"{Marker(item)} {item.SafeDisplayName ?? item.Kind.ToString()} · {Status(item)}")
            .Concat(_cell.Before
                .Where(item => _cell.Changes.Any(change =>
                    change.ClientBreakpointId == item.ClientBreakpointId &&
                    change.Kind == DebugBreakpointSelectionDeltaKind.Removed))
                .Select(item => $"− {item.SafeDisplayName ?? item.Kind.ToString()} · removed"))
            .Append(_cell.Truncated ? "… breakpoint details truncated" : null)
            .Where(static row => row is not null)
            .Select(static row => row!)
            .ToArray();
    }

    private AnnotatedSourceDocument CreateSourceDocument()
    {
        var items = _cell.Before.Concat(_cell.After)
            .GroupBy(item => (item.DisplayPath, item.RequestedLine))
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

    private static string Marker(DebugBreakpointSelectionEventItem item)
        => item.Condition is not null ? "◆" :
            item.HitCondition is not null ? "◈" :
            item.LogMessage is not null ? "◇" :
            item.Verified ? "●" :
            item.Acknowledged ? "○" : "!";

    private static SourceAnnotationTone Tone(
        DebugBreakpointSelectionEventItem item,
        DebugBreakpointSelectionDeltaKind change)
        => change == DebugBreakpointSelectionDeltaKind.Removed ? SourceAnnotationTone.Removed :
            item.Verified ? SourceAnnotationTone.Success :
            item.Acknowledged ? SourceAnnotationTone.Warning :
            SourceAnnotationTone.Error;

    private static string Status(DebugBreakpointSelectionEventItem item)
        => item.Verified ? "verified" :
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
        if (!item.Verified) parts.Add(Status(item));
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }
}
