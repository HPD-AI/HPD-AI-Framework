using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using HPD.TUI.Core;
using HPD.TUI.Utilities;

namespace HPD.TUI.Markdown;

/// <summary>Builds immutable terminal lines during layout without retaining a render-time writer.</summary>
internal sealed class TerminalLayoutBuilder
{
    private readonly int _width;
    private readonly int _maximumRows;
    private readonly List<List<MutableRun>> _lines = [[]];
    private int _column;
    private string _wrapPrefix = string.Empty;
    private Style _wrapPrefixStyle;

    internal TerminalLayoutBuilder(int width, int maximumRows = 16_384)
    {
        _width = width;
        _maximumRows = maximumRows;
    }

    internal int Column => _column;
    internal bool LimitExceeded { get; private set; }

    internal void SetWrapPrefix(string prefix, Style style)
    {
        _wrapPrefix = prefix;
        _wrapPrefixStyle = style;
    }

    internal void ClearWrapPrefix() => _wrapPrefix = string.Empty;

    internal void Write(
        string? value,
        Style style,
        TerminalHyperlink? hyperlink = null,
        int? sourceStart = null,
        int? sourceEndExclusive = null,
        bool decorative = false)
    {
        if (string.IsNullOrEmpty(value) || LimitExceeded) return;
        var hasExactSourceRange = sourceStart.HasValue && sourceEndExclusive.HasValue &&
            sourceEndExclusive.Value - sourceStart.Value == value.Length;
        var offset = 0;
        while (offset < value.Length)
        {
            var length = StringInfo.GetNextTextElementLength(value.AsSpan(offset));
            var grapheme = value.AsSpan(offset, length);
            if (grapheme.SequenceEqual("\r")) { offset += length; continue; }
            if (grapheme.SequenceEqual("\n") || grapheme.SequenceEqual("\r\n"))
            {
                NewLine();
                if (LimitExceeded) return;
                offset += length;
                continue;
            }
            if (grapheme.SequenceEqual("\t"))
            {
                var spaces = 4 - (_column & 3);
                var tabStart = hasExactSourceRange ? sourceStart + offset : sourceStart;
                var tabEnd = hasExactSourceRange ? tabStart + length : sourceEndExclusive;
                Write(new string(' ', spaces), style, hyperlink, tabStart, tabEnd, decorative);
                offset += length;
                continue;
            }

            var safe = true;
            foreach (var character in grapheme)
                if (TerminalTextSafety.IsUnsafe(character)) { safe = false; break; }
            var text = safe ? grapheme.ToString() : "�";
            var displayWidth = Math.Max(0, UnicodeWidth.GetWidth(text.AsSpan()));
            if (_column > 0 && displayWidth > 0 && _column + displayWidth > _width)
            {
                NewLine(wrapped: true);
                if (LimitExceeded) return;
                if (text == " ") { offset += length; continue; }
            }
            var graphemeStart = hasExactSourceRange ? sourceStart + offset : sourceStart;
            var graphemeEnd = hasExactSourceRange ? graphemeStart + length : sourceEndExclusive;
            Append(text, style, hyperlink, graphemeStart, graphemeEnd, decorative);
            _column += displayWidth;
            offset += length;
        }
    }

    internal void WriteRepeated(char value, int count, Style style, bool decorative = true)
    {
        if (count > 0) Write(new string(value, count), style, decorative: decorative);
    }

    internal void WriteRun(StyledTerminalRun run) => WriteSlice(run, 0, run.Text.Length);

    internal void WriteSlice(StyledTerminalRun run, int visualStart, int visualLength)
    {
        if (visualLength <= 0) return;
        if (run.SourceMap.IsDefaultOrEmpty)
        {
            Write(run.Text.Substring(visualStart, visualLength), run.Style, run.Hyperlink,
                run.SourceStart, run.SourceEndExclusive, run.IsDecorative);
            return;
        }

        var visualEnd = visualStart + visualLength;
        var cursor = visualStart;
        foreach (var segment in run.SourceMap)
        {
            var start = Math.Max(cursor, segment.VisualStart);
            var end = Math.Min(visualEnd, segment.VisualEndExclusive);
            if (start >= end) continue;
            if (cursor < start)
                Write(run.Text[cursor..start], run.Style, run.Hyperlink, decorative: run.IsDecorative);
            var sourceStart = segment.SourceStart;
            var sourceEnd = segment.SourceEndExclusive;
            if (segment.SourceEndExclusive - segment.SourceStart == segment.VisualEndExclusive - segment.VisualStart)
            {
                sourceStart += start - segment.VisualStart;
                sourceEnd = sourceStart + end - start;
            }
            Write(run.Text[start..end], run.Style, run.Hyperlink, sourceStart, sourceEnd, run.IsDecorative);
            cursor = end;
            if (cursor >= visualEnd) break;
        }
        if (cursor < visualEnd)
            Write(run.Text[cursor..visualEnd], run.Style, run.Hyperlink, decorative: run.IsDecorative);
    }

