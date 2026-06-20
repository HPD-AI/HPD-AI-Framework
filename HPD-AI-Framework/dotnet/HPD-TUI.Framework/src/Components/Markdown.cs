using System.Text;
using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;
using HPD.TUI.Utilities;
using Markdig;
using Markdig.Extensions.AutoLinks;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace HPD.TUI.Components;

public sealed class Markdown : IComponent
{
    private const int MaxInlineRenderHeight = 16_384;

    private static readonly Markdig.MarkdownPipeline Pipeline = new Markdig.MarkdownPipelineBuilder()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .UseTaskLists()
        .UsePipeTables()
        .Build();

    private string _source;
    private readonly Theme? _themeOverride;
    private readonly Dictionary<Table, MarkdownTableModel> _tableModels = [];
    private readonly Dictionary<Table, MarkdownTableLayout> _tableLayouts = [];
    private readonly Dictionary<Block, string> _blockText = [];
    private MarkdownDocument? _document;
    private bool _parseAttempted;

    public Markdown(string source, Theme? theme = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _themeOverride = theme;
        ParseSource();
    }

    public string Source => _source;

    public void SetSource(string source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        ParseSource();
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        var maxLine = 0;
        foreach (var line in EnumerateLines(_source.AsSpan()))
        {
            maxLine = Math.Max(maxLine, Math.Min(maxWidth, UnicodeWidth.GetWidth(TrimMarkdownPrefix(line))));
        }

        return new Measurement(Math.Min(maxWidth, Math.Min(maxLine, 1)), Math.Min(maxWidth, maxLine));
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        var theme = _themeOverride ?? context.Theme;
        var document = GetDocument();
        if (document is null)
        {
            output.Write(_source.AsSpan(), theme.Text);
            return;
        }

        var state = new RenderState(theme, maxWidth, context.Height);
        var first = true;

        foreach (var block in document)
        {
            if (output.CursorY >= context.Height)
            {
                break;
            }

            if (!first)
            {
                if (!output.WriteLineBreak())
                {
                    break;
                }
            }

            first = false;
            RenderBlock(block, ref state, ref output);
        }
    }

    public void HandleInput(in KeyEvent key)
    {
    }

    public void Invalidate()
    {
    }

    private MarkdownDocument? GetDocument()
    {
        if (!_parseAttempted)
        {
            ParseSource();
        }

        return _document;
    }

    private void ParseSource()
    {
        _parseAttempted = true;
        _tableModels.Clear();
        _tableLayouts.Clear();
        _blockText.Clear();

        try
        {
            _document = Markdig.Markdown.Parse(_source, Pipeline);
        }
        catch
        {
            _document = null;
        }
    }

    private string GetBlockText(Block block, bool trim = false, bool trimEnd = false)
    {
        if (_blockText.TryGetValue(block, out var text))
        {
            return text;
        }

        text = block switch
        {
            LeafBlock leaf => leaf.Lines.ToString(),
            _ => string.Empty
        };

        if (trim)
        {
            text = text.Trim();
        }
        else if (trimEnd)
        {
            text = text.TrimEnd();
        }

        _blockText.Add(block, text);
        return text;
    }

    private void RenderBlock(Block block, ref RenderState state, ref SegmentWriter output)
    {
        switch (block)
        {
            case HeadingBlock heading:
                RenderHeading(heading, ref state, ref output);
                break;
            case ParagraphBlock paragraph:
                RenderParagraph(paragraph, ref state, ref output);
                break;
            case FencedCodeBlock fenced:
                RenderFencedCodeBlock(fenced, ref state, ref output);
                break;
            case CodeBlock code:
                RenderCodeBlock(code, ref state, ref output);
                break;
            case ListBlock list:
                RenderList(list, 0, ref state, ref output);
                break;
            case QuoteBlock quote:
                RenderQuote(quote, 0, ref state, ref output);
                break;
            case ThematicBreakBlock:
                RenderRule(ref state, ref output);
                break;
            case HtmlBlock html:
                output.Write(GetBlockText(html, trim: true).AsSpan(), state.Theme.Border);
                break;
            case Table table:
                RenderTable(table, ref state, ref output);
                break;
            case LeafBlock leaf when GetBlockText(leaf, trimEnd: true) is { Length: > 0 } text:
                output.Write(text.TrimEnd().AsSpan(), state.Theme.Text);
                break;
        }
    }

