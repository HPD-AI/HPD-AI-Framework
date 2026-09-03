using System.Collections.Immutable;
using System.Text;
using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

namespace HPD.TUI.Markdown;

/// <summary>Specifies all semantic inputs to Markdown layout.</summary>
public readonly record struct MarkdownLayoutOptions(
    int Width,
    MarkdownTheme Theme,
    ColorSystem ColorSystem = ColorSystem.TrueColor,
    MarkdownPresentationMode Mode = MarkdownPresentationMode.Rich,
    long SyntaxThemeRevision = 0,
    MarkdownSpacing? Spacing = null);

/// <summary>Prepares immutable Markdown layouts outside component measurement and rendering.</summary>
public interface IMarkdownLayoutEngine
{
    /// <summary>Lays out a parsed document.</summary>
    MarkdownLayout Layout(MarkdownDocumentSnapshot document, MarkdownLayoutOptions options);
    /// <summary>Lays out one selected block with its full document context available.</summary>
    MarkdownBlockLayout LayoutBlock(MarkdownDocumentSnapshot document, MarkdownTopLevelBlock block, MarkdownLayoutOptions options);
}

/// <summary>Default terminal Markdown layout engine.</summary>
public sealed class MarkdownLayoutEngine : IMarkdownLayoutEngine
{
    private const int MaximumLayoutHeight = 16_384;

    /// <inheritdoc />
    public MarkdownLayout Layout(MarkdownDocumentSnapshot document, MarkdownLayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(options);
        var blockLayouts = ImmutableArray.CreateBuilder<MarkdownBlockLayout>(document.Blocks.Count);
        var rows = ImmutableArray.CreateBuilder<MarkdownLayoutRow>();
        MarkdownTopLevelBlock? previous = null;
        foreach (var block in document.Blocks)
        {
            var blockLayout = LayoutBlock(document, block, options);
            if (rows.Count > 0)
                for (var gap = 0; gap < GetSeparatorRows(previous!, block, options.Spacing ?? new MarkdownSpacing()); gap++)
                    rows.Add(new(MarkdownLayoutRowKind.Separator, StyledTerminalLine.Empty, null, null, null, true));
            foreach (var line in blockLayout.Lines)
                rows.Add(new(MarkdownLayoutRowKind.BlockContent, line, block.Ordinal, block.SourceStart, block.SourceEndExclusive, false));
            blockLayouts.Add(blockLayout);
            previous = block;
        }

        return new MarkdownLayout
        {
            Key = CreateKey(document, options),
            Blocks = blockLayouts.ToImmutable(),
            Rows = rows.ToImmutable()
        };
    }

    /// <inheritdoc />
    public MarkdownBlockLayout LayoutBlock(MarkdownDocumentSnapshot document, MarkdownTopLevelBlock block, MarkdownLayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(block);
        Validate(options);
        var renderer = new TerminalMarkdownRenderer(document, options);
        return renderer.RenderBlock(block);
    }

    private static MarkdownLayoutKey CreateKey(MarkdownDocumentSnapshot document, MarkdownLayoutOptions options) =>
        new(document.PipelineId, "terminal-v1", options.Width, options.Theme.ThemeKey, options.ColorSystem, options.Mode,
            options.SyntaxThemeRevision, (options.Spacing ?? new MarkdownSpacing()).Key);

    /// <summary>Gets document-owned separator rows for an adjacent top-level block pair.</summary>
    public static int GetSeparatorRows(MarkdownTopLevelBlock previous, MarkdownTopLevelBlock current, MarkdownSpacing spacing)
    {
        if (previous.Kind == MarkdownBlockKind.Html || current.Kind == MarkdownBlockKind.Html) return 0;
        if (current.Kind == MarkdownBlockKind.Heading) return spacing.HeadingTopGap;
        if (previous.Kind == MarkdownBlockKind.Heading) return spacing.HeadingBottomGap;
        if (previous.Kind == MarkdownBlockKind.ThematicBreak || current.Kind == MarkdownBlockKind.ThematicBreak) return 0;
        return spacing.ParagraphGap;
    }

    private static void Validate(MarkdownLayoutOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Width);
        ArgumentNullException.ThrowIfNull(options.Theme);
    }

    internal static StyledTerminalLine CaptureLine(TerminalGrid grid, int y)
    {
        var runs = ImmutableArray.CreateBuilder<StyledTerminalRun>();
        StringBuilder? text = null;
        Style style = default;
        TerminalHyperlink? link = null;
        for (var x = 0; x < grid.Width; x++)
        {
            var cell = grid.GetCell(x, y);
            if (cell.IsContinuation) continue;
            var cellLink = grid.GetHyperlink(cell);
            if (text is null || cell.Style != style || cellLink != link)
            {
                if (text is not null) runs.Add(new(text.ToString(), style, link));
                text = new StringBuilder();
                style = cell.Style;
                link = cellLink;
            }
            text.Append(grid.GetGrapheme(cell));
        }
        if (text is not null) runs.Add(new(text.ToString().TrimEnd(), style, link));
        return new(runs.ToImmutable());
    }
}

internal static class TerminalTextSanitizer
{
    internal static string Sanitize(string source)
    {
        source = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        StringBuilder? builder = null;
        for (var index = 0; index < source.Length; index++)
        {
            var ch = source[index];
            var safe = ch is '\n' or '\t' || !TerminalTextSafety.IsUnsafe(ch);
            if (builder is null)
            {
                if (safe) continue;
                builder = new StringBuilder(source.Length).Append(source, 0, index);
            }
            builder.Append(safe ? ch : '�');
        }
        return builder?.ToString() ?? source;
    }
}
