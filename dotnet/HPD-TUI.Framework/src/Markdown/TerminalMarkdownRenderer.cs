using System.Text;
using System.Globalization;
using System.Net;
using HPD.TUI.Core;
using HPD.TUI.Utilities;
using Markdig.Extensions.AutoLinks;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace HPD.TUI.Markdown;

/// <summary>Dispatches Markdig nodes to frozen typed terminal object renderers.</summary>
internal sealed class TerminalMarkdownRenderer : RendererBase
{
    private readonly MarkdownDocumentSnapshot _document;
    private readonly MarkdownLayoutOptions _options;
    private readonly ICodeHighlighter _highlighter;
    private readonly string _registrationSignature;
    private TerminalLayoutBuilder _builder = null!;

    internal TerminalMarkdownRenderer(MarkdownDocumentSnapshot document, MarkdownLayoutOptions options, ICodeHighlighter? highlighter = null)
    {
        _document = document;
        _options = options;
        _highlighter = highlighter ?? new BasicCodeHighlighter();
        CurrentStyle = options.Theme.Body;
        foreach (var extension in document.Pipeline.TerminalExtensions)
            extension.Extension.ConfigureTerminal(ObjectRenderers, extension.Options);
        ObjectRenderers.Add(new TableRenderer());
        ObjectRenderers.Add(new TaskListRenderer());
        ObjectRenderers.Add(new AutolinkRenderer());
        ObjectRenderers.Add(new HeadingRenderer());
        ObjectRenderers.Add(new ParagraphRenderer());
        ObjectRenderers.Add(new FencedCodeRenderer());
        ObjectRenderers.Add(new CodeBlockRenderer());
        ObjectRenderers.Add(new ListRenderer());
        ObjectRenderers.Add(new ListItemRenderer());
        ObjectRenderers.Add(new QuoteRenderer());
        ObjectRenderers.Add(new ThematicBreakRenderer());
        ObjectRenderers.Add(new HtmlBlockRenderer());
        ObjectRenderers.Add(new LiteralInlineRenderer());
        ObjectRenderers.Add(new EmphasisRenderer());
        ObjectRenderers.Add(new CodeInlineRenderer());
        ObjectRenderers.Add(new LinkRenderer());
        ObjectRenderers.Add(new LineBreakRenderer());
        ObjectRenderers.Add(new HtmlInlineRenderer());
        ObjectRenderers.Add(new LiteralFallbackRenderer());
        _registrationSignature = RegistrationSignature();
    }

    internal TerminalLayoutBuilder Builder => _builder;
    internal MarkdownLayoutOptions Options => _options;
    internal ICodeHighlighter Highlighter => _highlighter;
    internal Style CurrentStyle { get; set; }
    internal TerminalHyperlink? ActiveHyperlink { get; set; }
    internal int ListDepth { get; set; }
    internal int QuoteDepth { get; set; }
    internal MarkdownDegradationReason DegradationReason { get; set; }
    internal string PendingListMarker { get; set; } = string.Empty;

    public override object Render(MarkdownObject markdownObject)
    {
        ArgumentNullException.ThrowIfNull(markdownObject);
        if (markdownObject is not Block block) throw new ArgumentException("Independent layout requires a block.", nameof(markdownObject));
        var selected = _document.Blocks.FirstOrDefault(candidate => ReferenceEquals(candidate.Syntax, block))
            ?? throw new InvalidOperationException("The block does not belong to this snapshot.");
        return RenderBlock(selected);
    }

    internal MarkdownBlockLayout RenderBlock(MarkdownTopLevelBlock block)
    {
        if (!string.Equals(_registrationSignature, RegistrationSignature(), StringComparison.Ordinal))
            throw new InvalidOperationException("Terminal Markdown renderer registration changed after construction.");
        _builder = new(_options.Width);
        CurrentStyle = _options.Theme.Body;
        ActiveHyperlink = null;
        ListDepth = QuoteDepth = 0;
        PendingListMarker = string.Empty;
        DegradationReason = MarkdownDegradationReason.None;
        Write(block.Syntax);
        var layout = _builder.Freeze(block.SourceStart, block.SourceEndExclusive);
        return new MarkdownBlockLayout
        {
            SourceStart = layout.SourceStart,
            SourceEndExclusive = layout.SourceEndExclusive,
            Lines = layout.Lines,
            DegradationReason = DegradationReason
        };
    }