    private static void RenderHeading(HeadingBlock heading, ref RenderState state, ref SegmentWriter output)
    {
        var style = heading.Level switch
        {
            1 => new Style(state.Theme.Text.Foreground, state.Theme.Text.Background, TextAttributes.Bold | TextAttributes.Underline),
            2 => new Style(state.Theme.Accent.Foreground, state.Theme.Accent.Background, TextAttributes.Bold),
            3 => new Style(state.Theme.Warning.Foreground, state.Theme.Warning.Background, TextAttributes.Bold | TextAttributes.Italic),
            4 => new Style(state.Theme.Text.Foreground, state.Theme.Text.Background, TextAttributes.Italic),
            _ => new Style(state.Theme.Border.Foreground, state.Theme.Border.Background, TextAttributes.Bold)
        };

        RenderInlines(heading.Inline, style, ref state, ref output);
    }

    private static void RenderParagraph(ParagraphBlock paragraph, ref RenderState state, ref SegmentWriter output)
    {
        RenderInlines(paragraph.Inline, state.Theme.Text, ref state, ref output);
    }

    private void RenderFencedCodeBlock(FencedCodeBlock codeBlock, ref RenderState state, ref SegmentWriter output)
    {
        var language = codeBlock.Info.AsSpan().Trim();
        RenderCodeHeader(language, ref state, ref output);
        output.WriteLineBreak();

        var code = GetBlockText(codeBlock, trimEnd: true);
        RenderCodeLines(code.AsSpan(), language, ref state, ref output);
    }

    private void RenderCodeBlock(CodeBlock codeBlock, ref RenderState state, ref SegmentWriter output)
    {
        RenderCodeHeader(default, ref state, ref output);
        output.WriteLineBreak();

        var code = GetBlockText(codeBlock, trimEnd: true);
        RenderCodeLines(code.AsSpan(), default, ref state, ref output);
    }

    private static void RenderCodeHeader(ReadOnlySpan<char> language, ref RenderState state, ref SegmentWriter output)
    {
        output.Write("code", state.Theme.Border);
        if (!language.IsEmpty)
        {
            output.Write(" ", state.Theme.Text);
            output.Write(language, state.Theme.Warning);
        }
    }

    private static void RenderCodeLines(ReadOnlySpan<char> code, ReadOnlySpan<char> language, ref RenderState state, ref SegmentWriter output)
    {
        var first = true;
        foreach (var line in EnumerateLines(code))
        {
            if (!first)
            {
                output.WriteLineBreak();
            }

            first = false;
            output.Write("  ", state.Theme.Border);
            RenderHighlightedCode(line, language, ref state, ref output);
        }
    }

    private void RenderList(ListBlock list, int depth, ref RenderState state, ref SegmentWriter output)
    {
        var index = list.OrderedStart is null ? 1 : int.Parse(list.OrderedStart);
        var first = true;

        foreach (var child in list)
        {
            if (child is not ListItemBlock item)
            {
                continue;
            }

            if (!first)
            {
                output.WriteLineBreak();
            }

            first = false;
            var indentWidth = depth * 2;
            output.WriteRepeated(' ', indentWidth, state.Theme.Text);
            var markerWidth = 0;

            if (list.IsOrdered)
            {
                var marker = $"{index}. ";
                output.Write(marker.AsSpan(), state.Theme.Accent);
                markerWidth = UnicodeWidth.GetWidth(marker.AsSpan());
                index++;
            }
            else
            {
                var bullet = depth switch
                {
                    0 => "• ",
                    1 => "o ",
                    _ => "- "
                };
                output.Write(bullet, state.Theme.Border);
                markerWidth = UnicodeWidth.GetWidth(bullet.AsSpan());
            }

            RenderListItem(item, depth, indentWidth + markerWidth, ref state, ref output);
        }
    }

    private void RenderListItem(
        ListItemBlock item,
        int depth,
        int contentIndent,
        ref RenderState state,
        ref SegmentWriter output)
    {
        var first = true;
        foreach (var block in item)
        {
            if (block is ParagraphBlock paragraph)
            {
                if (!first)
                {
                    output.WriteLineBreak();
                    output.WriteRepeated(' ', contentIndent, state.Theme.Text);
                }

                RenderIndentedInlines(paragraph.Inline, contentIndent, ref state, ref output);
                first = false;
            }
            else if (block is ListBlock nested)
            {
                output.WriteLineBreak();
                RenderList(nested, depth + 1, ref state, ref output);
                first = false;
            }
            else
            {
                if (!first)
                {
                    output.WriteLineBreak();
                    output.WriteRepeated(' ', contentIndent, state.Theme.Text);
                }

                RenderBlock(block, ref state, ref output);
                first = false;
            }
        }
    }

