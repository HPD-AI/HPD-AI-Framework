using System.Text;
using HPD.TUI.Core;
using HPD.TUI.Utilities;

namespace HPD.Agent.ToolHarness.Coding.TUI.SourcePresentation;

/// <summary>
/// Renders bounded source hunks with line numbers, semantic gutter annotations,
/// wrapping, emphasis bands, and optional trailing explanations.
/// </summary>
internal sealed class AnnotatedSourceView : IComponent
{
    private const int TabWidth = 4;
    private readonly AnnotatedSourceDocument _document;
    private readonly CodingHarnessTuiTheme _theme;

    public AnnotatedSourceView(
        AnnotatedSourceDocument document,
        CodingHarnessTuiTheme theme)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        if (maxWidth <= 0)
        {
            return new Measurement(0, 0, 0);
        }

        var gutter = MeasureGutter(_document);
        var rows = 0;
        for (var hunkIndex = 0; hunkIndex < _document.Hunks.Count; hunkIndex++)
        {
            if (hunkIndex > 0)
            {
                rows++;
            }

            foreach (var line in _document.Hunks[hunkIndex].Lines)
            {
                rows += MeasureLineRows(line, gutter.PrefixWidth, maxWidth);
            }
        }

        if (_document.Truncated)
        {
            rows++;
        }

        return new Measurement(1, Math.Min(maxWidth, 120), Math.Max(1, rows));
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        if (maxWidth <= 0)
        {
            return;
        }

        var gutter = MeasureGutter(_document);
        var wroteAny = false;
        foreach (var hunk in _document.Hunks)
        {
            if (wroteAny)
            {
                output.WriteLineBreak();
                output.Write("⋮".AsSpan(), _theme.ResolveMuted(context.Theme));
            }

            foreach (var line in hunk.Lines)
            {
                if (wroteAny)
                {
                    output.WriteLineBreak();
                }

                RenderLine(line, gutter, in context, maxWidth, ref output);
                wroteAny = true;
            }
        }

        if (!wroteAny)
        {
            output.Write("no source preview available".AsSpan(), _theme.ResolveMuted(context.Theme));
        }