    private string RegistrationSignature() => string.Join('|', ObjectRenderers.Select(static renderer => renderer.GetType().AssemblyQualifiedName));

    internal void WriteExact(MarkdownObject node, Style style)
    {
        var (start, end) = NormalizeSpan(node);
        Builder.Write(_document.Source[start..end], style, ActiveHyperlink, start, end);
    }

    internal (int Start, int End) NormalizeSpan(MarkdownObject node)
    {
        var start = Math.Clamp(node.Span.Start, 0, _document.Source.Length);
        var end = node.Span.End < start ? start : Math.Clamp(node.Span.End + 1, start, _document.Source.Length);
        return (start, end);
    }

    internal (int Start, int End) LocateRenderedContent(MarkdownObject node, string rendered)
    {
        var (start, end) = NormalizeSpan(node);
        var relative = _document.Source.AsSpan(start, end - start).IndexOf(rendered.AsSpan(), StringComparison.Ordinal);
        return relative >= 0 ? (start + relative, start + relative + rendered.Length) : (start, end);
    }

    internal void WriteTransformed(MarkdownObject node, string rendered, Style style)
    {
        var (sourceStart, sourceEnd) = NormalizeSpan(node);
        var cursor = sourceStart;
        for (var visual = 0; visual < rendered.Length;)
        {
            var length = StringInfo.GetNextTextElementLength(rendered.AsSpan(visual));
            var grapheme = rendered.AsSpan(visual, length);
            var mappedStart = sourceStart;
            var mappedEnd = sourceEnd;
            if (cursor < sourceEnd && _document.Source[cursor] == '&')
            {
                var terminator = _document.Source.IndexOf(';', cursor, Math.Min(32, _document.Source.Length - cursor));
                if (terminator >= cursor && WebUtility.HtmlDecode(_document.Source[cursor..(terminator + 1)]) == grapheme.ToString())
                {
                    mappedStart = cursor;
                    mappedEnd = terminator + 1;
                    cursor = mappedEnd;
                }
                else if (grapheme.SequenceEqual("&"))
                {
                    mappedStart = cursor;
                    mappedEnd = ++cursor;
                }
            }
            else if (cursor + length <= sourceEnd && _document.Source.AsSpan(cursor, length).SequenceEqual(grapheme))
            {
                mappedStart = cursor;
                mappedEnd = cursor + length;
                cursor = mappedEnd;
            }
            else if (cursor < sourceEnd && _document.Source[cursor] == '\\' &&
                     cursor + 1 + length <= sourceEnd &&
                     _document.Source.AsSpan(cursor + 1, length).SequenceEqual(grapheme))
            {
                mappedStart = cursor;
                mappedEnd = cursor + 1 + length;
                cursor = mappedEnd;
            }
            else
            {
                var found = _document.Source.AsSpan(cursor, sourceEnd - cursor).IndexOf(grapheme, StringComparison.Ordinal);
                if (found >= 0)
                {
                    mappedStart = cursor + found;
                    mappedEnd = mappedStart + length;
                    cursor = mappedEnd;
                }
            }
            Builder.Write(grapheme.ToString(), style, ActiveHyperlink, mappedStart, mappedEnd);
            visual += length;
        }
    }

    internal void WithPresentation(Style style, TerminalHyperlink? hyperlink, Action action)
    {
        var oldStyle = CurrentStyle;
        var oldLink = ActiveHyperlink;
        CurrentStyle = style;
        ActiveHyperlink = hyperlink;
        try { action(); }
        finally { CurrentStyle = oldStyle; ActiveHyperlink = oldLink; }
    }

