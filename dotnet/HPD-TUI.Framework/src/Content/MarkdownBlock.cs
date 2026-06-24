using HPD.TUI.Components;
using HPD.TUI.Core;
using Markdig;
using Markdig.Syntax;

namespace HPD.TUI.Content;

public sealed class MarkdownBlock : IContentBlock
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private Components.Markdown _component;

    public MarkdownBlock(string source, Theme? theme = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        ThemeOverride = theme;
        Document = Markdig.Markdown.Parse(Source, Pipeline);
        _component = new Components.Markdown(Source, theme);
    }

    public ContentBlockKind Kind => ContentBlockKind.Markdown;

    public string Source { get; private set; }

    public Theme? ThemeOverride { get; }

    public MarkdownDocument Document { get; private set; }

    public void SetSource(string source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Document = Markdig.Markdown.Parse(Source, Pipeline);
        _component = new Components.Markdown(Source, ThemeOverride);
    }

    public Measurement Measure(in RenderContext context, int maxWidth) => _component.Measure(in context, maxWidth);

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output) => _component.Render(in context, maxWidth, ref output);

    public bool HandleInput(in TuiInputEvent key)
    {
        return false;
    }

    public static MarkdownBlock Create(string source, Theme? theme = null) => new(source, theme);
}
