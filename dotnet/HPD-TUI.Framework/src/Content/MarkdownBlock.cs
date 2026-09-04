using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Markdown;

namespace HPD.TUI.Content;

/// <summary>Content block over a Markdown layout prepared at a publication boundary.</summary>
public sealed class MarkdownBlock : Component, IContentBlock
{
    private readonly MarkdownView _view;

    private MarkdownBlock(string source, MarkdownDocumentSnapshot document, MarkdownLayout layout)
    {
        Source = source;
        Document = document;
        Layout = layout;
        _view = new(layout);
    }

    /// <inheritdoc />
    public ContentBlockKind Kind => ContentBlockKind.Markdown;

    /// <summary>Gets the exact canonical source.</summary>
    public string Source { get; }

    /// <summary>Gets the parsed immutable document snapshot.</summary>
    public MarkdownDocumentSnapshot Document { get; }

    /// <summary>Gets the immutable prepared terminal layout.</summary>
    public MarkdownLayout Layout { get; }

    /// <inheritdoc />
    public override Measurement Measure(in RenderContext context, int maxWidth) => _view.Measure(in context, maxWidth);

    /// <inheritdoc />
    public override void Render(in RenderContext context, int maxWidth, ref SegmentWriter output) => _view.Render(in context, maxWidth, ref output);

    /// <inheritdoc />
    public override bool HandleInput(in TuiInputEvent key)
    {
        return false;
    }

    /// <summary>Parses and prepares a complete document for an exact render context.</summary>
    /// <remarks>Invoke this at a publication or frame-preparation boundary, never from component measurement or rendering.</remarks>
    public static MarkdownBlock Prepare(string source, int width, Theme theme,
        ColorSystem colorSystem = ColorSystem.TrueColor, MarkdownSpacing? spacing = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        var pipeline = MarkdownPipelineFactory.CreateDefault();
        var document = new MarkdownDocumentParser().Parse(source, new MarkdownParseOptions { Pipeline = pipeline });
        var layout = new MarkdownLayoutEngine().Layout(document,
            new MarkdownLayoutOptions(width, MarkdownTheme.FromTheme(theme), colorSystem, Spacing: spacing));
        return new(source, document, layout);
    }
}