    internal MarkdownBlockLayout RenderNested(ContainerBlock container, int width, Style style)
    {
        var previousBuilder = _builder;
        var previousStyle = CurrentStyle;
        var previousLink = ActiveHyperlink;
        _builder = new(Math.Max(1, width));
        CurrentStyle = style;
        ActiveHyperlink = null;
        try
        {
            foreach (var child in container) Write(child);
            var (start, end) = NormalizeSpan(container);
            return _builder.Freeze(start, end);
        }
        finally
        {
            _builder = previousBuilder;
            CurrentStyle = previousStyle;
            ActiveHyperlink = previousLink;
        }
    }
}

internal abstract class TerminalObjectRenderer<T> : MarkdownObjectRenderer<TerminalMarkdownRenderer, T> where T : MarkdownObject;

internal sealed class HeadingRenderer : TerminalObjectRenderer<HeadingBlock>
{
    protected override void Write(TerminalMarkdownRenderer r, HeadingBlock n)
    {
        var style = n.Level == 1 ? r.Options.Theme.Heading1 : n.Level == 2 ? r.Options.Theme.Heading2 : r.Options.Theme.Heading3;
        if (n.Inline is not null)
            r.WithPresentation(style, null, () => r.WriteChildren(n.Inline));
    }
}

internal sealed class ParagraphRenderer : TerminalObjectRenderer<ParagraphBlock>
{ protected override void Write(TerminalMarkdownRenderer r, ParagraphBlock n) { if (n.Inline is not null) r.WriteChildren(n.Inline); } }

internal sealed class LiteralInlineRenderer : TerminalObjectRenderer<LiteralInline>
{
    protected override void Write(TerminalMarkdownRenderer r, LiteralInline n)
    {
        r.WriteTransformed(n, n.Content.ToString(), r.CurrentStyle);
    }
}

internal sealed class EmphasisRenderer : TerminalObjectRenderer<EmphasisInline>
{
    protected override void Write(TerminalMarkdownRenderer r, EmphasisInline n)
    {
        var attributes = n.DelimiterChar == '~' ? r.CurrentStyle.Attributes | TextAttributes.Strikethrough :
            n.DelimiterCount == 1 ? r.CurrentStyle.Attributes | TextAttributes.Italic :
            n.DelimiterCount == 2 ? r.CurrentStyle.Attributes | TextAttributes.Bold :
            r.CurrentStyle.Attributes | TextAttributes.Bold | TextAttributes.Italic;
        r.WithPresentation(r.CurrentStyle with { Attributes = attributes }, r.ActiveHyperlink, () => r.WriteChildren(n));
    }
}

internal sealed class CodeInlineRenderer : TerminalObjectRenderer<CodeInline>
{
    protected override void Write(TerminalMarkdownRenderer r, CodeInline n)
    {
        var (start, end) = r.LocateRenderedContent(n, n.Content);
        r.Builder.Write(n.Content, r.Options.Theme.InlineCode, r.ActiveHyperlink, start, end);
    }
}

internal sealed class LinkRenderer : TerminalObjectRenderer<LinkInline>
{
    protected override void Write(TerminalMarkdownRenderer r, LinkInline n)
    {
        if (n.IsImage)
        {
            r.Builder.Write("[img] ", r.Options.Theme.CodeBorder, decorative: true);
            r.WithPresentation(r.Options.Theme.CodeBorder, null, () => r.WriteChildren(n));
            return;
        }
        TerminalHyperlinkPolicy.TryCreate(n.Url, out var link);
        r.WithPresentation(r.Options.Theme.Link, link, () => r.WriteChildren(n));
    }
}

internal sealed class AutolinkRenderer : TerminalObjectRenderer<AutolinkInline>
{
    protected override void Write(TerminalMarkdownRenderer r, AutolinkInline n)
    {
        TerminalHyperlinkPolicy.TryCreate(n.Url, out var link);
        var (start, end) = r.LocateRenderedContent(n, n.Url);
        r.Builder.Write(n.Url, r.Options.Theme.Link, link, start, end);
    }
}

internal sealed class LineBreakRenderer : TerminalObjectRenderer<LineBreakInline>
{
    protected override void Write(TerminalMarkdownRenderer r, LineBreakInline n)
    {
        if (n.IsHard) { r.Builder.NewLine(); return; }
        var (start, end) = r.NormalizeSpan(n);
        r.Builder.Write(" ", r.CurrentStyle, r.ActiveHyperlink, start, end);
    }
}

