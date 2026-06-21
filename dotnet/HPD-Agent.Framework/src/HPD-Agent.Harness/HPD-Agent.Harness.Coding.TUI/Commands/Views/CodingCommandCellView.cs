using System.Text;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands.Views;

internal sealed class CodingCommandCellView : IComponent
{
    private readonly CodingCommandCell _cell;
    private int? _cachedOutputWidth;
    private DisplayOutputSnapshot? _cachedDisplaySnapshot;

    public CodingCommandCellView(CodingCommandCell cell)
    {
        _cell = cell ?? throw new ArgumentNullException(nameof(cell));
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        var outputWidth = Math.Max(0, maxWidth - 2);
        var snapshot = GetDisplaySnapshot(outputWidth);
        var rows = Math.Max(1, snapshot.Lines.Count);
        if (snapshot.OmittedLineCount > 0)
        {
            rows++;
        }

        if (!string.IsNullOrWhiteSpace(_cell.Summary))
        {
            rows++;
        }

        return new Measurement(1, Math.Min(maxWidth, 80), rows);
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        var outputWidth = Math.Max(0, maxWidth - 2);
        var snapshot = GetDisplaySnapshot(outputWidth);
        var wroteLine = false;

        for (var i = 0; i < snapshot.Lines.Count; i++)
        {
            if (snapshot.OmittedLineCount > 0 && i == snapshot.HeadLineCount)
            {
                if (wroteLine)
                {
                    output.WriteLineBreak();
                }

                WriteOutputPrefix(wroteLine, context.Theme.Border, ref output);
                output.Write($"... +{snapshot.OmittedLineCount} lines".AsSpan(), context.Theme.Border);
                wroteLine = true;
            }

            var line = snapshot.Lines[i];
            if (wroteLine)
            {
                output.WriteLineBreak();
            }

            WriteOutputPrefix(wroteLine, context.Theme.Border, ref output);
            var style = line.Stream == CodingCommandOutputStream.Stderr
                ? context.Theme.Warning
                : context.Theme.Border;
            WriteClipped(line.Text, outputWidth, style, ref output);
            wroteLine = true;
        }

        if (!wroteLine)
        {
            output.Write("└ ".AsSpan(), context.Theme.Border);
            output.Write("no output observed".AsSpan(), context.Theme.Border);
            wroteLine = true;
        }

        if (!string.IsNullOrWhiteSpace(_cell.Summary))
        {
            output.WriteLineBreak();
            output.Write("  ".AsSpan(), context.Theme.Border);
            output.Write(_cell.Summary.AsSpan(), StyleForState(context));
        }
    }

    private static IReadOnlyList<CodingCommandOutputLine> WrapOutput(
        CodingCommandCell cell,
        int outputWidth)
    {
        if (outputWidth <= 0)
        {
            return cell.Output;
        }

        var rows = new List<CodingCommandOutputLine>();
        foreach (var line in cell.Output)
        {
            AddWrappedRows(line, outputWidth, rows);
        }

        return rows;
    }

    private static void AddWrappedRows(
        CodingCommandOutputLine line,
        int width,
        List<CodingCommandOutputLine> rows)
    {
        if (width <= 0 || string.IsNullOrEmpty(line.Text))
        {
            rows.Add(line);
            return;
        }

        var row = new StringBuilder();
        var rowWidth = 0;
        foreach (var rune in line.Text.EnumerateRunes())
        {
            var runeText = rune.ToString();
            var runeWidth = Math.Max(0, HPD.TUI.Utilities.UnicodeWidth.GetWidth(rune));
            if (row.Length > 0 && rowWidth + runeWidth > width)
            {
                rows.Add(new CodingCommandOutputLine(line.Stream, row.ToString()));
                row.Clear();
                rowWidth = 0;
            }

            row.Append(runeText);
            rowWidth += runeWidth;
        }

        rows.Add(new CodingCommandOutputLine(line.Stream, row.ToString()));
    }

    private DisplayOutputSnapshot GetDisplaySnapshot(int outputWidth)
    {
        if (_cachedOutputWidth == outputWidth && _cachedDisplaySnapshot is not null)
        {
            return _cachedDisplaySnapshot;
        }

        _cachedOutputWidth = outputWidth;
        _cachedDisplaySnapshot = CreateDisplaySnapshot(_cell, outputWidth);
        return _cachedDisplaySnapshot;
    }

    private static DisplayOutputSnapshot CreateDisplaySnapshot(CodingCommandCell cell, int outputWidth)
    {
        const int headRows = 2;
        const int tailRows = 2;
        const int maxVisibleRows = 5;

        var rows = WrapOutput(cell, outputWidth);
        if (rows.Count <= maxVisibleRows)
        {
            return new DisplayOutputSnapshot(
                rows,
                cell.OutputWindow.OmittedLineCount,
                rows.Count);
        }

        var head = Math.Clamp(headRows, 0, maxVisibleRows);
        var tail = Math.Clamp(tailRows, 0, Math.Max(0, maxVisibleRows - head));
        var visible = rows.Take(head)
            .Concat(rows.TakeLast(tail))
            .ToArray();
        var omitted = rows.Count - visible.Length + cell.OutputWindow.OmittedLineCount;
        return new DisplayOutputSnapshot(visible, omitted, head);
    }

    private static void WriteOutputPrefix(bool continuation, Style style, ref SegmentWriter output)
    {
        output.Write((continuation ? "  " : "└ ").AsSpan(), style);
    }

    public void HandleInput(in KeyEvent key)
    {
    }

    public void Invalidate()
    {
    }

    private Style StyleForState(in RenderContext context)
        => _cell.State switch
        {
            CodingCommandTranscriptState.Completed => context.Theme.Success,
            CodingCommandTranscriptState.Failed or CodingCommandTranscriptState.TimedOut => context.Theme.Error,
            CodingCommandTranscriptState.Cancelled => context.Theme.Warning,
            CodingCommandTranscriptState.Backgrounded => context.Theme.Accent,
            _ => context.Theme.Border
        };

    private static void WriteClipped(string text, int width, Style style, ref SegmentWriter output)
    {
        if (width <= 0)
        {
            return;
        }

        var normalized = text.Replace('\t', ' ');
        if (normalized.Length <= width)
        {
            output.Write(normalized.AsSpan(), style);
            return;
        }

        if (width <= 1)
        {
            output.Write(".".AsSpan(), style);
            return;
        }

        var marker = width >= 3 ? "..." : new string('.', width);
        output.Write(normalized.AsSpan(0, width - marker.Length), style);
        output.Write(marker.AsSpan(), style);
    }

    private sealed record DisplayOutputSnapshot(
        IReadOnlyList<CodingCommandOutputLine> Lines,
        int OmittedLineCount,
        int HeadLineCount);
}
