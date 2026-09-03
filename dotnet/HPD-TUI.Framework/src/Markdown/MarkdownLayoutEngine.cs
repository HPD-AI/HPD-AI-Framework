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
    MarkdownSpacing? Spacing = null,
    MarkdownResourceLimits? ResourceLimits = null);

/// <summary>Bounds resource-intensive Markdown presentation work without changing canonical source.</summary>
public sealed record MarkdownResourceLimits
{
    /// <summary>Gets the largest source eligible for rich layout.</summary>
    public int MaximumRichSourceLength { get; init; } = 1_048_576;
    /// <summary>Gets the maximum number of materialized terminal rows.</summary>
    public int MaximumLayoutRows { get; init; } = 16_384;
    /// <summary>Gets the largest table eligible for structured terminal layout.</summary>
    public int MaximumTableColumns { get; init; } = 64;
    /// <summary>Gets the largest table cell count eligible for structured terminal layout.</summary>
    public int MaximumTableCells { get; init; } = 4_096;
    /// <summary>Gets the largest code block eligible for syntax highlighting.</summary>
    public int MaximumHighlightedCodeLength { get; init; } = 262_144;

    /// <summary>Gets the structural cache identity for these limits.</summary>
    public MarkdownResourceLimitsKey Key => new(
        MaximumRichSourceLength, MaximumLayoutRows, MaximumTableColumns,
        MaximumTableCells, MaximumHighlightedCodeLength);
}

/// <summary>Structurally identifies all behavior-affecting Markdown resource limits.</summary>
public readonly record struct MarkdownResourceLimitsKey(
    int MaximumRichSourceLength,
    int MaximumLayoutRows,
    int MaximumTableColumns,
    int MaximumTableCells,
    int MaximumHighlightedCodeLength);

/// <summary>Prepares immutable Markdown layouts outside component measurement and rendering.</summary>
public interface IMarkdownLayoutEngine
{
    /// <summary>Lays out a parsed document.</summary>
    MarkdownLayout Layout(MarkdownDocumentSnapshot document, MarkdownLayoutOptions options);
    /// <summary>Lays out exact canonical source as sanitized literal text without AST reconstruction.</summary>
    MarkdownLayout LayoutRaw(string canonicalSource, string pipelineId, MarkdownLayoutOptions options);
    /// <summary>Lays out one selected block with its full document context available.</summary>
    MarkdownBlockLayout LayoutBlock(MarkdownDocumentSnapshot document, MarkdownTopLevelBlock block, MarkdownLayoutOptions options);
}

/// <summary>Default terminal Markdown layout engine.</summary>
public sealed class MarkdownLayoutEngine : IMarkdownLayoutEngine
{
    /// <inheritdoc />
    public MarkdownLayout Layout(MarkdownDocumentSnapshot document, MarkdownLayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(options);
        var limits = options.ResourceLimits ?? new MarkdownResourceLimits();
        if (options.Mode == MarkdownPresentationMode.Rich && document.Source.Length > limits.MaximumRichSourceLength)
            return LayoutLiteral(document.Source, document.PipelineId, options, MarkdownDegradationReason.SourceLength);
        if (options.Mode == MarkdownPresentationMode.Raw)
            return LayoutRaw(document.Source, document.PipelineId, options);
        var blockLayouts = ImmutableArray.CreateBuilder<MarkdownBlockLayout>(document.Blocks.Count);
        var rows = ImmutableArray.CreateBuilder<MarkdownLayoutRow>();
        MarkdownTopLevelBlock? previous = null;
        var degradationReason = MarkdownDegradationReason.None;
        foreach (var block in document.Blocks)
        {
            var blockLayout = LayoutBlock(document, block, options);
            if (rows.Count > 0)
                for (var gap = 0; gap < GetSeparatorRows(
                         previous!, block, options.Spacing ?? new MarkdownSpacing(), document.Source); gap++)
                    rows.Add(new(MarkdownLayoutRowKind.Separator, StyledTerminalLine.Empty, null, null, null, true));
            foreach (var line in blockLayout.Lines)
                rows.Add(new(MarkdownLayoutRowKind.BlockContent, line, block.Ordinal, block.SourceStart, block.SourceEndExclusive, false));
            blockLayouts.Add(blockLayout);
            if (degradationReason == MarkdownDegradationReason.None)
                degradationReason = blockLayout.DegradationReason;
            previous = block;
            if (rows.Count > limits.MaximumLayoutRows)
                return Simplified(document.PipelineId, options, MarkdownDegradationReason.LayoutRows);
        }

        return new MarkdownLayout
        {
            Key = CreateKey(document, options),
            Blocks = blockLayouts.ToImmutable(),
            Rows = rows.ToImmutable(),
            DegradationReason = degradationReason
        };
    }

    /// <inheritdoc />
    public MarkdownLayout LayoutRaw(string canonicalSource, string pipelineId, MarkdownLayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(canonicalSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineId);
        Validate(options);
        return LayoutLiteral(canonicalSource, pipelineId, options, MarkdownDegradationReason.None);
    }