internal sealed class HtmlInlineRenderer : TerminalObjectRenderer<HtmlInline>
{ protected override void Write(TerminalMarkdownRenderer r, HtmlInline n) => r.WriteExact(n, r.Options.Theme.CodeBorder); }

internal sealed class TaskListRenderer : TerminalObjectRenderer<TaskList>
{ protected override void Write(TerminalMarkdownRenderer r, TaskList n) => r.Builder.Write(n.Checked ? "[x] " : "[ ] ", n.Checked ? r.Options.Theme.QuoteMarker : r.Options.Theme.CodeBorder, decorative: true); }

internal sealed class FencedCodeRenderer : TerminalObjectRenderer<FencedCodeBlock>
{
    protected override void Write(TerminalMarkdownRenderer r, FencedCodeBlock n) => WriteCode(r, n, n.Info);
    internal static void WriteCode(TerminalMarkdownRenderer r, CodeBlock n, string? language)
    {
        r.Builder.Write("code", r.Options.Theme.CodeBorder, decorative: true);
        if (!string.IsNullOrWhiteSpace(language))
        { r.Builder.Write(" ", r.Options.Theme.Body, decorative: true); r.Builder.Write(language.Trim(), r.Options.Theme.CodeLanguage, decorative: true); }
        r.Builder.NewLine();
        var code = n.Lines.ToString().TrimEnd();
        var highlightEligible = code.Length <= (r.Options.ResourceLimits ?? new MarkdownResourceLimits()).MaximumHighlightedCodeLength;
        if (!highlightEligible) r.DegradationReason = MarkdownDegradationReason.CodeHighlightLength;
        var result = highlightEligible
            ? r.Highlighter.Highlight(code.AsMemory(), language, r.Options.Theme)
            : new CodeHighlightResult([new StyledTerminalLine([new StyledTerminalRun(code, r.Options.Theme.Body)])], null);
        for (var index = 0; index < result.Lines.Length; index++)
        {
            if (index > 0) r.Builder.NewLine();
            r.Builder.WriteRepeated(' ', (r.Options.Spacing ?? new MarkdownSpacing()).CodeIndent, r.Options.Theme.CodeBorder);
            foreach (var run in result.Lines[index].Runs)
                r.Builder.Write(run.Text, run.Style, run.Hyperlink, n.Span.Start, n.Span.End + 1);
        }
    }
}

internal sealed class CodeBlockRenderer : TerminalObjectRenderer<CodeBlock>
{ protected override void Write(TerminalMarkdownRenderer r, CodeBlock n) => FencedCodeRenderer.WriteCode(r, n, null); }

internal sealed class ListRenderer : TerminalObjectRenderer<ListBlock>
{
    protected override void Write(TerminalMarkdownRenderer r, ListBlock n)
    {
        var depth = r.ListDepth++;
        var ordinal = int.TryParse(n.OrderedStart, out var parsed) ? parsed : 1;
        var first = true;
        foreach (var item in n.OfType<ListItemBlock>())
        {
            if (!first) r.Builder.NewLine();
            first = false;
            r.PendingListMarker = n.IsOrdered ? $"{ordinal++}. " : depth switch { 0 => "• ", 1 => "o ", _ => "- " };
            r.Write(item);
        }
        r.ListDepth--;
    }
}

internal sealed class ListItemRenderer : TerminalObjectRenderer<ListItemBlock>
{
    protected override void Write(TerminalMarkdownRenderer r, ListItemBlock n)
    {
        var indent = Math.Max(0, r.ListDepth - 1) * (r.Options.Spacing ?? new MarkdownSpacing()).ListIndent;
        var prefixWidth = indent + r.PendingListMarker.Length;
        r.Builder.WriteRepeated(' ', indent, r.Options.Theme.Body);
        r.Builder.Write(r.PendingListMarker, r.Options.Theme.CodeBorder, decorative: true);
        r.Builder.SetWrapPrefix(new string(' ', prefixWidth), r.Options.Theme.Body);
        var first = true;
        foreach (var child in n)
        {
            if (!first) { r.Builder.NewLine(); if (child is not ListBlock) r.Builder.WriteRepeated(' ', prefixWidth, r.Options.Theme.Body); }
            r.Write(child);
            first = false;
        }
        r.Builder.ClearWrapPrefix();
    }
}