    private static void RenderIndentedInlines(
        ContainerInline? container,
        int contentIndent,
        ref RenderState state,
        ref SegmentWriter output)
    {
        if (container is null)
        {
            return;
        }

        var bodyWidth = Math.Max(1, state.MaxWidth - contentIndent);
        var bodyHeight = Math.Clamp(state.MaxHeight - output.CursorY, 1, MaxInlineRenderHeight);
        using var grid = new TerminalGrid(bodyWidth, bodyHeight);
        var capture = new SegmentWriter(grid);
        var captureState = new RenderState(state.Theme, bodyWidth, bodyHeight);
        RenderInlines(container, state.Theme.Text, ref captureState, ref capture);

        var lineCount = TuiCapture.GetUsedLineCount(grid);
        for (var y = 0; y < lineCount; y++)
        {
            if (y > 0)
            {
                output.WriteLineBreak();
                output.WriteRepeated(' ', contentIndent, state.Theme.Text);
            }

            WriteCapturedInlineLine(grid, y, trimLeadingBlankCells: y > 0, ref output);
        }
    }

    private static void WriteCapturedInlineLine(
        TerminalGrid grid,
        int y,
        bool trimLeadingBlankCells,
        ref SegmentWriter output)
    {
        Span<char> runeBuffer = stackalloc char[2];
        var trimming = trimLeadingBlankCells;
        for (var x = 0; x < grid.Width; x++)
        {
            var cell = grid.GetCell(x, y);
            if (cell.IsContinuation)
            {
                continue;
            }

            if (trimming && cell.Rune.Value == ' ')
            {
                continue;
            }

            trimming = false;
            if (cell.Rune.TryEncodeToUtf16(runeBuffer, out var written))
            {
                output.Write(runeBuffer[..written], cell.Style);
            }
        }
    }

    private void RenderQuote(QuoteBlock quote, int depth, ref RenderState state, ref SegmentWriter output)
    {
        var first = true;
        foreach (var block in quote)
        {
            if (!first)
            {
                output.WriteLineBreak();
            }

            first = false;
            output.WriteRepeated('>', depth, state.Theme.Success);
            output.Write("| ", state.Theme.Success);

            if (block is ParagraphBlock paragraph)
            {
                RenderInlines(paragraph.Inline, new Style(state.Theme.Text.Foreground, state.Theme.Text.Background, TextAttributes.Italic), ref state, ref output);
            }
            else if (block is QuoteBlock nested)
            {
                RenderQuote(nested, depth + 1, ref state, ref output);
            }
            else
            {
                RenderBlock(block, ref state, ref output);
            }
        }
    }

    private void RenderTable(Table table, ref RenderState state, ref SegmentWriter output)
    {
        var model = GetTableModel(table);
        if (model.Rows.Count == 0 || model.ColumnCount == 0)
        {
            return;
        }

        var layout = GetTableLayout(table, model, state.MaxWidth);
        if (layout.Widths is null)
        {
            RenderRawTable(model, ref state, ref output);
            return;
        }

        var widths = layout.Widths;
        RenderTableBorder('┌', '┬', '┐', widths, ref state, ref output);
        output.WriteLineBreak();

        for (var rowIndex = 0; rowIndex < layout.Rows.Length; rowIndex++)
        {
            if (rowIndex > 0)
            {
                output.WriteLineBreak();
                RenderTableBorder('├', '┼', '┤', widths, ref state, ref output);
                output.WriteLineBreak();
            }

            RenderTableDataRow(layout.Rows[rowIndex], widths, rowIndex == 0, ref state, ref output);
        }

        output.WriteLineBreak();
        RenderTableBorder('└', '┴', '┘', widths, ref state, ref output);
    }

    private MarkdownTableModel GetTableModel(Table table)
    {
        if (_tableModels.TryGetValue(table, out var model))
        {
            return model;
        }

        model = BuildTableModel(table);
        _tableModels.Add(table, model);
        return model;
    }