    private static MarkdownLayout LayoutLiteral(
        string canonicalSource,
        string pipelineId,
        MarkdownLayoutOptions options,
        MarkdownDegradationReason degradationReason)
    {
        var limits = options.ResourceLimits ?? new MarkdownResourceLimits();
        var builder = new TerminalLayoutBuilder(options.Width, limits.MaximumLayoutRows);
        builder.Write(canonicalSource, options.Theme.Body, sourceStart: 0, sourceEndExclusive: canonicalSource.Length);
        if (builder.LimitExceeded)
            return Simplified(pipelineId, options, MarkdownDegradationReason.LayoutRows);
        var block = builder.Freeze(0, canonicalSource.Length);
        return new MarkdownLayout
        {
            Key = new(pipelineId, "terminal-v1", options.Width, options.Theme.ThemeKey, options.ColorSystem,
                options.Mode, options.SyntaxThemeRevision, (options.Spacing ?? new MarkdownSpacing()).Key, limits.Key),
            Blocks = [block],
            Rows = block.Lines.Select(line => new MarkdownLayoutRow(
                MarkdownLayoutRowKind.BlockContent, line, null, 0, canonicalSource.Length, false)).ToImmutableArray(),
            DegradationReason = degradationReason
        };
    }

    /// <inheritdoc />
    public MarkdownBlockLayout LayoutBlock(MarkdownDocumentSnapshot document, MarkdownTopLevelBlock block, MarkdownLayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(block);
        Validate(options);
        if (options.Mode == MarkdownPresentationMode.Raw)
            throw new InvalidOperationException("Raw presentation must be laid out from the complete canonical source.");
        try
        {
            var renderer = new TerminalMarkdownRenderer(document, options);
            return renderer.RenderBlock(block);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            var builder = new TerminalLayoutBuilder(options.Width,
                (options.ResourceLimits ?? new MarkdownResourceLimits()).MaximumLayoutRows);
            builder.Write(document.Source[block.SourceStart..block.SourceEndExclusive], options.Theme.Body,
                sourceStart: block.SourceStart, sourceEndExclusive: block.SourceEndExclusive);
            var fallback = builder.Freeze(block.SourceStart, block.SourceEndExclusive);
            return new MarkdownBlockLayout
            {
                SourceStart = fallback.SourceStart,
                SourceEndExclusive = fallback.SourceEndExclusive,
                Lines = fallback.Lines,
                DegradationReason = MarkdownDegradationReason.LayoutFailure
            };
        }
    }

    private static MarkdownLayoutKey CreateKey(MarkdownDocumentSnapshot document, MarkdownLayoutOptions options) =>
        new(document.PipelineId, "terminal-v1", options.Width, options.Theme.ThemeKey, options.ColorSystem, options.Mode,
            options.SyntaxThemeRevision, (options.Spacing ?? new MarkdownSpacing()).Key,
            (options.ResourceLimits ?? new MarkdownResourceLimits()).Key);

    private static MarkdownLayout Simplified(
        string pipelineId,
        MarkdownLayoutOptions options,
        MarkdownDegradationReason reason)
    {
        var limits = options.ResourceLimits ?? new MarkdownResourceLimits();
        var line = new StyledTerminalLine([new StyledTerminalRun(
            "[Markdown presentation simplified by resource policy]", options.Theme.CodeBorder, IsDecorative: true)]);
        return new MarkdownLayout
        {
            Key = new(pipelineId, "terminal-v1", options.Width, options.Theme.ThemeKey, options.ColorSystem,
                options.Mode, options.SyntaxThemeRevision, (options.Spacing ?? new MarkdownSpacing()).Key, limits.Key),
            Blocks = [],
            Rows = [new(MarkdownLayoutRowKind.BlockContent, line, null, null, null, true)],
            DegradationReason = reason
        };
    }

    /// <summary>Gets document-owned separator rows from an adjacent block pair and its exact source trivia.</summary>
    public static int GetSeparatorRows(
        MarkdownTopLevelBlock previous,
        MarkdownTopLevelBlock current,
        MarkdownSpacing spacing,
        string canonicalSource)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(spacing);
        ArgumentNullException.ThrowIfNull(canonicalSource);
        if (previous.Kind == MarkdownBlockKind.Html || current.Kind == MarkdownBlockKind.Html) return 0;
        if (previous.Kind == MarkdownBlockKind.ThematicBreak || current.Kind == MarkdownBlockKind.ThematicBreak) return 0;
        if (!HasBlankLine(canonicalSource, previous.SourceEndExclusive, current.SourceStart)) return 0;
        if (current.Kind == MarkdownBlockKind.Heading) return spacing.HeadingTopGap;
        if (previous.Kind == MarkdownBlockKind.Heading) return spacing.HeadingBottomGap;
        return spacing.ParagraphGap;
    }

    private static bool HasBlankLine(string source, int start, int endExclusive)
    {
        var lineBreaks = 0;
        for (var index = Math.Clamp(start, 0, source.Length);
             index < Math.Clamp(endExclusive, 0, source.Length);
             index++)
            if (source[index] == '\n' && ++lineBreaks >= 2) return true;
        return false;
    }

    private static void Validate(MarkdownLayoutOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Width);
        ArgumentNullException.ThrowIfNull(options.Theme);
        var limits = options.ResourceLimits ?? new MarkdownResourceLimits();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaximumRichSourceLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaximumLayoutRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaximumTableColumns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaximumTableCells);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaximumHighlightedCodeLength);
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