internal sealed class QuoteRenderer : TerminalObjectRenderer<QuoteBlock>
{
    protected override void Write(TerminalMarkdownRenderer r, QuoteBlock n)
    {
        var depth = r.QuoteDepth++;
        var prefix = new string('>', depth) + "|" + new string(' ', Math.Max(1, (r.Options.Spacing ?? new MarkdownSpacing()).QuoteIndent - depth - 1));
        var first = true;
        foreach (var child in n)
        {
            if (!first) r.Builder.NewLine();
            first = false;
            r.Builder.Write(prefix, r.Options.Theme.QuoteMarker, decorative: true);
            r.Builder.SetWrapPrefix(new string(' ', prefix.Length), r.Options.Theme.Body);
            r.WithPresentation(r.Options.Theme.Emphasis, null, () => r.Write(child));
            r.Builder.ClearWrapPrefix();
        }
        r.QuoteDepth--;
    }
}

internal sealed class ThematicBreakRenderer : TerminalObjectRenderer<ThematicBreakBlock>
{ protected override void Write(TerminalMarkdownRenderer r, ThematicBreakBlock n) => r.Builder.Write(new string('─', Math.Min(r.Options.Width, 120)), r.Options.Theme.CodeBorder, decorative: true); }

internal sealed class HtmlBlockRenderer : TerminalObjectRenderer<HtmlBlock>
{ protected override void Write(TerminalMarkdownRenderer r, HtmlBlock n) => r.WriteExact(n, r.Options.Theme.CodeBorder); }

internal sealed class TableRenderer : TerminalObjectRenderer<Table>
{
    protected override void Write(TerminalMarkdownRenderer r, Table table)
    {
        var rows = table.OfType<TableRow>().Select(row => row.OfType<TableCell>().ToArray()).ToArray();
        var columns = rows.Length == 0 ? 0 : rows.Max(row => row.Length);
        if (columns == 0) return;
        var limits = r.Options.ResourceLimits ?? new MarkdownResourceLimits();
        if (columns > limits.MaximumTableColumns || rows.Sum(static row => row.Length) > limits.MaximumTableCells)
        {
            r.DegradationReason = MarkdownDegradationReason.TableShape;
            r.WriteExact(table, r.Options.Theme.Body);
            return;
        }
        var naturalLayouts = rows.Select((row, rowIndex) => row.Select(cell =>
            r.RenderNested(cell, 4096, rowIndex == 0 ? r.Options.Theme.TableHeader : r.Options.Theme.Body)).ToArray()).ToArray();
        var natural = naturalLayouts.Select(row => row.Select(PlainText).ToArray()).ToArray();
        var widths = Widths(natural, columns, r.Options.Width);
        if (widths is null) { r.WriteExact(table, r.Options.Theme.Body); return; }
        Border(r, '┌', '┬', '┐', widths); r.Builder.NewLine();
        for (var row = 0; row < rows.Length; row++)
        {
            if (row > 0) { Border(r, '├', '┼', '┤', widths); r.Builder.NewLine(); }
            var cells = Enumerable.Range(0, columns).Select(column => column < rows[row].Length
                ? WrapCell(naturalLayouts[row][column], widths[column])
                : new MarkdownBlockLayout { SourceStart = table.Span.Start, SourceEndExclusive = table.Span.Start, Lines = [StyledTerminalLine.Empty] }).ToArray();
            for (var line = 0; line < cells.Max(cell => cell.Lines.Length); line++)
            {
                if (line > 0) r.Builder.NewLine();
                r.Builder.Write("│", r.Options.Theme.TableBorder, decorative: true);
                for (var column = 0; column < columns; column++)
                {
                    var cellLine = line < cells[column].Lines.Length ? cells[column].Lines[line] : StyledTerminalLine.Empty;
                    r.Builder.Write(" ", r.Options.Theme.Body, decorative: true);
                    var used = 0;
                    foreach (var run in cellLine.Runs)
                    {
                        r.Builder.Write(run.Text, run.Style, run.Hyperlink, run.SourceStart, run.SourceEndExclusive, run.IsDecorative);
                        used += UnicodeWidth.GetWidth(run.Text.AsSpan());
                    }
                    r.Builder.WriteRepeated(' ', Math.Max(0, widths[column] - used) + 1, r.Options.Theme.Body);
                    r.Builder.Write("│", r.Options.Theme.TableBorder, decorative: true);
                }
            }
            r.Builder.NewLine();
        }
        Border(r, '└', '┴', '┘', widths);
    }

