using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Markdown;

namespace HPD.TUI.Content;

/// <summary>Convenience content block that prepares a complete Markdown document for ordinary non-streaming use.</summary>
public sealed class MarkdownBlock : IContentBlock
{
    private static readonly MarkdownPipelineDescriptor Pipeline = MarkdownPipelineFactory.CreateDefault();
    private static readonly IMarkdownDocumentParser Parser = new MarkdownDocumentParser();
    private static readonly IMarkdownLayoutEngine LayoutEngine = new MarkdownLayoutEngine();
    private readonly MarkdownDocumentSnapshot _document;
    private MarkdownView? _view;
    private MarkdownLayoutKey _viewKey;

    /// <summary>Creates a complete source-backed Markdown content block.</summary>
    public MarkdownBlock(string source, Theme? theme = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        ThemeOverride = theme;
        _document = Parser.Parse(Source, new MarkdownParseOptions { Pipeline = Pipeline });
    }

    /// <inheritdoc />
    public ContentBlockKind Kind => ContentBlockKind.Markdown;

    /// <summary>Gets the exact canonical source.</summary>
    public string Source { get; }

    /// <summary>Gets the optional presentation theme override.</summary>
    public Theme? ThemeOverride { get; }

    /// <summary>Gets the parsed immutable document snapshot.</summary>
    public MarkdownDocumentSnapshot Document => _document;

    /// <inheritdoc />
    public Measurement Measure(in RenderContext context, int maxWidth) => GetView(in context, maxWidth).Measure(in context, maxWidth);

    /// <inheritdoc />
    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output) => GetView(in context, maxWidth).Render(in context, maxWidth, ref output);

    /// <inheritdoc />
    public bool HandleInput(in TuiInputEvent key)
    {
        return false;
    }

    /// <summary>Creates a complete source-backed Markdown content block.</summary>
    public static MarkdownBlock Create(string source, Theme? theme = null) => new(source, theme);

    private MarkdownView GetView(in RenderContext context, int width)
    {
        var theme = ThemeOverride ?? context.Theme;
        var markdownTheme = MarkdownTheme.FromTheme(theme);
        var key = new MarkdownLayoutKey(_document.PipelineId, "terminal-v1", width, theme.Key, context.ColorSystem, MarkdownPresentationMode.Rich, 0);
        if (_view is not null && _viewKey == key) return _view;
        var layout = LayoutEngine.Layout(_document, new MarkdownLayoutOptions(width, markdownTheme, context.ColorSystem));
        _viewKey = layout.Key;
        return _view = new MarkdownView(layout);
    }
}
