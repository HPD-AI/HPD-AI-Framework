using System.Text;
using HPD.TUI.Core;
using HPD.TUI.Utilities;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.FileMutations.Views;

internal sealed class FileMutationTranscriptView : IComponent
{
    private const int MaxRenderedDiffLines = 18;
    private const int MaxDiagnostics = 4;
    private readonly FileMutationTranscriptModel _model;

    public FileMutationTranscriptView(FileMutationTranscriptModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        var rows = Math.Max(1, Math.Min(MaxRenderedDiffLines, CountDiffLines(_model.Mutation)));
        if (_model.Mutation.Hunks.Count > 1)
        {
            rows += _model.Mutation.Hunks.Count - 1;
        }

        if (_model.Mutation.HunksTruncated)
        {
            rows++;
        }

        if (ShouldRenderDiagnostics(_model.Diagnostics))
        {
            rows += 1 + Math.Min(MaxDiagnostics, CountVisibleDiagnostics(_model.Diagnostics!));
            if (_model.Diagnostics!.DiagnosticsTruncated)
            {
                rows++;
            }
        }

        return new Measurement(1, Math.Min(maxWidth, 100), rows);
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        if (maxWidth <= 0)
        {
            return;
        }

        var gutterWidth = CalculateGutterWidth(_model.Mutation);
        var renderedLines = 0;
        var wroteAny = false;

        foreach (var hunk in _model.Mutation.Hunks)
        {
            if (wroteAny)
            {
                output.WriteLineBreak();
                WriteHunkSeparator(context.Theme.Border, ref output);
            }

            var oldLine = hunk.OldStart;
            var newLine = hunk.NewStart;
            foreach (var rawLine in hunk.Lines)
            {
                if (renderedLines >= MaxRenderedDiffLines)
                {
                    output.WriteLineBreak();
                    WriteTruncation("diff truncated", context.Theme.Border, ref output);
                    RenderDiagnosticsIfNeeded(in context, maxWidth, ref output);
                    return;
                }

                if (wroteAny)
                {
                    output.WriteLineBreak();
                }

                var parsed = ParseLine(rawLine);
                var number = parsed.Sign switch
                {
                    '-' => oldLine++,
                    '+' => newLine++,
                    _ => newLine++
                };

                if (parsed.Sign is ' ')
                {
                    oldLine++;
                }

                WriteDiffLine(number, gutterWidth, parsed.Sign, parsed.Text, in context, maxWidth, ref output);
                renderedLines++;
                wroteAny = true;
            }
        }

        if (!wroteAny)
        {
            output.Write("no diff available".AsSpan(), context.Theme.Border);
        }

        if (_model.Mutation.HunksTruncated)
        {
            output.WriteLineBreak();
            WriteTruncation("diff truncated", context.Theme.Border, ref output);
        }

        RenderDiagnosticsIfNeeded(in context, maxWidth, ref output);
    }

    public void HandleInput(in KeyEvent key)
    {
    }

    public void Invalidate()
    {
    }

    private void RenderDiagnosticsIfNeeded(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        if (!ShouldRenderDiagnostics(_model.Diagnostics))
        {
            return;
        }

        output.WriteLineBreak();
        output.WriteLineBreak();
        DiagnosticsTranscriptView.RenderDiagnosticsBody(
            _model.Diagnostics!,
            maxWidth,
            MaxDiagnostics,
            in context,
            ref output);
    }

    private static void WriteDiffLine(
        int lineNumber,
        int gutterWidth,
        char sign,
        string text,
        in RenderContext context,
        int maxWidth,
        ref SegmentWriter output)
    {
        var background = sign switch
        {
            '+' => new Color(24, 62, 38),
            '-' => new Color(82, 34, 30),
            _ => Color.Default
        };
        var style = sign switch
        {
            '+' => new Style(context.Theme.Success.Foreground, background),
            '-' => new Style(context.Theme.Error.Foreground, background),
            _ => context.Theme.Text
        };
        var gutterStyle = sign is '+' or '-'
            ? new Style(context.Theme.Border.Foreground, background)
            : context.Theme.Border;

        var prefix = $"{lineNumber.ToString().PadLeft(gutterWidth)} {sign}";
        output.Write(prefix.AsSpan(), gutterStyle);
        WriteWrapped(text, prefix.Length, maxWidth, sign, style, gutterStyle, ref output);
    }

    private static void WriteWrapped(
        string text,
        int prefixWidth,
        int maxWidth,
        char sign,
        Style style,
        Style gutterStyle,
        ref SegmentWriter output)
    {
        var contentWidth = Math.Max(1, maxWidth - prefixWidth);
        var rows = Wrap(text, contentWidth);
        output.Write(rows[0].AsSpan(), style);
        WriteLineFill(prefixWidth + UnicodeWidth.GetWidth(rows[0].AsSpan()), maxWidth, style, ref output);
        for (var i = 1; i < rows.Count; i++)
        {
            output.WriteLineBreak();
            output.Write(new string(' ', Math.Max(0, prefixWidth - 1)).AsSpan(), gutterStyle);
            output.Write(sign, gutterStyle);
            output.Write(rows[i].AsSpan(), style);
            WriteLineFill(prefixWidth + UnicodeWidth.GetWidth(rows[i].AsSpan()), maxWidth, style, ref output);
        }
    }

    private static void WriteLineFill(int usedWidth, int maxWidth, Style style, ref SegmentWriter output)
    {
        var remaining = maxWidth - usedWidth;
        if (remaining <= 0 || style.Background.IsDefault)
        {
            return;
        }

        output.Write(new string(' ', remaining).AsSpan(), style);
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

    private static void WriteHunkSeparator(Style style, ref SegmentWriter output)
    {
        output.Write("⋮".AsSpan(), style);
    }

    private static void WriteTruncation(string text, Style style, ref SegmentWriter output)
    {
        output.Write("⋮ ".AsSpan(), style);
        output.Write(text.AsSpan(), style);
    }

    private static (char Sign, string Text) ParseLine(string rawLine)
    {
        if (string.IsNullOrEmpty(rawLine))
        {
            return (' ', "");
        }

        var first = rawLine[0];
        return first is '+' or '-' or ' '
            ? (first, rawLine[1..])
            : (' ', rawLine);
    }

    private static int CalculateGutterWidth(FileMutationAppliedEvent mutation)
    {
        var max = 1;
        foreach (var hunk in mutation.Hunks)
        {
            max = Math.Max(max, hunk.OldStart + Math.Max(0, hunk.OldLines - 1));
            max = Math.Max(max, hunk.NewStart + Math.Max(0, hunk.NewLines - 1));
        }

        return max.ToString().Length;
    }

    private static int CountDiffLines(FileMutationAppliedEvent mutation)
        => mutation.Hunks.Sum(static hunk => hunk.Lines.Count);

    private static bool ShouldRenderDiagnostics(LanguageServerDiagnosticsReceivedEvent? diagnostics)
        => diagnostics is not null &&
           (diagnostics.ErrorCount > 0 || diagnostics.WarningCount > 0 || diagnostics.DiagnosticsTruncated);

    private static int CountVisibleDiagnostics(LanguageServerDiagnosticsReceivedEvent diagnostics)
        => diagnostics.Diagnostics.Count(static diagnostic =>
            diagnostic.Severity is LanguageServerDiagnosticSeverity.Error or LanguageServerDiagnosticSeverity.Warning);
}