    private MarkdownTableLayout GetTableLayout(Table table, MarkdownTableModel model, int maxWidth)
    {
        if (_tableLayouts.TryGetValue(table, out var layout) &&
            layout.MaxWidth == maxWidth)
        {
            return layout;
        }

        var widths = CalculateTableWidths(model, maxWidth);
        layout = widths is null
            ? new MarkdownTableLayout(maxWidth, null, [])
            : new MarkdownTableLayout(maxWidth, widths, BuildWrappedTableRows(model, widths));
        _tableLayouts[table] = layout;
        return layout;
    }

    private static MarkdownTableModel BuildTableModel(Table table)
    {
        var rows = new List<MarkdownTableRow>();
        var columnCount = 0;

        foreach (var row in table.OfType<TableRow>())
        {
            var cells = new List<string>();
            foreach (var cell in row.OfType<TableCell>())
            {
                cells.Add(ExtractTableCellText(cell));
            }

            columnCount = Math.Max(columnCount, cells.Count);
            rows.Add(new MarkdownTableRow(cells));
        }

        return new MarkdownTableModel(rows, columnCount);
    }

    private static int[]? CalculateTableWidths(MarkdownTableModel model, int maxWidth)
    {
        var columnCount = model.ColumnCount;
        var contentWidth = maxWidth - ((columnCount * 3) + 1);
        if (columnCount <= 0 || contentWidth < columnCount)
        {
            return null;
        }

        var natural = new int[columnCount];
        var minimum = new int[columnCount];

        foreach (var row in model.Rows)
        {
            for (var column = 0; column < columnCount; column++)
            {
                var value = row.GetCell(column);
                natural[column] = Math.Max(natural[column], Math.Max(1, UnicodeWidth.GetWidth(value.AsSpan())));
                minimum[column] = Math.Max(minimum[column], Math.Max(1, Math.Min(30, GetLongestWordWidth(value))));
            }
        }

        var widths = natural.ToArray();
        var total = widths.Sum();
        if (total <= contentWidth)
        {
            return widths;
        }

        var shrinkNeeded = total - contentWidth;
        while (shrinkNeeded > 0)
        {
            var widestColumn = -1;
            var widestExtra = 0;

            for (var column = 0; column < columnCount; column++)
            {
                var extra = widths[column] - minimum[column];
                if (extra > widestExtra)
                {
                    widestColumn = column;
                    widestExtra = extra;
                }
            }

            if (widestColumn < 0)
            {
                break;
            }

            widths[widestColumn]--;
            shrinkNeeded--;
        }

        while (widths.Sum() > contentWidth)
        {
            var widestColumn = 0;
            for (var column = 1; column < columnCount; column++)
            {
                if (widths[column] > widths[widestColumn])
                {
                    widestColumn = column;
                }
            }

            if (widths[widestColumn] <= 1)
            {
                return null;
            }

            widths[widestColumn]--;
        }

        return widths;
    }

    private static void RenderTableBorder(char left, char join, char right, int[] widths, ref RenderState state, ref SegmentWriter output)
    {
        output.Write(left, state.Theme.Border);
        for (var column = 0; column < widths.Length; column++)
        {
            output.WriteRepeated('─', widths[column] + 2, state.Theme.Border);
            output.Write(column == widths.Length - 1 ? right : join, state.Theme.Border);
        }
    }

    private static MarkdownWrappedTableRow[] BuildWrappedTableRows(MarkdownTableModel model, int[] widths)
    {
        var rows = new MarkdownWrappedTableRow[model.Rows.Count];
        for (var rowIndex = 0; rowIndex < model.Rows.Count; rowIndex++)
        {
            var row = model.Rows[rowIndex];
            var wrapped = new string[widths.Length][];
            var height = 1;

            for (var column = 0; column < widths.Length; column++)
            {
                var lines = WrapCellText(row.GetCell(column), widths[column]);
                if (lines.Length == 0)
                {
                    lines = [string.Empty];
                }

                wrapped[column] = lines;
                height = Math.Max(height, lines.Length);
            }

            rows[rowIndex] = new MarkdownWrappedTableRow(wrapped, height);
        }

        return rows;
    }

