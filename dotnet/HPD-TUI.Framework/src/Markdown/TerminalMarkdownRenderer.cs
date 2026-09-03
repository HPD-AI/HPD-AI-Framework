using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;
using Markdig.Extensions.Tables;
using Markdig.Renderers;
using Markdig.Syntax;

namespace HPD.TUI.Markdown;

/// <summary>Dispatches Markdig syntax nodes through the frozen terminal renderer family.</summary>
internal sealed class TerminalMarkdownRenderer : RendererBase
{
    private readonly MarkdownDocumentSnapshot _document;
    private readonly MarkdownLayoutOptions _options;
    private MarkdownBlockLayout? _result;

    internal TerminalMarkdownRenderer(MarkdownDocumentSnapshot document, MarkdownLayoutOptions options)
    {
        _document = document;
        _options = options;
        // Extension nodes precede core base types. The literal fallback is last.
        ObjectRenderers.Add(new TerminalObjectRenderer<Table>());
        ObjectRenderers.Add(new TerminalObjectRenderer<HeadingBlock>());
        ObjectRenderers.Add(new TerminalObjectRenderer<ParagraphBlock>());
        ObjectRenderers.Add(new TerminalObjectRenderer<FencedCodeBlock>());
        ObjectRenderers.Add(new TerminalObjectRenderer<CodeBlock>());
        ObjectRenderers.Add(new TerminalObjectRenderer<ListBlock>());
        ObjectRenderers.Add(new TerminalObjectRenderer<QuoteBlock>());
        ObjectRenderers.Add(new TerminalObjectRenderer<ThematicBreakBlock>());
        ObjectRenderers.Add(new TerminalObjectRenderer<HtmlBlock>());
        ObjectRenderers.Add(new TerminalObjectRenderer<LeafBlock>());
        ObjectRenderers.Add(new LiteralFallbackRenderer());
    }

    /// <inheritdoc />
    public override object Render(MarkdownObject markdownObject)
    {
        ArgumentNullException.ThrowIfNull(markdownObject);
        _result = null;
        Write(markdownObject);
        return _result ?? throw new InvalidOperationException(
            $"Terminal renderer did not produce a layout for '{markdownObject.GetType().Name}'.");
    }

    internal MarkdownBlockLayout RenderBlock(MarkdownTopLevelBlock block)
    {
        _result = null;
        Write(block.Syntax);
        return _result ?? throw new InvalidOperationException(
            $"Terminal renderer did not produce block {block.Ordinal}.");
    }

    internal void RenderKnown(Block block) => _result = Capture(block, rich: true);

    internal void RenderLiteral(MarkdownObject markdownObject)
    {
        if (markdownObject is not Block block)
            throw new NotSupportedException($"Top-level inline '{markdownObject.GetType().Name}' cannot be laid out independently.");
        _result = Capture(block, rich: false);
    }

    private MarkdownBlockLayout Capture(Block block, bool rich)
    {
        var span = NormalizeSpan(block);
        var source = _document.Source[span.Start..span.EndExclusive];
        var theme = _options.Theme.ToFrameworkTheme();
        IComponent component = rich && _options.Mode == MarkdownPresentationMode.Rich
            ? new MarkdownLayoutComponent(_document.Source, _document.Syntax, theme, block, _options.Theme, new BasicCodeHighlighter())
            : new Text(TerminalTextSanitizer.Sanitize(source), theme.Text);
        var estimatedHeight = source.Length >= 8_188 ? 16_384 : Math.Max(8, 8 + (source.Length * 2));
        using var grid = TuiCapture.RenderToGrid(component, _options.Width, estimatedHeight, theme, _options.ColorSystem);
        var used = TuiCapture.GetUsedLineCount(grid);
        var lines = System.Collections.Immutable.ImmutableArray.CreateBuilder<StyledTerminalLine>(used);
        for (var y = 0; y < used; y++) lines.Add(MarkdownLayoutEngine.CaptureLine(grid, y));
        return new MarkdownBlockLayout
        {
            SourceStart = span.Start,
            SourceEndExclusive = span.EndExclusive,
            Lines = lines.ToImmutable()
        };
    }

    private (int Start, int EndExclusive) NormalizeSpan(MarkdownObject markdownObject)
    {
        var start = Math.Clamp(markdownObject.Span.Start, 0, _document.Source.Length);
        var inclusiveEnd = Math.Clamp(markdownObject.Span.End, start - 1, _document.Source.Length - 1);
        return (start, inclusiveEnd < start ? start : inclusiveEnd + 1);
    }
}

internal sealed class TerminalObjectRenderer<TNode>
    : MarkdownObjectRenderer<TerminalMarkdownRenderer, TNode>
    where TNode : Block
{
    protected override void Write(TerminalMarkdownRenderer renderer, TNode obj) => renderer.RenderKnown(obj);
}

internal sealed class LiteralFallbackRenderer
    : MarkdownObjectRenderer<TerminalMarkdownRenderer, MarkdownObject>
{
    protected override void Write(TerminalMarkdownRenderer renderer, MarkdownObject obj) => renderer.RenderLiteral(obj);
}