    private static string PlainText(MarkdownBlockLayout cell) => string.Join(' ', cell.Lines
        .Select(line => string.Concat(line.Runs.Where(static run => !run.IsDecorative).Select(static run => run.Text))))
        .Trim();

    private static MarkdownBlockLayout WrapCell(MarkdownBlockLayout source, int width)
    {
        var builder = new TerminalLayoutBuilder(width);
        for (var lineIndex = 0; lineIndex < source.Lines.Length; lineIndex++)
        {
            if (lineIndex > 0) builder.NewLine();
            foreach (var run in source.Lines[lineIndex].Runs)
            {
                var index = 0;
                var pendingSpace = false;
                while (index < run.Text.Length)
                {
                    while (index < run.Text.Length && char.IsWhiteSpace(run.Text[index])) { pendingSpace = true; index++; }
                    var start = index;
                    while (index < run.Text.Length && !char.IsWhiteSpace(run.Text[index])) index++;
                    if (start == index) continue;
                    var word = run.Text[start..index];
                    var wordWidth = UnicodeWidth.GetWidth(word.AsSpan());
                    if (builder.Column > 0 && builder.Column + (pendingSpace ? 1 : 0) + wordWidth > width)
                    {
                        builder.NewLine();
                        pendingSpace = false;
                    }
                    if (pendingSpace && builder.Column > 0)
                        builder.Write(" ", run.Style, run.Hyperlink, run.SourceStart, run.SourceEndExclusive, run.IsDecorative);
                    builder.Write(word, run.Style, run.Hyperlink, run.SourceStart, run.SourceEndExclusive, run.IsDecorative);
                    pendingSpace = false;
                }
            }
        }
        return builder.Freeze(source.SourceStart, source.SourceEndExclusive);
    }

    private static int[]? Widths(string[][] rows, int count, int maxWidth)
    {
        var available = maxWidth - (count * 3 + 1);
        if (available < count) return null;
        var widths = new int[count];
        var minimum = Enumerable.Repeat(1, count).ToArray();
        foreach (var row in rows) for (var column = 0; column < count; column++)
        {
            var value = column < row.Length ? row[column] : "";
            widths[column] = Math.Max(widths[column], Math.Max(1, UnicodeWidth.GetWidth(value.AsSpan())));
            minimum[column] = Math.Max(minimum[column], value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => UnicodeWidth.GetWidth(word.AsSpan())).DefaultIfEmpty(1).Max());
        }
        while (widths.Sum() > available)
        {
            var index = Enumerable.Range(0, count).OrderByDescending(i => widths[i] - minimum[i]).First();
            if (widths[index] <= minimum[index]) { index = Enumerable.Range(0, count).OrderByDescending(i => widths[i]).First(); if (widths[index] <= 1) return null; }
            widths[index]--;
        }
        return widths;
    }

    private static void Border(TerminalMarkdownRenderer r, char left, char join, char right, int[] widths)
    {
        r.Builder.Write(left.ToString(), r.Options.Theme.TableBorder, decorative: true);
        for (var column = 0; column < widths.Length; column++)
        { r.Builder.WriteRepeated('─', widths[column] + 2, r.Options.Theme.TableBorder); r.Builder.Write((column == widths.Length - 1 ? right : join).ToString(), r.Options.Theme.TableBorder, decorative: true); }
    }
}

internal sealed class LiteralFallbackRenderer : TerminalObjectRenderer<MarkdownObject>
{ protected override void Write(TerminalMarkdownRenderer r, MarkdownObject n) => r.WriteExact(n, r.Options.Theme.Body); }