    private static void RenderTableDataRow(MarkdownWrappedTableRow row, int[] widths, bool isHeader, ref RenderState state, ref SegmentWriter output)
    {
        var cellStyle = isHeader
            ? new Style(state.Theme.Text.Foreground, state.Theme.Text.Background, TextAttributes.Bold)
            : state.Theme.Text;

        for (var line = 0; line < row.Height; line++)
        {
            if (line > 0)
            {
                output.WriteLineBreak();
            }

            output.Write("│", state.Theme.Border);
            for (var column = 0; column < widths.Length; column++)
            {
                var lines = row.Cells[column];
                var value = line < lines.Length ? lines[line] : string.Empty;
                output.Write(" ", state.Theme.Text);
                output.Write(value.AsSpan(), cellStyle);
                WritePadding(widths[column] - UnicodeWidth.GetWidth(value.AsSpan()) + 1, ref state, ref output);
                output.Write("│", state.Theme.Border);
            }
        }
    }

    private static void WritePadding(int count, ref RenderState state, ref SegmentWriter output)
    {
        if (count <= 0)
        {
            return;
        }

        output.WriteRepeated(' ', count, state.Theme.Text);
    }

    private static void WriteInt(int value, Style style, ref SegmentWriter output)
    {
        Span<char> buffer = stackalloc char[16];
        if (value.TryFormat(buffer, out var written))
        {
            output.Write(buffer[..written], style);
        }
    }

    private static string[] WrapCellText(string text, int width)
    {
        if (width <= 0)
        {
            return [];
        }

        var normalized = NormalizeWhitespace(text);
        if (normalized.Length == 0)
        {
            return [string.Empty];
        }

        var lines = new List<string>();
        var line = new StringBuilder();
        var lineWidth = 0;

        foreach (var word in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var remaining = word;
            while (remaining.Length > 0)
            {
                var remainingWidth = UnicodeWidth.GetWidth(remaining.AsSpan());
                var separatorWidth = lineWidth > 0 ? 1 : 0;
                if (lineWidth > 0 && lineWidth + separatorWidth + remainingWidth <= width)
                {
                    line.Append(' ');
                    line.Append(remaining);
                    lineWidth += separatorWidth + remainingWidth;
                    remaining = string.Empty;
                    continue;
                }

                if (lineWidth == 0 && remainingWidth <= width)
                {
                    line.Append(remaining);
                    lineWidth = remainingWidth;
                    remaining = string.Empty;
                    continue;
                }

                if (lineWidth > 0)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                    lineWidth = 0;
                    continue;
                }

                var split = TakeRunesByWidth(remaining, width, out var takenWidth);
                lines.Add(remaining[..split]);
                remaining = remaining[split..];
                lineWidth = 0;

                if (takenWidth <= 0)
                {
                    break;
                }
            }
        }

        if (line.Length > 0)
        {
            lines.Add(line.ToString());
        }