        if (_document.Truncated)
        {
            if (wroteAny)
            {
                output.WriteLineBreak();
            }

            output.Write("⋮ ".AsSpan(), _theme.ResolveMuted(context.Theme));
            output.Write(
                (_document.TruncationReason ?? "source preview truncated").AsSpan(),
                _theme.ResolveMuted(context.Theme));
        }
    }

    public bool HandleInput(in TuiInputEvent input) => false;

    private void RenderLine(
        AnnotatedSourceLine line,
        SourceGutterMeasurement gutter,
        in RenderContext context,
        int maxWidth,
        ref SegmentWriter output)
    {
        var marker = BuildMarker(line.Annotations);
        var primaryTone = ResolvePrimaryTone(line.Annotations);
        var background = ResolveBackground(line.Emphasis, primaryTone);
        var textStyle = ResolveTextStyle(line.Emphasis, primaryTone, context.Theme, background);
        var gutterStyle = WithBackground(
            _theme.ResolveSourceGutter(context.Theme, background),
            background);
        var trailingStyle = ResolveTrailingStyle(primaryTone, context.Theme, background);
        var prefix = string.Concat(
            line.LineNumber.ToString().PadLeft(gutter.LineNumberWidth),
            " ",
            PadMarker(marker, gutter.MarkerWidth),
            " ");

        output.Write(prefix.AsSpan(), gutterStyle);

        var contentWidth = Math.Max(1, maxWidth - gutter.PrefixWidth);
        var expandedText = ExpandTabs(line.Text);
        var wrapped = Wrap(expandedText, contentWidth);
        WriteContentRow(
            wrapped[0],
            _document.Language,
            gutter.PrefixWidth,
            maxWidth,
            textStyle,
            context.Theme,
            ref output);
        for (var i = 1; i < wrapped.Count; i++)
        {
            output.WriteLineBreak();
            WriteContinuationGutter(gutter.PrefixWidth, gutterStyle, ref output);
            WriteContentRow(
                wrapped[i],
                _document.Language,
                gutter.PrefixWidth,
                maxWidth,
                textStyle,
                context.Theme,
                ref output);
        }

        if (string.IsNullOrWhiteSpace(line.TrailingText))
        {
            return;
        }

        var lastWidth = UnicodeWidth.GetWidth(wrapped[^1].AsSpan());
        var trailing = $"  {line.TrailingText}";
        if (lastWidth + UnicodeWidth.GetWidth(trailing.AsSpan()) <= contentWidth)
        {
            output.Write(trailing.AsSpan(), trailingStyle);
            FillBackground(
                gutter.PrefixWidth + lastWidth + UnicodeWidth.GetWidth(trailing.AsSpan()),
                maxWidth,
                textStyle,
                ref output);
            return;
        }

        output.WriteLineBreak();
        WriteContinuationGutter(gutter.PrefixWidth, gutterStyle, ref output);
        var trailingRows = Wrap(line.TrailingText, contentWidth);
        var continuationStyle = WithoutBackground(trailingStyle);
        for (var i = 0; i < trailingRows.Count; i++)
        {
            output.Write(trailingRows[i].AsSpan(), continuationStyle);
            if (i < trailingRows.Count - 1)
            {
                output.WriteLineBreak();
                WriteContinuationGutter(gutter.PrefixWidth, gutterStyle, ref output);
            }
        }
    }

    private static void WriteContentRow(
        string text,
        string? language,
        int prefixWidth,
        int maxWidth,
        Style style,
        Theme theme,
        ref SegmentWriter output)
    {
        SourceTextHighlighter.Render(text, language, style, theme, ref output);
        FillBackground(prefixWidth + UnicodeWidth.GetWidth(text.AsSpan()), maxWidth, style, ref output);
    }

    private static void FillBackground(
        int usedWidth,
        int maxWidth,
        Style style,
        ref SegmentWriter output)
    {
        var remaining = maxWidth - usedWidth;
        if (remaining > 0 && !style.Background.IsDefault)
        {
            output.Write(new string(' ', remaining).AsSpan(), style);
        }
    }

    private static void WriteContinuationGutter(
        int prefixWidth,
        Style gutterStyle,
        ref SegmentWriter output)
        => output.Write(new string(' ', prefixWidth).AsSpan(), gutterStyle);

    private Style ResolveTextStyle(
        SourceLineEmphasis emphasis,
        SourceAnnotationTone tone,
        Theme theme,
        Color background)
        => (emphasis, tone) switch
        {
            (SourceLineEmphasis.Added, _) or (_, SourceAnnotationTone.Added)
                => _theme.ResolveDiffAdded(theme, background),
            (SourceLineEmphasis.Removed, _) or (_, SourceAnnotationTone.Removed)
                => _theme.ResolveDiffRemoved(theme, background),
            (SourceLineEmphasis.Warning, _) or (_, SourceAnnotationTone.Warning)
                => WithBackground(_theme.ResolveDebugBreakpointPending(theme), background),
            (SourceLineEmphasis.Error, _) or (_, SourceAnnotationTone.Error)
                => WithBackground(_theme.ResolveDebugBreakpointRejected(theme), background),
            (SourceLineEmphasis.Current, _) or (_, SourceAnnotationTone.Current)
                => WithBackground(_theme.ResolveDebugCurrentLine(theme), background),
            _ => WithBackground(_theme.ResolveDiffContext(theme), background)
        };

    private Style ResolveTrailingStyle(
        SourceAnnotationTone tone,
        Theme theme,
        Color background)
        => tone switch
        {
            SourceAnnotationTone.Error => WithBackground(_theme.ResolveDebugBreakpointRejected(theme), background),
            SourceAnnotationTone.Warning => WithBackground(_theme.ResolveDebugBreakpointPending(theme), background),
            SourceAnnotationTone.Success or SourceAnnotationTone.Added
                => WithBackground(_theme.ResolveDebugBreakpointVerified(theme), background),
            SourceAnnotationTone.Information or SourceAnnotationTone.Current
                => WithBackground(_theme.ResolveDebugCurrentLine(theme), background),
            _ => WithBackground(_theme.ResolveSourceTrailingAnnotation(theme), background)
        };

    private static Style WithBackground(Style style, Color background)
        => background.IsDefault
            ? style
            : new Style(style.Foreground, background, style.Attributes);

    private static Style WithoutBackground(Style style)
        => new(style.Foreground, Color.Default, style.Attributes);

    private static Color ResolveBackground(
        SourceLineEmphasis emphasis,
        SourceAnnotationTone tone)
        => (emphasis, tone) switch
        {
            (SourceLineEmphasis.Added, _) or (_, SourceAnnotationTone.Added)
                => new Color(24, 62, 38),
            (SourceLineEmphasis.Removed, _) or (_, SourceAnnotationTone.Removed)
                => new Color(82, 34, 30),
            (SourceLineEmphasis.Warning, _) or (_, SourceAnnotationTone.Warning)
                => new Color(72, 58, 24),
            (SourceLineEmphasis.Error, _) or (_, SourceAnnotationTone.Error)
                => new Color(72, 30, 34),
            (SourceLineEmphasis.Current, _) or (_, SourceAnnotationTone.Current)
                => new Color(30, 48, 74),
            (SourceLineEmphasis.Subtle, _) => new Color(32, 34, 40),
            _ => Color.Default
        };

    private static SourceAnnotationTone ResolvePrimaryTone(
        IReadOnlyList<SourceAnnotation> annotations)
    {
        var result = SourceAnnotationTone.Neutral;
        var priority = -1;
        foreach (var annotation in annotations)
        {
            var candidate = annotation.Tone switch
            {
                SourceAnnotationTone.Error => 7,
                SourceAnnotationTone.Current => 6,
                SourceAnnotationTone.Warning => 5,
                SourceAnnotationTone.Removed => 4,
                SourceAnnotationTone.Added => 3,
                SourceAnnotationTone.Success => 2,
                SourceAnnotationTone.Information => 1,
                _ => 0
            };
            if (candidate > priority)
            {
                result = annotation.Tone;
                priority = candidate;
            }
        }

        return result;
    }

    private static string BuildMarker(IReadOnlyList<SourceAnnotation> annotations)
    {
        if (annotations.Count == 0)
        {
            return " ";
        }

        var builder = new StringBuilder();
        foreach (var annotation in annotations)
        {
            if (!string.IsNullOrEmpty(annotation.Marker))
            {
                builder.Append(annotation.Marker);
            }
        }

        return builder.Length == 0 ? " " : builder.ToString();
    }

    private static string PadMarker(string marker, int width)
    {
        var used = UnicodeWidth.GetWidth(marker.AsSpan());
        return used >= width ? marker : marker + new string(' ', width - used);
    }

    private static SourceGutterMeasurement MeasureGutter(
        AnnotatedSourceDocument document)
    {
        var maxLine = 1;
        var markerWidth = 1;
        foreach (var hunk in document.Hunks)
        {
            foreach (var line in hunk.Lines)
            {
                maxLine = Math.Max(maxLine, line.LineNumber);
                markerWidth = Math.Max(
                    markerWidth,
                    UnicodeWidth.GetWidth(BuildMarker(line.Annotations).AsSpan()));
            }
        }

        var lineNumberWidth = maxLine.ToString().Length;
        return new(
            lineNumberWidth,
            markerWidth,
            lineNumberWidth + 1 + markerWidth + 1);
    }

    private static int MeasureLineRows(
        AnnotatedSourceLine line,
        int prefixWidth,
        int maxWidth)
    {
        var contentWidth = Math.Max(1, maxWidth - prefixWidth);
        var sourceRows = Wrap(ExpandTabs(line.Text), contentWidth);
        var rows = sourceRows.Count;
        if (!string.IsNullOrWhiteSpace(line.TrailingText))
        {
            var sourceLast = sourceRows[^1];
            var trailingWidth = UnicodeWidth.GetWidth($"  {line.TrailingText}".AsSpan());
            if (UnicodeWidth.GetWidth(sourceLast.AsSpan()) + trailingWidth > contentWidth)
            {
                rows += Wrap(line.TrailingText, contentWidth).Count;
            }
        }

        return rows;
    }

    private static string ExpandTabs(string text)
    {
        if (!text.Contains('\t', StringComparison.Ordinal))
            return text;

        var result = new StringBuilder(text.Length);
        var column = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == '\t')
            {
                var spaces = TabWidth - column % TabWidth;
                result.Append(' ', spaces);
                column += spaces;
                continue;
            }

            result.Append(rune);
            column += Math.Max(0, UnicodeWidth.GetWidth(rune));
        }
        return result.ToString();
    }

    private static IReadOnlyList<string> Wrap(string text, int width)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [""];
        }

        var rows = new List<string>();
        var row = new StringBuilder();
        var rowWidth = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var runeWidth = Math.Max(0, UnicodeWidth.GetWidth(rune));
            if (row.Length > 0 && rowWidth + runeWidth > width)
            {
                rows.Add(row.ToString());
                row.Clear();
                rowWidth = 0;
            }

            row.Append(rune.ToString());
            rowWidth += runeWidth;
        }

        rows.Add(row.ToString());
        return rows;
    }

    private sealed record SourceGutterMeasurement(
        int LineNumberWidth,
        int MarkerWidth,
        int PrefixWidth);
}
