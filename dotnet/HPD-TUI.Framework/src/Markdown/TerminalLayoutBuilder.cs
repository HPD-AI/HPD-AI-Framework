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
    private readonly List<List<MutableRun>> _lines = [[]];
    private int _column;
    private string _wrapPrefix = string.Empty;
    private Style _wrapPrefixStyle;

    internal TerminalLayoutBuilder(int width) => _width = width;

    internal int Column => _column;

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
        if (string.IsNullOrEmpty(value)) return;
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

    internal void NewLine(bool wrapped = false)
    {
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
                if (final.Text.Length == 0) line.RemoveAt(line.Count - 1);
            }
            lines.Add(new(line.Select(static run => new StyledTerminalRun(
                run.Text.ToString(), run.Style, run.Hyperlink, run.SourceStart,
                run.SourceEndExclusive, run.Decorative)).ToImmutableArray()));
        }
        return new() { SourceStart = sourceStart, SourceEndExclusive = sourceEndExclusive, Lines = lines.ToImmutable() };
    }

    private void Append(string text, Style style, TerminalHyperlink? hyperlink, int? sourceStart, int? sourceEndExclusive, bool decorative)
    {
        var line = _lines[^1];
        if (line.Count > 0 && line[^1].CanAppend(style, hyperlink, sourceStart, sourceEndExclusive, decorative))
        {
            line[^1].Text.Append(text);
            if (line[^1].SourceEndExclusive == sourceStart)
                line[^1].SourceEndExclusive = sourceEndExclusive;
        }
        else
            line.Add(new(new StringBuilder(text), style, hyperlink, sourceStart, sourceEndExclusive, decorative));
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

        internal bool CanAppend(Style nextStyle, TerminalHyperlink? nextHyperlink, int? nextStart, int? nextEnd, bool nextDecorative) =>
            Style == nextStyle && Hyperlink == nextHyperlink && Decorative == nextDecorative &&
            (SourceEndExclusive == nextStart || SourceStart == nextStart && SourceEndExclusive == nextEnd);
    }
}