        return lines.ToArray();
    }

    private static int TakeRunesByWidth(string text, int width, out int takenWidth)
    {
        var consumed = 0;
        takenWidth = 0;
        var enumerator = new RuneEnumerator(text);
        while (enumerator.MoveNext())
        {
            var runeWidth = UnicodeWidth.GetWidth(enumerator.Current);
            if (takenWidth > 0 && takenWidth + runeWidth > width)
            {
                break;
            }

            if (takenWidth == 0 && runeWidth > width)
            {
                consumed += enumerator.Current.Utf16SequenceLength;
                takenWidth += runeWidth;
                break;
            }

            consumed += enumerator.Current.Utf16SequenceLength;
            takenWidth += runeWidth;
        }

        return consumed;
    }

    private static string NormalizeWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var rune in new RuneEnumerator(text))
        {
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }

    private static int GetLongestWordWidth(string value)
    {
        var max = 1;
        foreach (var word in NormalizeWhitespace(value).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            max = Math.Max(max, UnicodeWidth.GetWidth(word.AsSpan()));
        }

        return max;
    }

    private static void RenderRawTable(MarkdownTableModel model, ref RenderState state, ref SegmentWriter output)
    {
        for (var rowIndex = 0; rowIndex < model.Rows.Count; rowIndex++)
        {
            if (rowIndex > 0)
            {
                output.WriteLineBreak();
            }

            output.Write("|", state.Theme.Border);
            for (var column = 0; column < model.ColumnCount; column++)
            {
                output.Write(" ", state.Theme.Text);
                output.Write(model.Rows[rowIndex].GetCell(column).AsSpan(), rowIndex == 0
                    ? new Style(state.Theme.Text.Foreground, state.Theme.Text.Background, TextAttributes.Bold)
                    : state.Theme.Text);
                output.Write(" |", state.Theme.Border);
            }

            if (rowIndex == 0)
            {
                output.WriteLineBreak();
                output.Write("|", state.Theme.Border);
                for (var column = 0; column < model.ColumnCount; column++)
                {
                    output.Write(" --- |", state.Theme.Border);
                }
            }
        }
    }

    private static string ExtractTableCellText(TableCell cell)
    {
        var builder = new StringBuilder();
        var first = true;

        foreach (var block in cell)
        {
            if (!first)
            {
                builder.Append(' ');
            }

            first = false;
            if (block is ParagraphBlock paragraph)
            {
                AppendInlineText(paragraph.Inline, builder);
            }
            else if (block is LeafBlock leaf)
            {
                builder.Append(leaf.Lines.ToString().Trim());
            }
        }

        return NormalizeWhitespace(builder.ToString());
    }

    private static void AppendInlineText(ContainerInline? container, StringBuilder builder)
    {
        if (container is null)
        {
            return;
        }

        foreach (var inline in container)
        {
            AppendInlineText(inline, builder);
        }
    }

    private static void AppendInlineText(Inline inline, StringBuilder builder)
    {
        switch (inline)
        {
            case LiteralInline literal:
                builder.Append(literal.Content);
                break;
            case CodeInline code:
                builder.Append(code.Content);
                break;
            case LinkInline link:
                AppendInlineText(link, builder);
                break;
            case AutolinkInline autoLink:
                builder.Append(autoLink.Url);
                break;
            case LineBreakInline:
                builder.Append(' ');
                break;
            case HtmlInline html:
                builder.Append(html.Tag);
                break;
            case Markdig.Extensions.TaskLists.TaskList task:
                builder.Append(task.Checked ? "[x] " : "[ ] ");
                break;
            case ContainerInline nested:
                AppendInlineText(nested, builder);
                break;
        }
    }

    private static void RenderInlines(ContainerInline? container, Style baseStyle, ref RenderState state, ref SegmentWriter output)
    {
        if (container is null)
        {
            return;
        }

        foreach (var inline in container)
        {
            RenderInline(inline, baseStyle, ref state, ref output);
        }
    }

    private static void RenderInline(Inline inline, Style baseStyle, ref RenderState state, ref SegmentWriter output)
    {
        switch (inline)
        {
            case LiteralInline literal:
                output.Write(literal.Content.AsSpan(), baseStyle);
                break;
            case EmphasisInline emphasis:
                RenderEmphasis(emphasis, baseStyle, ref state, ref output);
                break;
            case CodeInline code:
                output.Write(code.Content.AsSpan(), new Style(state.Theme.Accent.Foreground, baseStyle.Background));
                break;
            case LinkInline { IsImage: true } image:
                output.Write("[img] ", state.Theme.Border);
                RenderInlines(image, state.Theme.Border, ref state, ref output);
                break;
            case LinkInline link:
                RenderInlines(link, new Style(state.Theme.Accent.Foreground, state.Theme.Accent.Background, TextAttributes.Underline), ref state, ref output);
                break;
            case AutolinkInline autoLink:
                output.Write(autoLink.Url.AsSpan(), new Style(state.Theme.Accent.Foreground, state.Theme.Accent.Background, TextAttributes.Underline));
                break;
            case LineBreakInline lineBreak:
                if (lineBreak.IsHard)
                {
                    output.WriteLineBreak();
                }
                else
                {
                    output.Write(" ", baseStyle);
                }

                break;
            case HtmlInline html:
                output.Write(html.Tag.AsSpan(), state.Theme.Border);
                break;
            case Markdig.Extensions.TaskLists.TaskList task:
                output.Write(task.Checked ? "[x] " : "[ ] ", task.Checked ? state.Theme.Success : state.Theme.Border);
                break;
            case ContainerInline nested:
                RenderInlines(nested, baseStyle, ref state, ref output);
                break;
        }
    }

    private static void RenderEmphasis(EmphasisInline emphasis, Style baseStyle, ref RenderState state, ref SegmentWriter output)
    {
        var attributes = emphasis.DelimiterChar == '~'
            ? baseStyle.Attributes | TextAttributes.Strikethrough
            : emphasis.DelimiterCount switch
            {
                1 => baseStyle.Attributes | TextAttributes.Italic,
                2 => baseStyle.Attributes | TextAttributes.Bold,
                3 => baseStyle.Attributes | TextAttributes.Bold | TextAttributes.Italic,
                _ => baseStyle.Attributes
            };

        RenderInlines(emphasis, new Style(baseStyle.Foreground, baseStyle.Background, attributes), ref state, ref output);
    }

    private static void RenderHighlightedCode(ReadOnlySpan<char> line, ReadOnlySpan<char> language, ref RenderState state, ref SegmentWriter output)
    {
        var pos = 0;
        var commentPrefix = GetCommentPrefix(language);

        while (pos < line.Length)
        {
            if (!commentPrefix.IsEmpty && line[pos..].StartsWith(commentPrefix, StringComparison.Ordinal))
            {
                output.Write(line[pos..], state.Theme.Border);
                return;
            }

            var ch = line[pos];
            if (char.IsWhiteSpace(ch))
            {
                output.Write(line[pos..(pos + 1)], state.Theme.Text);
                pos++;
                continue;
            }

            if (ch is '"' or '\'' or '`')
            {
                var end = FindQuotedStringEnd(line, pos, ch);
                output.Write(line[pos..end], state.Theme.Warning);
                pos = end;
                continue;
            }

            if (char.IsAsciiDigit(ch) || (ch == '-' && pos + 1 < line.Length && char.IsAsciiDigit(line[pos + 1])))
            {
                var end = pos + 1;
                while (end < line.Length && IsNumberChar(line[end]))
                {
                    end++;
                }

                output.Write(line[pos..end], state.Theme.Success);
                pos = end;
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                var end = pos + 1;
                while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '_'))
                {
                    end++;
                }

                var word = line[pos..end];
                output.Write(word, IsKeyword(word, language) ? state.Theme.Accent : state.Theme.Text);
                pos = end;
                continue;
            }

            output.Write(line[pos..(pos + 1)], IsPunctuation(ch) ? state.Theme.Border : state.Theme.Text);
            pos++;
        }
    }

    private static void RenderRule(ref RenderState state, ref SegmentWriter output)
    {
        Span<char> buffer = stackalloc char[Math.Min(Math.Max(state.MaxWidth, 1), 120)];
        buffer.Fill('─');
        output.Write(buffer, state.Theme.Border);
    }

    private static ReadOnlySpan<char> TrimMarkdownPrefix(ReadOnlySpan<char> line)
    {
        var trimmed = line.TrimStart();

        if (trimmed.StartsWith("### ", StringComparison.Ordinal))
        {
            return trimmed[4..];
        }

        if (trimmed.StartsWith("## ", StringComparison.Ordinal))
        {
            return trimmed[3..];
        }

        if (trimmed.StartsWith("# ", StringComparison.Ordinal) ||
            trimmed.StartsWith("- ", StringComparison.Ordinal) ||
            trimmed.StartsWith("* ", StringComparison.Ordinal) ||
            trimmed.StartsWith("+ ", StringComparison.Ordinal) ||
            trimmed.StartsWith("> ", StringComparison.Ordinal))
        {
            return trimmed[2..];
        }

        return line;
    }

    private static ReadOnlySpan<char> GetCommentPrefix(ReadOnlySpan<char> language)
    {
        if (EqualsLanguage(language, "python") || EqualsLanguage(language, "py") ||
            EqualsLanguage(language, "bash") || EqualsLanguage(language, "sh") ||
            EqualsLanguage(language, "shell") || EqualsLanguage(language, "zsh") ||
            EqualsLanguage(language, "yaml") || EqualsLanguage(language, "yml"))
        {
            return "#";
        }

        if (EqualsLanguage(language, "sql"))
        {
            return "--";
        }

        return "//";
    }

    private static bool IsKeyword(ReadOnlySpan<char> word, ReadOnlySpan<char> language)
    {
        if (EqualsLanguage(language, "python") || EqualsLanguage(language, "py"))
        {
            return IsOneOf(word, "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "elif", "else", "except", "finally", "for", "from", "if", "import", "in", "is", "lambda", "not", "or", "pass", "raise", "return", "try", "while", "with", "yield");
        }

        if (EqualsLanguage(language, "javascript") || EqualsLanguage(language, "js") ||
            EqualsLanguage(language, "typescript") || EqualsLanguage(language, "ts"))
        {
            return IsOneOf(word, "async", "await", "break", "case", "catch", "class", "const", "continue", "default", "else", "export", "extends", "false", "finally", "for", "function", "if", "import", "in", "let", "new", "null", "return", "switch", "this", "throw", "true", "try", "typeof", "undefined", "var", "while");
        }

        if (EqualsLanguage(language, "sql"))
        {
            return IsOneOfIgnoreCase(word, "SELECT", "FROM", "WHERE", "INSERT", "UPDATE", "DELETE", "CREATE", "DROP", "ALTER", "TABLE", "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "ON", "AND", "OR", "NOT", "NULL", "ORDER", "BY", "GROUP", "LIMIT", "AS");
        }

        return IsOneOf(word, "abstract", "as", "base", "bool", "break", "case", "catch", "class", "const", "continue", "default", "delegate", "do", "else", "enum", "event", "false", "finally", "for", "foreach", "if", "in", "int", "interface", "internal", "is", "lock", "namespace", "new", "null", "object", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sealed", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "using", "var", "void", "while", "async", "await", "record", "yield");
    }

    private static bool IsOneOf(ReadOnlySpan<char> word, params string[] values)
    {
        foreach (var value in values)
        {
            if (word.Equals(value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsOneOfIgnoreCase(ReadOnlySpan<char> word, params string[] values)
    {
        foreach (var value in values)
        {
            if (word.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool EqualsLanguage(ReadOnlySpan<char> language, string value) =>
        language.Equals(value, StringComparison.OrdinalIgnoreCase);

    private static bool IsNumberChar(char ch) =>
        char.IsAsciiDigit(ch) || ch is '.' or 'e' or 'E' or 'f' or 'F' or 'd' or 'D' or 'l' or 'L' or 'm' or 'M' or 'x' or 'X' ||
        (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');

    private static bool IsPunctuation(char ch) => "+-*/%=<>!&|^~?:;,.()[]{}".Contains(ch);

    private static int FindQuotedStringEnd(ReadOnlySpan<char> line, int start, char delimiter)
    {
        var pos = start + 1;
        while (pos < line.Length)
        {
            if (line[pos] == '\\' && pos + 1 < line.Length)
            {
                pos += 2;
                continue;
            }

            if (line[pos] == delimiter)
            {
                return pos + 1;
            }

            pos++;
        }

        return line.Length;
    }

    private static LineEnumerable EnumerateLines(ReadOnlySpan<char> source) => new(source);

    private readonly ref struct LineEnumerable
    {
        private readonly ReadOnlySpan<char> _source;

        public LineEnumerable(ReadOnlySpan<char> source)
        {
            _source = source;
        }

        public LineEnumerator GetEnumerator() => new(_source);
    }

    private ref struct LineEnumerator
    {
        private ReadOnlySpan<char> _remaining;

        public LineEnumerator(ReadOnlySpan<char> source)
        {
            _remaining = source;
            Current = default;
        }

        public ReadOnlySpan<char> Current { get; private set; }

        public bool MoveNext()
        {
            if (_remaining.IsEmpty)
            {
                return false;
            }

            var next = _remaining.IndexOf('\n');
            if (next < 0)
            {
                Current = _remaining.TrimEnd('\r');
                _remaining = [];
                return true;
            }

            Current = _remaining[..next].TrimEnd('\r');
            _remaining = _remaining[(next + 1)..];
            return true;
        }
    }

    private readonly ref struct RenderState
    {
        public RenderState(Theme theme, int maxWidth, int maxHeight)
        {
            Theme = theme;
            MaxWidth = maxWidth;
            MaxHeight = maxHeight;
        }

        public Theme Theme { get; }

        public int MaxWidth { get; }

        public int MaxHeight { get; }
    }

    private sealed record MarkdownTableModel(IReadOnlyList<MarkdownTableRow> Rows, int ColumnCount);

    private sealed record MarkdownTableRow(IReadOnlyList<string> Cells)
    {
        public string GetCell(int index) => index >= 0 && index < Cells.Count ? Cells[index] : string.Empty;
    }

    private sealed record MarkdownTableLayout(int MaxWidth, int[]? Widths, MarkdownWrappedTableRow[] Rows);

    private sealed record MarkdownWrappedTableRow(string[][] Cells, int Height);
}