    internal void NewLine(bool wrapped = false)
    {
        if (_lines.Count >= _maximumRows) { LimitExceeded = true; return; }
        _lines.Add([]);
        _column = 0;
        if (wrapped && _wrapPrefix.Length > 0)
            Write(_wrapPrefix, _wrapPrefixStyle, decorative: true);
    }

    internal MarkdownBlockLayout Freeze(int sourceStart, int sourceEndExclusive)
    {
        while (_lines.Count > 1 && _lines[^1].Count == 0) _lines.RemoveAt(_lines.Count - 1);
        var lines = ImmutableArray.CreateBuilder<StyledTerminalLine>(_lines.Count);
        foreach (var line in _lines)
        {
            if (line.Count > 0)
            {
                var final = line[^1];
                var trimmed = final.Text.ToString().TrimEnd();
                final.Text.Clear().Append(trimmed);
                final.TrimSourceMap(trimmed.Length);
                if (final.Text.Length == 0) line.RemoveAt(line.Count - 1);
            }
            lines.Add(new(line.Select(static run => new StyledTerminalRun(
                run.Text.ToString(), run.Style, run.Hyperlink, run.SourceStart,
                run.SourceEndExclusive, run.Decorative, run.SourceMap.ToImmutableArray())).ToImmutableArray()));
        }
        return new() { SourceStart = sourceStart, SourceEndExclusive = sourceEndExclusive, Lines = lines.ToImmutable() };
    }

    private void Append(string text, Style style, TerminalHyperlink? hyperlink, int? sourceStart, int? sourceEndExclusive, bool decorative)
    {
        var line = _lines[^1];
        if (line.Count > 0 && line[^1].CanAppend(style, hyperlink, sourceStart, sourceEndExclusive, decorative))
        {
            var visualStart = line[^1].Text.Length;
            line[^1].Text.Append(text);
            if (sourceStart.HasValue && sourceEndExclusive.HasValue)
                line[^1].SourceMap.Add(new(visualStart, visualStart + text.Length, sourceStart.Value, sourceEndExclusive.Value));
            if (line[^1].SourceEndExclusive == sourceStart)
                line[^1].SourceEndExclusive = sourceEndExclusive;
        }
        else
        {
            var run = new MutableRun(new StringBuilder(text), style, hyperlink, sourceStart, sourceEndExclusive, decorative);
            if (sourceStart.HasValue && sourceEndExclusive.HasValue)
                run.SourceMap.Add(new(0, text.Length, sourceStart.Value, sourceEndExclusive.Value));
            line.Add(run);
        }
    }

    private sealed class MutableRun(
        StringBuilder text,
        Style style,
        TerminalHyperlink? hyperlink,
        int? sourceStart,
        int? sourceEndExclusive,
        bool decorative)
    {
        internal StringBuilder Text { get; } = text;
        internal Style Style { get; } = style;
        internal TerminalHyperlink? Hyperlink { get; } = hyperlink;
        internal int? SourceStart { get; } = sourceStart;
        internal int? SourceEndExclusive { get; set; } = sourceEndExclusive;
        internal bool Decorative { get; } = decorative;
        internal List<MarkdownSourceMapSegment> SourceMap { get; } = [];

        internal void TrimSourceMap(int visualLength)
        {
            SourceMap.RemoveAll(segment => segment.VisualStart >= visualLength);
            if (SourceMap.Count == 0) return;
            if (SourceMap[^1].VisualEndExclusive <= visualLength)
            {
                SourceEndExclusive = SourceMap[^1].SourceEndExclusive;
                return;
            }
            var final = SourceMap[^1];
            var sourceEnd = final.SourceEndExclusive;
            if (final.SourceEndExclusive - final.SourceStart == final.VisualEndExclusive - final.VisualStart)
                sourceEnd = final.SourceStart + visualLength - final.VisualStart;
            SourceMap[^1] = final with { VisualEndExclusive = visualLength, SourceEndExclusive = sourceEnd };
            SourceEndExclusive = sourceEnd;
        }

        internal bool CanAppend(Style nextStyle, TerminalHyperlink? nextHyperlink, int? nextStart, int? nextEnd, bool nextDecorative) =>
            Style == nextStyle && Hyperlink == nextHyperlink && Decorative == nextDecorative &&
            (SourceEndExclusive == nextStart || SourceStart == nextStart && SourceEndExclusive == nextEnd);
    }
}
