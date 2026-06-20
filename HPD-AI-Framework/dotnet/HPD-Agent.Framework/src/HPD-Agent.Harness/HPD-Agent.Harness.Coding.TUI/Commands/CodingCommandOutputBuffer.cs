using System.Text;
using HPD.TUI.Utilities;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands;

internal sealed class CodingCommandOutputBuffer
{
    private const int MaxBufferedLines = 400;
    private const int MaxBufferedCharacters = 64 * 1024;
    private const int MaxLineCharacters = 4 * 1024;
    private readonly List<CodingCommandBufferedOutputLine> _lines = [];
    private string _pending = "";
    private int _discardedLineCount;
    private int _discardedCharacterCount;
    private int _bufferedCharacterCount;

    public bool Suppressed { get; private set; }

    public bool Binary { get; private set; }

    public bool Truncated { get; private set; }

    public void Append(ExecuteCommandStreamKind stream, string text, bool suppressed, bool binary, bool truncated)
    {
        Suppressed |= suppressed;
        Binary |= binary;
        Truncated |= truncated;

        if (suppressed || binary || string.IsNullOrEmpty(text))
        {
            return;
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var parts = normalized.Split('\n');
        if (parts.Length == 0)
        {
            return;
        }

        parts[0] = _pending + parts[0];
        for (var i = 0; i < parts.Length; i++)
        {
            var isLast = i == parts.Length - 1;
            if (isLast && normalized.EndsWith('\n') && parts[i].Length == 0)
            {
                continue;
            }

            parts[i] = ClipLine(parts[i]);
            if (isLast && !normalized.EndsWith('\n'))
            {
                _pending = parts[i];
                continue;
            }

            AddLine(stream, parts[i]);
        }

        if (normalized.EndsWith('\n'))
        {
            _pending = "";
        }
    }

    public CodingCommandOutputSnapshot CreateSnapshot(
        int headRows = 2,
        int tailRows = 2,
        int maxVisibleRows = 5,
        int? wrapWidth = null)
    {
        var materialized = new List<CodingCommandBufferedOutputLine>(_lines);
        if (!string.IsNullOrEmpty(_pending))
        {
            var stream = _lines.Count > 0 ? _lines[^1].Stream : ExecuteCommandStreamKind.Stdout;
            materialized.Add(new CodingCommandBufferedOutputLine(stream, _pending));
        }

        TrimBoundaryBlankLines(materialized);

        var visibleRows = wrapWidth is > 0
            ? WrapToVisualRows(materialized, wrapWidth.Value)
            : materialized;

        if (visibleRows.Count <= maxVisibleRows)
        {
            return new CodingCommandOutputSnapshot(
                visibleRows,
                _discardedLineCount,
                HeadLineCount: visibleRows.Count,
                Truncated,
                Suppressed,
                Binary);
        }

        var head = Math.Clamp(headRows, 0, maxVisibleRows);
        var tail = Math.Clamp(tailRows, 0, Math.Max(0, maxVisibleRows - head));
        var visible = visibleRows.Take(head)
            .Concat(visibleRows.TakeLast(tail))
            .ToArray();
        var omitted = visibleRows.Count - visible.Length + _discardedLineCount;
        if (_discardedCharacterCount > 0 && omitted == 0)
        {
            omitted = 1;
        }

        return new CodingCommandOutputSnapshot(
            visible,
            omitted,
            HeadLineCount: head,
            Truncated: true,
            Suppressed,
            Binary);
    }

    private static void TrimBoundaryBlankLines(List<CodingCommandBufferedOutputLine> lines)
    {
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0].Text))
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1].Text))
        {
            lines.RemoveAt(lines.Count - 1);
        }
    }

    private static List<CodingCommandBufferedOutputLine> WrapToVisualRows(
        IReadOnlyList<CodingCommandBufferedOutputLine> lines,
        int width)
    {
        var rows = new List<CodingCommandBufferedOutputLine>();
        foreach (var line in lines)
        {
            AddWrappedRows(line, width, rows);
        }

        return rows;
    }

    private static void AddWrappedRows(
        CodingCommandBufferedOutputLine line,
        int width,
        List<CodingCommandBufferedOutputLine> rows)
    {
        if (width <= 0 || string.IsNullOrEmpty(line.Text))
        {
            rows.Add(line);
            return;
        }

        var row = new StringBuilder();
        var rowWidth = 0;
        var enumerator = line.Text.EnumerateRunes();
        foreach (var rune in enumerator)
        {
            var runeWidth = Math.Max(0, UnicodeWidth.GetWidth(rune));
            if (row.Length > 0 && rowWidth + runeWidth > width)
            {
                rows.Add(new CodingCommandBufferedOutputLine(line.Stream, row.ToString()));
                row.Clear();
                rowWidth = 0;
            }

            row.Append(rune.ToString());
            rowWidth += runeWidth;
        }

        rows.Add(new CodingCommandBufferedOutputLine(line.Stream, row.ToString()));
    }

    private void AddLine(ExecuteCommandStreamKind stream, string text)
    {
        text = ClipLine(text);
        _lines.Add(new CodingCommandBufferedOutputLine(stream, text));
        _bufferedCharacterCount += text.Length;
        while (_lines.Count > MaxBufferedLines)
        {
            RemoveFirstLine();
        }

        while (_bufferedCharacterCount > MaxBufferedCharacters && _lines.Count > 0)
        {
            RemoveFirstLine();
        }
    }

    private void RemoveFirstLine()
    {
        _discardedCharacterCount += _lines[0].Text.Length;
        _bufferedCharacterCount -= _lines[0].Text.Length;
        _lines.RemoveAt(0);
        _discardedLineCount++;
        Truncated = true;
    }

    private string ClipLine(string text)
    {
        if (text.Length <= MaxLineCharacters)
        {
            return text;
        }

        Truncated = true;
        _discardedCharacterCount += text.Length - MaxLineCharacters;
        const string marker = " ... [line clipped] ... ";
        var keep = Math.Max(0, MaxLineCharacters - marker.Length);
        var head = Math.Min(48, keep / 2);
        var tail = keep - head;
        return string.Concat(
            text.AsSpan(0, head),
            marker,
            text.AsSpan(text.Length - tail, tail));
    }
}

internal sealed record CodingCommandBufferedOutputLine(
    ExecuteCommandStreamKind Stream,
    string Text);

internal sealed record CodingCommandOutputSnapshot(
    IReadOnlyList<CodingCommandBufferedOutputLine> Lines,
    int OmittedLineCount,
    int HeadLineCount,
    bool Truncated,
    bool Suppressed,
    bool Binary);
